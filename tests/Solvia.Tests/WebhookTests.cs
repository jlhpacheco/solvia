using System.Net;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Solvia.Config;
using Solvia.Data;
using Solvia.Models;
using Solvia.Models.Enums;
using Solvia.Services;

namespace Solvia.Tests;

/// <summary>
/// T7 payment-complete webhook tests — fully OFFLINE. A fake
/// <see cref="HttpMessageHandler"/> stands in for the Zapier Catch Hook
/// endpoint and a stub <see cref="IPayPalService"/> plays the sandbox,
/// so no real call leaves the test run.
///
/// What is proven here (docs/zapier-recipe.md describes the live Zap):
/// - a COMPLETED capture that marks the permit Paid fires exactly one
///   event carrying the permit facts — and no student PII;
/// - a $0 FREE permit fires the event too (amount 0, no capture id);
/// - a non-COMPLETED capture fires NOTHING;
/// - an empty Webhook:Url disables the webhook (no call at all) while
///   the payment still completes;
/// - a webhook endpoint that throws or answers 500 can NEVER block or
///   roll back the payment — the Paid state stands.
/// </summary>
public sealed class WebhookTests : IDisposable
{
    private const string HookUrl = "https://hooks.example.test/catch/1";

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<SolviaDbContext> _options;

    public WebhookTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<SolviaDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var db = new SolviaDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    // ------------------------------------------------------------------
    // The event fires — on Paid, and on free-issued
    // ------------------------------------------------------------------

    [Fact]
    public async Task PaidCapture_FiresWebhookOnce_WithPermitFactsAndNoPii()
    {
        var hook = new FakeHookHandler();
        var webhook = NewWebhook(hook, HookUrl);
        var payPal = new StubPayPalService { CaptureStatus = "COMPLETED" };

        Guid applicationId;
        using (var db = NewDb())
        {
            applicationId = SeedApplication(db, priceCents: 3000, orderId: "ORDER-1");
        }

        string? receiptNumber;
        using (var db = NewDb())
        {
            var checkout = NewCheckoutService(db, payPal, webhook);
            var result = await checkout.CaptureAsync(applicationId);
            Assert.True(result.Completed);
            receiptNumber = result.ReceiptNumber;
        }

        var body = Assert.Single(hook.Bodies);
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal(receiptNumber, root.GetProperty("permitNumber").GetString());
        Assert.Equal("Fresno City College", root.GetProperty("campus").GetString());
        Assert.Equal(3000, root.GetProperty("amountCents").GetInt32());
        Assert.Equal("CAPTURE-1", root.GetProperty("paypalCaptureId").GetString());
        Assert.Equal(JsonValueKind.String, root.GetProperty("paidAtUtc").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("paidAtUtc").GetString()));

        // Permit facts only — no student name, email, or plate anywhere.
        Assert.DoesNotContain("maria", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("8XYZ123", body);
        Assert.DoesNotContain("@", body);
    }

