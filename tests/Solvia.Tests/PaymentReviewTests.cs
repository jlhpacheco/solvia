using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Solvia.Controllers;
using Solvia.Data;
using Solvia.Models;
using Solvia.Models.Enums;
using Solvia.Models.ViewModels;
using Solvia.Services;

namespace Solvia.Tests;

/// <summary>
/// Offline fixtures deliberately cover inconsistent local records. These are
/// not provider transactions and do not claim PayPal approval or settlement.
/// </summary>
public sealed class PaymentReviewTests
{
    private static readonly DateOnly Today = new(2026, 10, 20);
    private const string Fresno = "Fresno City College";

    [Fact]
    public async Task UnissuedPendingZero_IsUnpricedRatherThanFree_WhileConfirmedAmountsRemainMoney()
    {
        var (db, connection) = RedoTestDb.Create();
        using var _ = connection;
        using var __ = db;
        var unpriced = Add(db, PaymentStatus.Pending, 0);
        unpriced.ReceiptNumber = "";
        var exempt = Add(db, PaymentStatus.NotRequired, 0, category: UserCategory.Employee);
        var paid = Add(db, PaymentStatus.Paid, 3000, capture: "OFFLINE-PAID-CAPTURE");
        var recordedZero = Add(db, PaymentStatus.Paid, 0, capture: "OFFLINE-ZERO-CAPTURE");
        await db.SaveChangesAsync();

        var result = await new PaymentReviewService(db).ReadAsync(validityDate: Today);
        var draft = result.Rows.Single(row => row.Id == unpriced.Id);
        Assert.False(draft.HasConfirmedAmount);
        Assert.Equal("Not priced", draft.Amount);
        Assert.Equal(new[] { "Price not confirmed" }, draft.Issues);
        var free = result.Rows.Single(row => row.Id == exempt.Id);
        Assert.True(free.HasConfirmedAmount);
        Assert.Equal("$0.00", free.Amount);
        Assert.Empty(free.Issues);
        Assert.Equal("$30.00", result.Rows.Single(row => row.Id == paid.Id).Amount);
        Assert.Equal("$0.00", result.Rows.Single(row => row.Id == recordedZero.Id).Amount);
        Assert.Equal(3000, result.PaidWithCaptureCents);
        Assert.Equal(2, result.PaidWithCaptureCount);
        Assert.Equal(1, result.AttentionCount);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(draft,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        Assert.False(json.RootElement.GetProperty("hasConfirmedAmount").GetBoolean());
        Assert.Equal("Not priced", json.RootElement.GetProperty("amount").GetString());
        Assert.DoesNotContain(db.ChangeTracker.Entries(), entry => entry.State == Microsoft.EntityFrameworkCore.EntityState.Modified);
    }

    [Fact]
    public async Task SummaryAndQueue_RespectSavedCaptureStatusValidityAndOneApplicationGrain()
    {
        var (db, connection) = RedoTestDb.Create();
        using var _ = connection;
        using var __ = db;
        Add(db, PaymentStatus.Paid, 3000, capture: "OFFLINE-CAP-1");
        var expired = Add(db, PaymentStatus.Paid, 500, capture: "OFFLINE-CAP-2");
        expired.EndDate = Today.AddDays(-1);
        var missing = Add(db, PaymentStatus.Paid, 1000);
        missing.EndDate = Today.AddDays(-1); // Two flags, still one issue application.
        Add(db, PaymentStatus.Pending, 3000);
        Add(db, PaymentStatus.NotRequired, 0, category: UserCategory.Employee);
        Add(db, PaymentStatus.Refunded, 2000, capture: "OFFLINE-REFUND");
        Add(db, PaymentStatus.PartiallyRefunded, 600, capture: "OFFLINE-PARTIAL");
        Add(db, PaymentStatus.Failed, 200);
        var denied = Add(db, PaymentStatus.Pending, 100);
        denied.Status = ApplicationStatus.Denied;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var model = await new PaymentReviewService(db).ReadAsync(validityDate: Today);

        Assert.Equal(9, model.ApplicationCount);
        Assert.Equal(2, model.PaidWithCaptureCount);
        Assert.Equal(3500, model.PaidWithCaptureCents);
        Assert.Equal(1, model.NoPaymentRequiredCount);
        Assert.Equal(5, model.AttentionCount);
        Assert.Equal(5, model.AttentionRows.Count());
        Assert.Contains("Paid · permit inactive", model.Rows.Single(row => row.Id == expired.Id).Issues);
        Assert.Equal(2, model.Rows.Single(row => row.Id == missing.Id).Issues.Count);
        Assert.Empty(model.Rows.Single(row => row.Id == denied.Id).Issues);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal("$35.00", PaymentReviewViewModel.Money(model.PaidWithCaptureCents));
    }

    [Fact]
    public async Task FiltersIntersect_AndEverySummaryUsesExactlyTheReturnedRows()
    {
        var (db, connection) = RedoTestDb.Create();
        using var _ = connection;
        using var __ = db;
        Add(db, PaymentStatus.Paid, 3000, capture: "OFFLINE-1");
        Add(db, PaymentStatus.Pending, 3000);
        Add(db, PaymentStatus.Paid, 500, capture: "OFFLINE-2", category: UserCategory.Public);
        Add(db, PaymentStatus.Paid, 600, capture: "OFFLINE-3", campus: "CSU Bakersfield");
        await db.SaveChangesAsync();
        var service = new PaymentReviewService(db);

        var selected = await service.ReadAsync(Fresno, PaymentStatus.Paid, UserCategory.Student, validityDate: Today);
        Assert.Single(selected.Rows);
        Assert.Equal(3000, selected.PaidWithCaptureCents);
        Assert.Equal(0, selected.AttentionCount);
        Assert.Equal(2, selected.Campuses.Count);
        var none = await service.ReadAsync(Fresno, PaymentStatus.Paid, UserCategory.Student,
            attentionOnly: true, validityDate: Today);
        Assert.Empty(none.Rows);
        Assert.Equal(0, none.PaidWithCaptureCents);
        var pending = await service.ReadAsync(Fresno, PaymentStatus.Pending, UserCategory.Student,
            attentionOnly: true, validityDate: Today);
        Assert.Single(pending.Rows);
        Assert.Equal(1, pending.AttentionCount);
        Assert.Equal(0, pending.PaidWithCaptureCents);
    }

    [Fact]
    public async Task ReviewProjection_ExcludesNamesEmailsVehicleAndVerificationSecrets()
    {
        var (db, connection) = RedoTestDb.Create();
        using var _ = connection;
        using var __ = db;
        var application = Add(db, PaymentStatus.Paid, 3000, capture: "OFFLINE-CAPTURE");
        application.VerifyToken = "DO-NOT-EXPOSE-VERIFY-TOKEN";
        db.Students.Single(person => person.Id == "maria").DisplayName = "PrivateFullName";
        db.Students.Single(person => person.Id == "maria").Email = "private@real.invalid";
        await db.SaveChangesAsync();

        var model = await new PaymentReviewService(db).ReadAsync(validityDate: Today);
        var json = JsonSerializer.Serialize(model);
        Assert.Contains("Maria", json);
        Assert.DoesNotContain("PrivateFullName", json);
        Assert.DoesNotContain("private@real.invalid", json);
        Assert.DoesNotContain("DO-NOT-EXPOSE", json);
        Assert.DoesNotContain("PLATE-PRIVATE", json);
        Assert.DoesNotContain("studentId", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Development", false)]
    [InlineData("Testing", false)]
    [InlineData("Production", false)]
    [InlineData("Production", true)]
    [InlineData("Staging", true)]
    public async Task Gate_RequiresBothExplicitDemoFlagAndAllowedEnvironment(string environment, bool enabled)
    {
        var (db, connection) = RedoTestDb.Create();
        using var _ = connection;
        // Dispose first: the denied route must not even query the database.
        db.Dispose();
        var controller = Controller(db, environment, enabled);
        Assert.IsType<NotFoundResult>(await controller.Index(null, null, null));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public async Task Gate_AllowsOptedInDemoOnly_WithoutCachingTheRecords(string environment)
    {
        var (db, connection) = RedoTestDb.Create();
        using var _ = connection;
        using var __ = db;
        var controller = Controller(db, environment, true);
        var result = Assert.IsType<ViewResult>(await controller.Index(null, null, null));
        Assert.Empty(Assert.IsType<PaymentReviewViewModel>(result.Model).Rows);
        Assert.Equal("noindex, nofollow", controller.Response.Headers["X-Robots-Tag"]);
        var cache = Assert.Single(typeof(OperationsController).GetCustomAttributes(typeof(ResponseCacheAttribute), false).Cast<ResponseCacheAttribute>());
        Assert.True(cache.NoStore);
        Assert.Equal(ResponseCacheLocation.None, cache.Location);
    }

    [Theory]
    [InlineData("99", null)]
    [InlineData("Paid,Pending", null)]
    [InlineData("paid", null)]
    [InlineData(null, "-1")]
    public async Task Filters_RejectNonListedValuesInsteadOfBroadening(string? status, string? category)
    {
        var (db, connection) = RedoTestDb.Create();
        using var _ = connection;
        using var __ = db;
        Assert.IsType<BadRequestObjectResult>(await Controller(db, "Testing", true).Index(null, status, category));
    }

    [Fact]
    public async Task Filters_RejectUnknownCampusAndMalformedCheckboxInsteadOfShowingDifferentScope()
    {
        var (db, connection) = RedoTestDb.Create();
        using var _ = connection;
        using var __ = db;
        Assert.IsType<BadRequestObjectResult>(await Controller(db, "Testing", true).Index("Not in this record catalog", null, null));
        var controller = Controller(db, "Testing", true);
        controller.ModelState.AddModelError("attentionOnly", "Invalid Boolean input.");
        Assert.IsType<BadRequestObjectResult>(await controller.Index(null, null, null));
    }

    [Fact]
    public async Task Razor_UsesSafeJsonAndNativeRecords_WithSavedReferencesAndNoPaymentActions()
    {
        using var baseFactory = new AppIntegrationTests.DemoFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Demo:PaymentReviewEnabled"] = "true" })));
        using var client = factory.CreateClient();
        Guid id;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SolviaDbContext>();
            var application = Add(db, PaymentStatus.Paid, 3000, capture: "OFFLINE-CAPTURE",
                campus: "</script><img src=x onerror=alert(1)>");
            application.PayPalOrderId = "OFFLINE-ORDER";
            id = application.Id;
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync("/Operations");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("payment-review-native", html);
        Assert.Contains("OFFLINE-ORDER", html);
        Assert.Contains("OFFLINE-CAPTURE", html);
        Assert.Contains("PayPal was not contacted", html);
        Assert.DoesNotContain("</script><img src=x", html);
        var block = Regex.Match(html, "<script type=\"application/json\" id=\"payment-review-data\">(.*?)</script>", RegexOptions.Singleline);
        Assert.True(block.Success);
        using var json = JsonDocument.Parse(block.Groups[1].Value);
        Assert.Equal(id.ToString(), json.RootElement[0].GetProperty("id").GetString());
        Assert.Equal("</script><img src=x onerror=alert(1)>", json.RootElement[0].GetProperty("campus").GetString());
        Assert.DoesNotContain("<", block.Groups[1].Value);
        // All forms on this page are the read-only filters. The layout may
        // contain persona-switch POST forms, so inspect the actual dashboard.
        var dashboard = html[(html.IndexOf("<div class=\"pr-page\">", StringComparison.Ordinal))..];
        Assert.DoesNotContain("method=\"post\"", dashboard, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api/paypal", dashboard);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
    }

    private static OperationsController Controller(SolviaDbContext db, string environment, bool enabled) =>
        new(new PaymentReviewService(db), new DemoEnvironment { EnvironmentName = environment },
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Demo:PaymentReviewEnabled"] = enabled.ToString()
            }).Build())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    private static PermitApplication Add(SolviaDbContext db, PaymentStatus payment, int cents,
        string? capture = null, UserCategory category = UserCategory.Student, string campus = Fresno)
    {
        var owner = category == UserCategory.Public ? "pat" : category == UserCategory.Employee ? "elena" : "maria";
        var vehicle = new Vehicle
        {
            Id = Guid.NewGuid(), StudentId = owner, Plate = "PLATE-PRIVATE", State = UsState.CA,
            Make = "Demo", Model = "Fixture", Color = VehicleColor.White
        };
        db.Vehicles.Add(vehicle);
        var application = new PermitApplication
        {
            Id = Guid.NewGuid(), StudentId = owner, VehicleId = vehicle.Id, PermitTypeId = 2,
            Campus = campus, Category = category, PaymentStatus = payment, PriceCents = cents,
            PayPalCaptureId = capture, ReceiptNumber = "OFFLINE-" + Guid.NewGuid().ToString("N")[..12],
            StartDate = Today.AddDays(-1), EndDate = Today.AddDays(10),
            SubmittedAtUtc = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc), ConcurrencyToken = Guid.NewGuid()
        };
        db.PermitApplications.Add(application);
        return application;
    }

    private sealed class DemoEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "Solvia";
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
