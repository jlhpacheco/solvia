using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Solvia.Config;
using Solvia.Data;
using Solvia.Models;
using Solvia.Models.Enums;
using Solvia.Services;

namespace Solvia.Tests;

/// <summary>Two-tab purchase review contracts. Provider calls are local stubs, never HTTP.</summary>
public sealed class CheckoutSnapshotTests
{
    private sealed class LocalPayPal : IPayPalService
    {
        public int Creates, Lookups, Captures;
        public int? CreatedAmount;
        public Task<PayPalOrderResult> CreateOrderAsync(int amountCents, string reference, string? returnUrl = null,
            string? cancelUrl = null, CancellationToken cancellationToken = default)
        {
            Creates++; CreatedAmount = amountCents;
            return Task.FromResult(new PayPalOrderResult("OFFLINE-SNAPSHOT-ORDER", "CREATED",
                "https://www.sandbox.paypal.com/checkoutnow?token=OFFLINE-SNAPSHOT-ORDER"));
        }
        public Task<PayPalOrderResult> GetOrderAsync(string orderId, CancellationToken cancellationToken = default)
        {
            Lookups++;
            return Task.FromResult(new PayPalOrderResult(orderId, "CREATED",
                "https://www.sandbox.paypal.com/checkoutnow?token=OFFLINE-SNAPSHOT-ORDER"));
        }
        public Task<PayPalCaptureResult> CaptureOrderAsync(string orderId, CancellationToken cancellationToken = default)
        {
            Captures++;
            return Task.FromResult(new PayPalCaptureResult("COMPLETED", "OFFLINE-SNAPSHOT-CAPTURE", CreatedAmount, "USD"));
        }
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("No network.");
        public Task<bool> VerifyWebhookSignatureAsync(string a, string b, string c, string d, string e, string f,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("No network.");
    }