    [Fact]
    public async Task FreePermit_FiresWebhook_WithZeroAmountAndNoCaptureId()
    {
        var hook = new FakeHookHandler();
        var webhook = NewWebhook(hook, HookUrl);
        var payPal = new StubPayPalService();

        Guid applicationId;
        using (var db = NewDb())
        {
            applicationId = SeedApplication(db, priceCents: 0, orderId: null);
        }

        using (var db = NewDb())
        {
            var checkout = NewCheckoutService(db, payPal, webhook);
            var result = await checkout.StartAsync(
                applicationId, "https://localhost/return", "https://localhost/cancel");
            Assert.Equal(CheckoutStartKind.FreePermitConfirmed, result.Kind);
        }

        var body = Assert.Single(hook.Bodies);
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.False(string.IsNullOrWhiteSpace(
            root.GetProperty("permitNumber").GetString()));
        Assert.Equal(0, root.GetProperty("amountCents").GetInt32());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("paypalCaptureId").ValueKind);
    }

    // ------------------------------------------------------------------
    // The event does NOT fire — failed capture, or webhook disabled
    // ------------------------------------------------------------------

    [Fact]
    public async Task FailedCapture_FiresNoWebhook()
    {
        var hook = new FakeHookHandler();
        var webhook = NewWebhook(hook, HookUrl);
        var payPal = new StubPayPalService { CaptureStatus = "PENDING" };

        Guid applicationId;
        using (var db = NewDb())
        {
            applicationId = SeedApplication(db, priceCents: 3000, orderId: "ORDER-1");
        }

        using (var db = NewDb())
        {
            var checkout = NewCheckoutService(db, payPal, webhook);
            var result = await checkout.CaptureAsync(applicationId);
            Assert.False(result.Completed);
        }

        Assert.Empty(hook.Bodies);
    }

    [Fact]
    public async Task EmptyWebhookUrl_DisablesEvent_AndPaymentStillCompletes()
    {
        var hook = new FakeHookHandler();
        var webhook = NewWebhook(hook, url: "");
        Assert.False(webhook.IsEnabled);
        var payPal = new StubPayPalService { CaptureStatus = "COMPLETED" };

        Guid applicationId;
        using (var db = NewDb())
        {
            applicationId = SeedApplication(db, priceCents: 3000, orderId: "ORDER-1");
        }

        using (var db = NewDb())
        {
            var checkout = NewCheckoutService(db, payPal, webhook);
            var result = await checkout.CaptureAsync(applicationId);
            Assert.True(result.Completed);
        }

        Assert.Empty(hook.Bodies); // no URL configured → no call attempted

        using (var db = NewDb())
        {
            var saved = await db.PermitApplications.SingleAsync(a => a.Id == applicationId);
            Assert.Equal(PaymentStatus.Paid, saved.PaymentStatus);
        }
    }

    // ------------------------------------------------------------------
    // The webhook can never hurt the payment
    // ------------------------------------------------------------------

    [Fact]
    public async Task WebhookOutage_NeverBlocksOrRollsBackThePayment()
    {
        var hook = new FakeHookHandler { Throws = true };
        var webhook = NewWebhook(hook, HookUrl);
        var payPal = new StubPayPalService { CaptureStatus = "COMPLETED" };

        Guid applicationId;
        using (var db = NewDb())
        {
            applicationId = SeedApplication(db, priceCents: 3000, orderId: "ORDER-1");
        }

        using (var db = NewDb())
        {
            var checkout = NewCheckoutService(db, payPal, webhook);
            var result = await checkout.CaptureAsync(applicationId);
            Assert.True(result.Completed); // the send blew up; capture still answered
        }

        using (var db = NewDb())
        {
            var saved = await db.PermitApplications.SingleAsync(a => a.Id == applicationId);
            Assert.Equal(PaymentStatus.Paid, saved.PaymentStatus);
            Assert.NotNull(saved.PaidAtUtc);
        }
    }

    [Fact]
    public async Task WebhookServerError_NeverBlocksOrRollsBackThePayment()
    {
        var hook = new FakeHookHandler { Status = HttpStatusCode.InternalServerError };
        var webhook = NewWebhook(hook, HookUrl);
        var payPal = new StubPayPalService { CaptureStatus = "COMPLETED" };

        Guid applicationId;
        using (var db = NewDb())
        {
            applicationId = SeedApplication(db, priceCents: 3000, orderId: "ORDER-1");
        }

        using (var db = NewDb())
        {
            var checkout = NewCheckoutService(db, payPal, webhook);
            var result = await checkout.CaptureAsync(applicationId);
            Assert.True(result.Completed);
        }

        Assert.Single(hook.Bodies); // the send WAS attempted…

        using (var db = NewDb())
        {
            var saved = await db.PermitApplications.SingleAsync(a => a.Id == applicationId);
            Assert.Equal(PaymentStatus.Paid, saved.PaymentStatus); // …and Paid stands
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private SolviaDbContext NewDb() => new(_options);

    private static PaymentWebhookService NewWebhook(FakeHookHandler handler, string url) =>
        new(
            new HttpClient(handler),
            Options.Create(new WebhookOptions { Url = url }),
            NullLogger<PaymentWebhookService>.Instance);

    private static CheckoutService NewCheckoutService(
        SolviaDbContext db, IPayPalService payPal, PaymentWebhookService webhook) =>
        new(db, payPal, new PermitNumberService(db),
            NullLogger<CheckoutService>.Instance, webhook);

    /// <summary>
    /// Seeds Maria's vehicle + one Pending application at Fresno City
    /// College, optionally already carrying a PayPal order id.
    /// </summary>
    private static Guid SeedApplication(SolviaDbContext db, int priceCents, string? orderId)
    {
        var vehicle = new Vehicle
        {
            Id = Guid.NewGuid(),
            StudentId = "maria", // seeded fictional student
            Plate = "8XYZ123",
            State = UsState.CA,
            Color = VehicleColor.Blue,
            Make = "Honda",
            Model = "Civic",
        };
        db.Vehicles.Add(vehicle);

        var application = new PermitApplication
        {
            Id = Guid.NewGuid(),
            StudentId = "maria",
            Campus = "Fresno City College",
            VehicleId = vehicle.Id,
            PermitTypeId = 2, // seeded Term type
            StartDate = new DateOnly(2026, 8, 24),
            EndDate = new DateOnly(2026, 12, 11),
            Status = ApplicationStatus.Pending,
            PriceCents = priceCents,
            PaymentStatus = PaymentStatus.Pending,
            PayPalOrderId = orderId,
            SubmittedAtUtc = DateTime.UtcNow,
        };
        db.PermitApplications.Add(application);
        db.SaveChanges();
        return application.Id;
    }

    /// <summary>An offline PayPal: capture answers the configured status.</summary>
    private sealed class StubPayPalService : IPayPalService
    {
        public string CaptureStatus { get; set; } = "COMPLETED";

        public Task<string> GetAccessTokenAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult("TEST-ACCESS-TOKEN");

        public Task<PayPalOrderResult> CreateOrderAsync(
            int amountCents,
            string reference,
            string? returnUrl = null,
            string? cancelUrl = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PayPalOrderResult(
                "ORDER-1", "CREATED",
                "https://www.sandbox.paypal.com/checkoutnow?token=ORDER-1"));

        public Task<PayPalCaptureResult> CaptureOrderAsync(
            string orderId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PayPalCaptureResult(
                CaptureStatus,
                CaptureStatus == "COMPLETED" ? "CAPTURE-1" : null));
    }

    /// <summary>
    /// An offline stand-in for the Zapier Catch Hook endpoint: records
    /// every request body, and can be told to answer an error status or
    /// to throw outright (a dead endpoint).
    /// </summary>
    private sealed class FakeHookHandler : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public bool Throws { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (Throws)
            {
                throw new HttpRequestException("Simulated webhook endpoint outage.");
            }

            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(Status);
        }
    }
}
