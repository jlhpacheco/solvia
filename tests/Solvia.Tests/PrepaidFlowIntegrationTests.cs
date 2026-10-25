using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Solvia.Data;
using Solvia.Models;
using Solvia.Models.Enums;
using Solvia.Services;

namespace Solvia.Tests;

public sealed class PrepaidFlowIntegrationTests : IDisposable
{
    private readonly AppIntegrationTests.DemoFactory _factory = new();
    private readonly HttpClient _client;
    private static readonly DateOnly[] Dates = [new(2027, 1, 25), new(2027, 1, 27), new(2027, 1, 29)];
    public PrepaidFlowIntegrationTests() => _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    public void Dispose() { _client.Dispose(); _factory.Dispose(); }

    private async Task<(string Plan, Guid Vehicle)> Plan(string owner = "maria")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SolviaDbContext>();
        var vehicle = new Vehicle { Id = Guid.NewGuid(), StudentId = owner, Plate = "PP" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), State = UsState.CA, Make = "Honda", Model = "Civic", Color = VehicleColor.White };
        db.Vehicles.Add(vehicle); await db.SaveChangesAsync();
        return (scope.ServiceProvider.GetRequiredService<PrepaidPlanProtection>().Protect(owner, "Fresno City College", TermKind.Spring, Dates), vehicle.Id);
    }
    private async Task<HttpResponseMessage> Create(string plan, Guid vehicle, params string[] dates)
    {
        var html = await _client.GetStringAsync("/");
        var token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        var fields = new List<KeyValuePair<string, string>> { new("plan", plan), new("vehicleId", vehicle.ToString()), new("__RequestVerificationToken", token) };
        fields.AddRange(dates.Select(date => new KeyValuePair<string, string>("selectedDates", date)));
        return await _client.PostAsync("/Prepaid/Create", new FormUrlEncodedContent(fields));
    }

    [Fact]
    public async Task SelectionPersistsExactSubset_ReviewIsReadOnly_ChildRoutesReturnToGroup()
    {
        var (plan, vehicle) = await Plan();
        var created = await Create(plan, vehicle, "2027-01-25", "2027-01-29");
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var url = created.Headers.Location!.OriginalString;
        var html = await _client.GetStringAsync(url);
        Assert.Contains("Review your prepaid passes", html);
        Assert.Contains("$2.00 USD", html);
        Assert.DoesNotContain("datetime=\"2027-01-27\"", html);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SolviaDbContext>();
        var p = await db.PrepaidPurchases.Include(p => p.Items).SingleAsync();
        Assert.Null(p.PayPalOrderId); Assert.Null(p.PayPalCaptureId); Assert.Equal(2, p.Items.Count);
        foreach (var item in p.Items)
        {
            var child = await db.PermitApplications.Include(a => a.Vehicle).Include(a => a.PermitType).SingleAsync(a => a.Id == item.ApplicationId);
            var checkout = scope.ServiceProvider.GetRequiredService<CheckoutService>();
            var startError = await Assert.ThrowsAsync<CheckoutPricingException>(() => checkout.StartAsync(child.Id, null, null,
                default, CheckoutReviewSnapshot.Compute(child)));
            Assert.Contains("prepaid purchase", startError.Message);
            var captureError = await Assert.ThrowsAsync<CheckoutPricingException>(() => checkout.CaptureAsync(child.Id));
            Assert.Contains("prepaid purchase", captureError.Message);
            foreach (var path in new[] { $"/Checkout/{item.ApplicationId}", $"/Permits/Review/{item.ApplicationId}" })
            {
                var response = await _client.GetAsync(path);
                Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
                Assert.Equal(url, response.Headers.Location!.OriginalString);
            }
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync($"/Prepaid/Pdf/{p.Id}")).StatusCode);
        Assert.Equal(url, (await _client.GetAsync($"/Prepaid/Receipt/{p.Id}")).Headers.Location!.OriginalString);
        var repeated = await Create(plan, vehicle, "2027-01-25", "2027-01-29");
        Assert.Equal(url, repeated.Headers.Location!.OriginalString);
        Assert.Equal(1, await db.PrepaidPurchases.CountAsync());
    }

    [Theory]
    [InlineData("2027-01-26")]
    [InlineData("not-a-date")]
    [InlineData("2027-01-25", "2027-01-25")]
    [InlineData()]
    public async Task InvalidSelectionCreatesNoPurchase(params string[] dates)
    {
        var (plan, vehicle) = await Plan();
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(plan, vehicle, dates)).StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SolviaDbContext>();
        Assert.Equal(0, await db.PrepaidPurchases.CountAsync()); Assert.Equal(0, await db.PermitApplications.CountAsync());
    }

    [Fact]
    public async Task WrongOwnerAndTamperedPlansCannotCreateApplications()
    {
        var (plan, vehicle) = await Plan("pat");
        Assert.Equal(HttpStatusCode.Redirect, (await Create(plan, vehicle, "2027-01-25")).StatusCode);
        var (ownPlan, ownVehicle) = await Plan();
        var tampered = ownPlan[..^8] + "AAAAAAAA";
        Assert.Equal(HttpStatusCode.Redirect, (await Create(tampered, ownVehicle, "2027-01-25")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(ownPlan, vehicle, "2027-01-25")).StatusCode);
        using var scope = _factory.Services.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<SolviaDbContext>().PermitApplications.CountAsync());
    }

    [Theory]
    [InlineData("/Prepaid/Create")]
    [InlineData("/Prepaid/Start/00000000-0000-0000-0000-000000000001")]
    [InlineData("/Prepaid/Refresh/00000000-0000-0000-0000-000000000001")]
    [InlineData("/Prepaid/Revise/00000000-0000-0000-0000-000000000001")]
    public async Task FormsRequireAntiforgery(string path) => Assert.Equal(HttpStatusCode.BadRequest,
        (await _client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>()))).StatusCode);

    [Theory]
    [InlineData("/api/prepaid/orders")]
    [InlineData("/api/prepaid/capture")]
    public async Task FinancialApisRequireAntiforgery(string path) => Assert.Equal(HttpStatusCode.BadRequest,
        (await _client.PostAsJsonAsync(path, new { purchaseId = Guid.NewGuid(), orderId = "fake" })).StatusCode);

    [Fact]
    public async Task RevisingUnpaidPurchaseKeepsVehicleAndDatesWithoutLeavingConflictingDrafts()
    {
        var (plan, vehicle) = await Plan();
        var created = await Create(plan, vehicle, "2027-01-25", "2027-01-29");
        var review = created.Headers.Location!.OriginalString;
        var id = Guid.Parse(review.Split('/')[^1]);
        var html = await _client.GetStringAsync(review);
        var token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        var revised = await _client.PostAsync($"/Prepaid/Revise/{id}", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, revised.StatusCode);
        var choose = await _client.GetStringAsync(revised.Headers.Location!);
        Assert.Contains($"value=\"{vehicle}\" selected=\"selected\"", choose);
        Assert.Contains("2027-01-25", choose); Assert.Contains("2027-01-29", choose);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SolviaDbContext>();
        Assert.Empty(await db.PrepaidPurchases.ToListAsync()); Assert.Empty(await db.PermitApplications.ToListAsync());
        Assert.True(await db.Vehicles.AnyAsync(v => v.Id == vehicle));
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(review)).StatusCode);
    }

    [Fact]
    public void ProtectedPlanExpiresAndRejectsWrongOwner()
    {
        using var scope = _factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>();
        var clock = new TestClock(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero));
        var protection = new PrepaidPlanProtection(provider, clock);
        var token = protection.Protect("maria", "Fresno City College", TermKind.Spring, Dates);
        Assert.NotNull(protection.Read(token, "maria")); Assert.Null(protection.Read(token, "pat"));
        clock.Now = clock.Now.AddHours(3); Assert.Null(protection.Read(token, "maria"));
    }
    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    { public DateTimeOffset Now { get; set; } = now; public override DateTimeOffset GetUtcNow() => Now; }
}