    private static WebApplicationFactory<Program> Factory(AppIntegrationTests.DemoFactory parent, LocalPayPal provider) =>
        parent.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IPayPalService>();
            services.AddSingleton<IPayPalService>(provider);
            services.PostConfigure<AiOptions>(options => options.Provider = "ollama");
            services.PostConfigure<TogetherOptions>(options => options.ApiKey = "");
            services.PostConfigure<OllamaOptions>(options => options.BaseUrl = "http://127.0.0.1:1/");
        }));

    private static ApplicationService Applications(SolviaDbContext db, IFeeCatalog catalog) => new(
        db, catalog, new PricingService(catalog), new UserCategoryResolver(Options.Create(new CategoryOptions())),
        new TermDatesService(catalog, db), new IssuanceService(new PermitNumberService(db)));

    private static async Task<PermitApplication> Draft(SolviaDbContext db, ApplicationService service)
    {
        var vehicle = await service.ResolveVehicleForDraftAsync("maria", "P" + Guid.NewGuid().ToString("N")[..6],
            "Honda", "Civic", "Blue", "CA");
        var app = (await service.CreateAsync(new ApplicationInput("maria", vehicle.Id, "Fresno City College",
            PermitTypeCode.Daily, TermKind.Fall, new DateOnly(2026, 10, 20), "fallback", true))).Application!;
        await db.Entry(app).Reference(a => a.Vehicle).LoadAsync();
        await db.Entry(app).Reference(a => a.PermitType).LoadAsync();
        return app;
    }

    private static async Task Change(SolviaDbContext db, ApplicationService service, Guid id, string change)
    {
        var app = await db.PermitApplications.Include(a => a.Vehicle).Include(a => a.PermitType).SingleAsync(a => a.Id == id);
        if (change == "vehicle-details")
        {
            app.Vehicle!.Make = "Volvo"; app.Vehicle.Model = "760"; app.Vehicle.Color = VehicleColor.White;
            await db.SaveChangesAsync();
            return;
        }
        var vehicleId = change == "vehicle"
            ? (await service.ResolveVehicleForDraftAsync("maria", "Q" + Guid.NewGuid().ToString("N")[..6], "Toyota", "Camry", "White", "CA")).Id
            : app.VehicleId;
        var result = await service.ReviseDraftAsync(id, new ApplicationInput("maria", vehicleId, app.Campus,
            change == "amount" ? PermitTypeCode.Term : PermitTypeCode.Daily, app.Term,
            change == "date" ? app.StartDate.AddDays(1) : app.StartDate, app.Source, true));
        Assert.Equal(CreateApplicationStatus.Created, result.Status);
    }

    private static async Task<(string Snapshot, string Csrf)> Review(HttpClient client, Guid id)
    {
        var html = await client.GetStringAsync($"/Permits/Review/{id}");
        var snapshot = Regex.Match(html, "data-reviewed-snapshot=\"([^\"]+)\"").Groups[1].Value;
        var csrf = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        Assert.Matches("^[a-f0-9]{64}$", snapshot);
        Assert.NotEmpty(csrf);
        return (snapshot, csrf);
    }

    private static async Task<HttpResponseMessage> Start(HttpClient client, Guid id, string? snapshot, string csrf)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/paypal/orders")
        {
            Content = JsonContent.Create(new { permitApplicationId = id, reviewedSnapshot = snapshot })
        };
        request.Headers.Add("RequestVerificationToken", csrf);
        return await client.SendAsync(request);
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("vehicle")]
    [InlineData("date")]
    [InlineData("vehicle-details")]
    public async Task OtherTabRevision_RejectsStaleSdkAndFallback_RefreshedReviewCanStart(string change)
    {
        using var parent = new AppIntegrationTests.DemoFactory();
        var provider = new LocalPayPal();
        using var factory = Factory(parent, provider);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Guid id;
        using (var firstTab = factory.Services.CreateScope())
            id = (await Draft(firstTab.ServiceProvider.GetRequiredService<SolviaDbContext>(),
                firstTab.ServiceProvider.GetRequiredService<ApplicationService>())).Id;
        var first = await Review(client, id);
        using (var otherTab = factory.Services.CreateScope())
            await Change(otherTab.ServiceProvider.GetRequiredService<SolviaDbContext>(),
                otherTab.ServiceProvider.GetRequiredService<ApplicationService>(), id, change);

        var stale = await Start(client, id, first.Snapshot, first.Csrf);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Contains("REVIEW_CHANGED", await stale.Content.ReadAsStringAsync());
        var fallback = await client.PostAsync($"/Checkout/Start/{id}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = first.Csrf, ["reviewedSnapshot"] = first.Snapshot
        }));
        Assert.Equal(HttpStatusCode.Conflict, fallback.StatusCode);
        var recovery = await fallback.Content.ReadAsStringAsync();
        Assert.Contains("Review the current total", recovery);
        Assert.Contains($"href=\"/Permits/Review/{id}\"", recovery);
        Assert.Equal(0, provider.Creates + provider.Lookups + provider.Captures);
        var fresh = await Review(client, id);
        Assert.NotEqual(first.Snapshot, fresh.Snapshot);
        var started = await Start(client, id, fresh.Snapshot, fresh.Csrf);
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        Assert.Contains("OFFLINE-SNAPSHOT-ORDER", await started.Content.ReadAsStringAsync());
        Assert.Equal(change == "amount" ? 3000 : 100, provider.CreatedAmount);
        var repeated = await Start(client, id, fresh.Snapshot, fresh.Csrf);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(1, provider.Creates);
        Assert.Equal(1, provider.Lookups);
        Assert.Equal(0, provider.Captures);
    }

    [Fact]
    public async Task MissingOrMalformedReview_RejectsBeforeAnyProviderCall()
    {
        using var parent = new AppIntegrationTests.DemoFactory();
        var provider = new LocalPayPal();
        using var factory = Factory(parent, provider);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Guid id;
        using (var scope = factory.Services.CreateScope())
            id = (await Draft(scope.ServiceProvider.GetRequiredService<SolviaDbContext>(),
                scope.ServiceProvider.GetRequiredService<ApplicationService>())).Id;
        var view = await Review(client, id);
        foreach (var value in new string?[] { null, "", "not-a-reviewed-snapshot" })
            Assert.Equal(HttpStatusCode.Conflict, (await Start(client, id, value, view.Csrf)).StatusCode);
        Assert.Equal(0, provider.Creates + provider.Lookups + provider.Captures);
    }

    [Fact]
    public async Task SameReviewedPurchase_RetainsItsFingerprintAcrossActualLocalCaptureAndPaidRetry()
    {
        var (db, connection) = RedoTestDb.Create();
        using var _ = connection;
        using var __ = db;
        var catalog = new FeeCatalogService(RedoTestDb.FindFeesCsv());
        var app = await Draft(db, Applications(db, catalog));
        var reviewed = CheckoutReviewSnapshot.Compute(app);
        var provider = new LocalPayPal();
        var checkout = new CheckoutService(db, provider, new IssuanceService(new PermitNumberService(db)),
            catalog, new PricingService(catalog), NullLogger<CheckoutService>.Instance);
        await checkout.StartAsync(app.Id, null, null, reviewedSnapshot: reviewed);
        Assert.Equal(reviewed, CheckoutReviewSnapshot.Compute(app));
        Assert.True((await checkout.CaptureAsync(app.Id)).Completed);
        Assert.Equal(reviewed, CheckoutReviewSnapshot.Compute(app));
        var repeat = await checkout.StartAsync(app.Id, null, null, reviewedSnapshot: reviewed);
        Assert.Equal(CheckoutStartKind.AlreadyPaid, repeat.Kind);
        Assert.Equal(1, provider.Creates);
        Assert.Equal(1, provider.Captures);
        Assert.Equal(0, provider.Lookups);
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("date")]
    [InlineData("vehicle")]
    [InlineData("vehicle-details")]
    public async Task WarmTrackedContext_RefreshesSeparatelyPersistedPurchaseAndVehicleBeforeChecking(string change)
    {
        var (db, connection) = RedoTestDb.Create();
        using var _ = connection;
        using var __ = db;
        var catalog = new FeeCatalogService(RedoTestDb.FindFeesCsv());
        var app = await Draft(db, Applications(db, catalog));
        var old = CheckoutReviewSnapshot.Compute(app);
        using (var other = RedoTestDb.NewContext(connection))
            await Change(other, Applications(other, catalog), app.Id, change);
        Assert.Equal(old, CheckoutReviewSnapshot.Compute(app)); // still cached in this context
        var provider = new LocalPayPal();
        var checkout = new CheckoutService(db, provider, new IssuanceService(new PermitNumberService(db)),
            catalog, new PricingService(catalog), NullLogger<CheckoutService>.Instance);

        await Assert.ThrowsAsync<CheckoutReviewChangedException>(() => checkout.StartAsync(app.Id, null, null, reviewedSnapshot: old));

        Assert.NotEqual(old, CheckoutReviewSnapshot.Compute(app));
        Assert.Equal(0, provider.Creates + provider.Lookups + provider.Captures);
        Assert.Null(app.PayPalOrderId);
    }
}
