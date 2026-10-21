using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Solvia.Config;
using Solvia.Data;
using Solvia.Models;
using Solvia.Models.Enums;
using Solvia.Services;

namespace Solvia.Tests;

/// <summary>Actual Razor/DI/CSRF and quote guards; all provider operations are local stubs.</summary>
public sealed class ReviewPaymentPanelTests
{
    private const string PublicClient = "PUBLIC-SDK-CONTRACT";
    private const string PrivateSentinel = "offline-secret-must-never-enter-markup";

    private static WebApplicationFactory<Program> Factory(AppIntegrationTests.DemoFactory parent,
        MutableCatalog catalog, LocalPayPal provider, bool configured = true) =>
        parent.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["PayPal:ClientId"] = configured ? PublicClient : "",
                    ["PayPal:ClientSecret"] = PrivateSentinel,
                    ["AI:Provider"] = "ollama",
                    ["Together:ApiKey"] = "",
                    ["Together:Model"] = TogetherOptions.SupportedModel,
                    ["Ollama:BaseUrl"] = "http://127.0.0.1:1/",
                    ["Ollama:TimeoutSeconds"] = "1"
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IFeeCatalog>();
                services.AddSingleton<IFeeCatalog>(catalog);
                services.RemoveAll<IPayPalService>();
                services.AddSingleton<IPayPalService>(provider);
                // These final options also pin the test if a developer's
                // process environment bridges provider aliases at startup.
                // No test route invokes inference, and no private key is used.
                services.PostConfigure<AiOptions>(options => options.Provider = "ollama");
                services.PostConfigure<TogetherOptions>(options => options.ApiKey = "");
                services.PostConfigure<OllamaOptions>(options =>
                {
                    options.BaseUrl = "http://127.0.0.1:1/";
                    options.TimeoutSeconds = 1;
                });
            });
        });

    private static async Task<Guid> Draft(WebApplicationFactory<Program> factory, bool bound = false)
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ApplicationService>();
        var vehicle = await service.ResolveVehicleForDraftAsync("maria",
            "R" + Guid.NewGuid().ToString("N")[..6], "Honda", "Civic", "Blue", "CA");
        var result = await service.CreateAsync(new ApplicationInput("maria", vehicle.Id,
            "Fresno City College", PermitTypeCode.Term, TermKind.Fall,
            new DateOnly(2026, 8, 24), "fallback", true));
        Assert.Equal(CreateApplicationStatus.Created, result.Status);
        if (bound)
        {
            result.Application!.PayPalOrderId = "OFFLINE-EXISTING-ORDER";
            await scope.ServiceProvider.GetRequiredService<SolviaDbContext>().SaveChangesAsync();
        }
        return result.Application!.Id;
    }

    private static async Task<HttpResponseMessage> Confirm(HttpClient client, Guid id)
    {
        var html = await client.GetStringAsync($"/Permits/Review/{id}");
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success);
        return await client.PostAsync("/Permits/Confirm", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["id"] = id.ToString(), ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value)
            }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReviewAndBookmarkedCheckout_ShareOneProtectedPanel_WithoutCreatingPayment(bool configured)
    {
        using var parent = new AppIntegrationTests.DemoFactory();
        var provider = new LocalPayPal();
        using var factory = Factory(parent, new MutableCatalog(), provider, configured);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var id = await Draft(factory);

        foreach (var surface in new[] { "Permits/Review", "Checkout" })
        {
            var response = await client.GetAsync($"/{surface}/{id}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Single(Regex.Matches(html, "id=\"checkout-payment\"").Cast<Match>());
            Assert.Single(Regex.Matches(html, "src=\"/js/checkout.js").Cast<Match>());
            Assert.Contains("Choose how to pay", html);
            Assert.Contains("PayPal order ID", html);
            Assert.Contains("PayPal capture ID", html);
            Assert.DoesNotContain("Pay with PayPal", html);
            Assert.DoesNotContain("Who pays what", html);
            Assert.DoesNotContain(PrivateSentinel, html);
            Assert.DoesNotContain("Download permit (PDF)", html);
            Assert.Contains($"action=\"/Checkout/Start/{id}\"", html);
            if (surface == "Permits/Review")
                Assert.True(html.IndexOf("Choose how to pay", StringComparison.Ordinal) <
                    html.IndexOf("Check my answers", StringComparison.Ordinal));
            if (configured)
            {
                Assert.Contains(PublicClient, html);
                Assert.Contains("enable-funding=card", html);
                Assert.Single(Regex.Matches(html, "id=\"paypal-button-container\"").Cast<Match>());
                var noScript = Regex.Match(html, "<noscript>(.*?)</noscript>", RegexOptions.Singleline).Groups[1].Value;
                Assert.Contains("method=\"post\"", noScript);
                Assert.Contains($"action=\"/Checkout/Start/{id}\"", noScript);
                Assert.Contains("__RequestVerificationToken", noScript);
                Assert.Contains("Continue to PayPal", noScript);
            }
            else
            {
                Assert.Contains("PayPal checkout is unavailable right now.", html);
                Assert.DoesNotContain("id=\"paypal-button-container\"", html);
                Assert.DoesNotContain("https://www.paypal.com/sdk/js?", html);
            }
        }
        using var scope = factory.Services.CreateScope();
        var saved = await scope.ServiceProvider.GetRequiredService<SolviaDbContext>().PermitApplications.SingleAsync(a => a.Id == id);
        Assert.Null(saved.PayPalOrderId);
        Assert.Null(saved.PayPalCaptureId);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task BoundOrder_ReviewAndConfirmKeepItsOriginalQuote_WhenCatalogChanges()
    {
        using var parent = new AppIntegrationTests.DemoFactory();
        var catalog = new MutableCatalog();
        var provider = new LocalPayPal();
        using var factory = Factory(parent, catalog, provider);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var id = await Draft(factory, bound: true);
        catalog.Price = 3100;

        var html = await client.GetStringAsync($"/Permits/Review/{id}");
        Assert.Contains("$30.00", html);
        Assert.DoesNotContain("$31.00", html);
        Assert.Contains("Total reserved for your existing PayPal order", html);
        Assert.Contains("OFFLINE-EXISTING-ORDER", html);
        var response = await Confirm(client, id);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/Checkout/{id}", response.Headers.Location!.OriginalString);
        using var scope = factory.Services.CreateScope();
        var saved = await scope.ServiceProvider.GetRequiredService<SolviaDbContext>().PermitApplications.SingleAsync(a => a.Id == id);
        Assert.Equal(3000, saved.PriceCents);
        Assert.Equal("OFFLINE-EXISTING-ORDER", saved.PayPalOrderId);
        Assert.Equal(0, provider.Calls);
    }

    [Theory]
    [InlineData(3100)]
    [InlineData(0)]
    public async Task UnboundPriceChange_RequiresProtectedAcknowledgmentBeforeShowingPayment(int newPrice)
    {
        using var parent = new AppIntegrationTests.DemoFactory();
        var catalog = new MutableCatalog();
        var provider = new LocalPayPal();
        using var factory = Factory(parent, catalog, provider);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var id = await Draft(factory);
        catalog.Price = newPrice;

        var html = await client.GetStringAsync($"/Permits/Review/{id}");
        Assert.Contains("Review updated total", html);
        Assert.DoesNotContain("id=\"paypal-button-container\"", html);
        var rejected = await client.PostAsync("/Permits/Confirm", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["id"] = id.ToString() }));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var response = await Confirm(client, id);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(newPrice == 0 ? $"/Permits/Receipt/{id}" : $"/Checkout/{id}", response.Headers.Location!.OriginalString);
        using var scope = factory.Services.CreateScope();
        var saved = await scope.ServiceProvider.GetRequiredService<SolviaDbContext>().PermitApplications.SingleAsync(a => a.Id == id);
        Assert.Equal(newPrice, saved.PriceCents);
        Assert.Null(saved.PayPalOrderId);
        Assert.Equal(0, provider.Calls);
        if (newPrice == 0)
        {
            Assert.Equal(PaymentStatus.NotRequired, saved.PaymentStatus);
            Assert.NotNull(saved.ReceiptNumber);
        }
    }

    [Theory]
    [InlineData(100)]
    [InlineData(0)]
    [InlineData(null)]
    public async Task NewOrder_RefusesStalePositiveQuote_BeforeCallingProvider(int? changedPrice)
    {
        using var parent = new AppIntegrationTests.DemoFactory();
        var catalog = new MutableCatalog();
        var provider = new LocalPayPal();
        using var factory = Factory(parent, catalog, provider);
        var id = await Draft(factory);
        catalog.Price = changedPrice;
        using var scope = factory.Services.CreateScope();

        await Assert.ThrowsAsync<CheckoutPricingException>(() => scope.ServiceProvider.GetRequiredService<CheckoutService>()
            .StartAsync(id, null, null));

        var saved = await scope.ServiceProvider.GetRequiredService<SolviaDbContext>().PermitApplications.SingleAsync(a => a.Id == id);
        Assert.Equal(3000, saved.PriceCents);
        Assert.Null(saved.PayPalOrderId);
        Assert.Null(saved.ReceiptNumber);
        Assert.Equal(0, provider.Calls);
    }

    private sealed class MutableCatalog : IFeeCatalog
    {
        private readonly IFeeCatalog _inner = new FeeCatalogService(RedoTestDb.FindFeesCsv());
        public int? Price { get; set; } = 3000;
        public CampusFee? Find(string campusName)
        {
            var fee = _inner.Find(campusName);
            return fee?.Campus == "Fresno City College"
                ? fee with { FallSemesterCents = Price, SpringSemesterCents = Price, IsFree = Price == 0 }
                : fee;
        }
        public IReadOnlyList<CampusFee> All() => _inner.All().Select(f => Find(f.Campus)!).ToArray();
        public int? SemesterPriceCents(CampusFee fee, TermKind term) => _inner.SemesterPriceCents(fee, term);
    }

    private sealed class LocalPayPal : IPayPalService
    {
        public int Calls { get; private set; }
        private Exception Unexpected() { Calls++; return new InvalidOperationException("Provider calls are forbidden in this offline contract test."); }
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default) => throw Unexpected();
        public Task<PayPalOrderResult> CreateOrderAsync(int amountCents, string reference, string? returnUrl = null,
            string? cancelUrl = null, CancellationToken cancellationToken = default) => throw Unexpected();
        public Task<PayPalOrderResult> GetOrderAsync(string orderId, CancellationToken cancellationToken = default) => throw Unexpected();
        public Task<PayPalCaptureResult> CaptureOrderAsync(string orderId, CancellationToken cancellationToken = default) => throw Unexpected();
        public Task<bool> VerifyWebhookSignatureAsync(string transmissionId, string transmissionTime, string transmissionSig,
            string certUrl, string authAlgo, string webhookEventJson, CancellationToken cancellationToken = default) => throw Unexpected();
    }
}
