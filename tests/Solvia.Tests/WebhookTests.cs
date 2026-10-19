using System.Net;
using Microsoft.AspNetCore.Http;
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
/// R7 inbound-webhook tests (REDO-PLAN item 33 + the item-35
/// outbound payload): signature truth is a stub
/// <see cref="IPayPalService"/> (PayPal's verify call is R6's one
/// talker; here the ANSWER is what matters), the outbound Zapier
/// POST is captured by a stub handler, and every assertion reads
/// the database back through a fresh context — saved truth, not
/// tracked-entity hope.
/// </summary>
public sealed class WebhookTests
{
    // ------------------------------------------------------- harness

    /// <summary>The PayPal seam, answered by the test. Only signature verification is exercised.</summary>
    private sealed class StubPayPal : IPayPalService
    {
        public bool VerifyResult = true;
        public bool ThrowNotConfigured;
        public int VerifyCalls;

        public Task<string> GetAccessTokenAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult("test-token");

        public Task<PayPalOrderResult> CreateOrderAsync(
            int amountCents, string reference, string? returnUrl = null,
            string? cancelUrl = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The webhook path never creates orders.");

        public Task<PayPalCaptureResult> CaptureOrderAsync(
            string orderId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The webhook path never captures directly.");

        public Task<bool> VerifyWebhookSignatureAsync(
            string transmissionId, string transmissionTime, string transmissionSig,
            string certUrl, string authAlgo, string webhookEventJson,
            CancellationToken cancellationToken = default)
        {
            VerifyCalls++;
            if (ThrowNotConfigured)
            {
                throw new PayPalNotConfiguredException(
                    "PayPal sandbox credentials are not configured — test stub.");
            }

            return Task.FromResult(VerifyResult);
        }
    }

    /// <summary>Captures every outbound webhook POST instead of sending it.</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed record World(
        SolviaDbContext Db,
        Microsoft.Data.Sqlite.SqliteConnection Connection,
        StubPayPal PayPal,
        CapturingHandler Outbound,
        PayPalWebhookHandler Handler,
        PaymentWebhookService PaymentWebhook);

    private static World NewWorld(string? publicBaseUrl = null)
    {
        var (db, connection) = RedoTestDb.Create();
        var payPal = new StubPayPal();
        var issuance = new IssuanceService(new PermitNumberService(db));
        var outbound = new CapturingHandler();
        var paymentWebhook = new PaymentWebhookService(
            new HttpClient(outbound),
            Options.Create(new WebhookOptions
            {
                Url = "https://hooks.example.test/catch",
                PublicBaseUrl = publicBaseUrl ?? "",
            }),
            NullLogger<PaymentWebhookService>.Instance);
        var handler = new PayPalWebhookHandler(
            db, payPal, issuance, paymentWebhook,
            NullLogger<PayPalWebhookHandler>.Instance);
        return new World(db, connection, payPal, outbound, handler, paymentWebhook);
    }

    private static Vehicle AddVehicle(
        SolviaDbContext db, string studentId, string plate = "8XYZ123")
    {
        var vehicle = new Vehicle
        {
            Id = Guid.NewGuid(),
            StudentId = studentId,
            Plate = plate,
            State = UsState.CA,
            Color = VehicleColor.Blue,
            Make = "Honda",
            Model = "Civic",
        };
        db.Vehicles.Add(vehicle);
        db.SaveChanges();
        return vehicle;
    }

    private static PermitApplication AddApplication(
        SolviaDbContext db,
        string studentId = "maria",
        UserCategory category = UserCategory.Student,
        int priceCents = 3000,
        PaymentStatus payment = PaymentStatus.Pending,
        string? captureId = null,
        string? orderId = null)
    {
        var type = db.PermitTypes.Single(t => t.Code == PermitTypeCode.Term);
        var application = new PermitApplication
        {
            Id = Guid.NewGuid(),
            StudentId = studentId,
            Campus = "Fresno City College",
            Category = category,
            VehicleId = AddVehicle(db, studentId).Id,
            PermitTypeId = type.Id,
            Term = TermKind.Fall,
            Source = "form",
            StartDate = new DateOnly(2026, 8, 24),
            EndDate = new DateOnly(2026, 12, 22),
            Status = ApplicationStatus.Pending,
            PriceCents = priceCents,
            PaymentStatus = payment,
            PayPalOrderId = orderId,
            PayPalCaptureId = captureId,
            SubmittedAtUtc = DateTime.UtcNow,
            ConcurrencyToken = Guid.NewGuid(),
        };
        db.PermitApplications.Add(application);
        db.SaveChanges();
        return application;
    }

    private static async Task<PermitApplication> Reload(World w, Guid id) =>
        await RedoTestDb.NewContext(w.Connection).PermitApplications
            .SingleAsync(a => a.Id == id);

    private static async Task<List<WebhookEventLog>> EventLogs(World w) =>
        await RedoTestDb.NewContext(w.Connection).WebhookEventLogs
            .OrderBy(l => l.ReceivedAtUtc).ToListAsync();

    private static HeaderDictionary SignedHeaders() => new()
    {
        ["paypal-transmission-id"] = "transmission-1",
        ["paypal-transmission-time"] = "2026-10-19T00:00:00Z",
        ["paypal-transmission-sig"] = "test-signature",
        ["paypal-cert-url"] = "https://api.sandbox.paypal.com/certs/test.pem",
        ["paypal-auth-algo"] = "SHA256withRSA",
    };

    private static string CaptureCompletedEvent(
        string eventId, string captureId, string amountValue, string? orderId = null) =>
        "{ \"id\": \"" + eventId + "\", " +
        "\"event_type\": \"PAYMENT.CAPTURE.COMPLETED\", " +
        "\"resource_type\": \"capture\", " +
        "\"resource\": { \"id\": \"" + captureId + "\", \"status\": \"COMPLETED\", " +
        "\"amount\": { \"value\": \"" + amountValue + "\", \"currency_code\": \"USD\" }" +
        (orderId is null
            ? ""
            : ", \"supplementary_data\": { \"related_ids\": { \"order_id\": \"" +
              orderId + "\" } }") +
        " } }";

    // ------------------------------------------------------- rejects

    [Fact]
    public async Task BadSignature_Returns401_NothingSaved()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var app = AddApplication(w.Db, captureId: "CAP-1");
        w.PayPal.VerifyResult = false;

        var outcome = await w.Handler.HandleAsync(
            CaptureCompletedEvent("EVT-1", "CAP-1", "30.00"), SignedHeaders());

        Assert.Equal(401, outcome.StatusCode);
        Assert.Equal(PayPalWebhookHandler.OutcomeRejected, outcome.Outcome);
        Assert.Empty(await EventLogs(w));
        var saved = await Reload(w, app.Id);
        Assert.Equal(PaymentStatus.Pending, saved.PaymentStatus);
        Assert.Null(saved.ReceiptNumber);
    }

    [Fact]
    public async Task NotConfigured_Returns401_NothingSaved()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var app = AddApplication(w.Db, captureId: "CAP-1");
        w.PayPal.ThrowNotConfigured = true;

        var outcome = await w.Handler.HandleAsync(
            CaptureCompletedEvent("EVT-1", "CAP-1", "30.00"), SignedHeaders());

        Assert.Equal(401, outcome.StatusCode);
        Assert.Empty(await EventLogs(w));
        Assert.Equal(PaymentStatus.Pending, (await Reload(w, app.Id)).PaymentStatus);
    }

    [Fact]
    public async Task EmptyBody_Returns401()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;

        var outcome = await w.Handler.HandleAsync("  ", SignedHeaders());

        Assert.Equal(401, outcome.StatusCode);
        Assert.Equal(0, w.PayPal.VerifyCalls);
        Assert.Empty(await EventLogs(w));
    }

    [Fact]
    public async Task MissingSignatureHeaders_Returns401()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        AddApplication(w.Db, captureId: "CAP-1");

        var outcome = await w.Handler.HandleAsync(
            CaptureCompletedEvent("EVT-1", "CAP-1", "30.00"), new HeaderDictionary());

        Assert.Equal(401, outcome.StatusCode);
        Assert.Empty(await EventLogs(w));
    }

    [Fact]
    public async Task MissingEventId_Returns400()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;

        var outcome = await w.Handler.HandleAsync(
            "{ \"event_type\": \"PAYMENT.CAPTURE.COMPLETED\", \"resource\": { } }",
            SignedHeaders());

        Assert.Equal(400, outcome.StatusCode);
        Assert.Empty(await EventLogs(w));
    }

    // --------------------------------------------------------- acting

    [Fact]
    public async Task CorrectEvent_OnUnpaidApplication_PaysAndIssues()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var app = AddApplication(w.Db, captureId: "CAP-1");

        var outcome = await w.Handler.HandleAsync(
            CaptureCompletedEvent("EVT-1", "CAP-1", "30.00"),
            SignedHeaders(), "https://solvia.example.test");

        Assert.Equal(200, outcome.StatusCode);
        Assert.Equal(PayPalWebhookHandler.OutcomeActed, outcome.Outcome);
        var saved = await Reload(w, app.Id);
        Assert.Equal(PaymentStatus.Paid, saved.PaymentStatus);
        Assert.NotNull(saved.PaidAtUtc);
        Assert.NotNull(saved.ReceiptNumber);
        Assert.NotNull(saved.VerifyToken);
        Assert.NotNull(saved.ReceiptIssuedAtUtc);
        var logs = await EventLogs(w);
        var log = Assert.Single(logs);
        Assert.Equal(PayPalWebhookHandler.OutcomeActed, log.Outcome);
        Assert.NotNull(log.ProcessedAtUtc);
        Assert.Single(w.Outbound.Bodies);
    }

    [Fact]
    public async Task SameEventTwice_SecondDeliveryIsLoggedNoOp()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var app = AddApplication(w.Db, captureId: "CAP-1");
        var body = CaptureCompletedEvent("EVT-1", "CAP-1", "30.00");

        var first = await w.Handler.HandleAsync(
            body, SignedHeaders(), "https://solvia.example.test");
        var receiptAfterFirst = (await Reload(w, app.Id)).ReceiptNumber;
        var second = await w.Handler.HandleAsync(
            body, SignedHeaders(), "https://solvia.example.test");

        Assert.Equal(PayPalWebhookHandler.OutcomeActed, first.Outcome);
        Assert.Equal(200, second.StatusCode);
        Assert.Equal(PayPalWebhookHandler.OutcomeDuplicate, second.Outcome);
        // One log row (the acted one), one issuance, one outbound send.
        var log = Assert.Single(await EventLogs(w));
        Assert.Equal(PayPalWebhookHandler.OutcomeActed, log.Outcome);
        Assert.Equal(receiptAfterFirst, (await Reload(w, app.Id)).ReceiptNumber);
        Assert.Single(w.Outbound.Bodies);
    }

    [Fact]
    public async Task AmountMismatch_MarksFailed_NoIssuance()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var app = AddApplication(w.Db, captureId: "CAP-1", priceCents: 3000);

        var outcome = await w.Handler.HandleAsync(
            CaptureCompletedEvent("EVT-1", "CAP-1", "25.00"), SignedHeaders());

        Assert.Equal(PayPalWebhookHandler.OutcomeFailed, outcome.Outcome);
        var saved = await Reload(w, app.Id);
        Assert.Equal(PaymentStatus.Failed, saved.PaymentStatus);
        Assert.Null(saved.ReceiptNumber);
        Assert.Null(saved.VerifyToken);
        Assert.Null(saved.PaidAtUtc);
        Assert.Empty(w.Outbound.Bodies);
    }

    [Fact]
    public async Task EmployeeApplication_EventMarksFailed_NoIssuance()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var app = AddApplication(
            w.Db, studentId: "elena", category: UserCategory.Employee,
            captureId: "CAP-1");

        var outcome = await w.Handler.HandleAsync(
            CaptureCompletedEvent("EVT-1", "CAP-1", "30.00"), SignedHeaders());

        Assert.Equal(PayPalWebhookHandler.OutcomeFailed, outcome.Outcome);
        var saved = await Reload(w, app.Id);
        Assert.Equal(PaymentStatus.Failed, saved.PaymentStatus);
        Assert.Null(saved.ReceiptNumber);
    }

    [Fact]
    public async Task OtherEventType_LoggedAndIgnored()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var app = AddApplication(w.Db, captureId: "CAP-1");

        var outcome = await w.Handler.HandleAsync(
            "{ \"id\": \"EVT-9\", \"event_type\": \"PAYMENT.CAPTURE.DENIED\", " +
            "\"resource\": { \"id\": \"CAP-1\" } }",
            SignedHeaders());

        Assert.Equal(200, outcome.StatusCode);
        Assert.Equal(PayPalWebhookHandler.OutcomeIgnored, outcome.Outcome);
        Assert.Equal(PaymentStatus.Pending, (await Reload(w, app.Id)).PaymentStatus);
        var log = Assert.Single(await EventLogs(w));
        Assert.Equal(PayPalWebhookHandler.OutcomeIgnored, log.Outcome);
        Assert.Empty(w.Outbound.Bodies);
    }

    [Fact]
    public async Task UnknownCapture_LoggedUnmatched_NothingChanges()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var app = AddApplication(w.Db, captureId: "CAP-1");

        var outcome = await w.Handler.HandleAsync(
            CaptureCompletedEvent("EVT-1", "CAP-OTHER", "30.00"), SignedHeaders());

        Assert.Equal(200, outcome.StatusCode);
        Assert.Equal(PayPalWebhookHandler.OutcomeUnmatched, outcome.Outcome);
        Assert.Equal(PaymentStatus.Pending, (await Reload(w, app.Id)).PaymentStatus);
        Assert.Single(await EventLogs(w));
    }

    [Fact]
    public async Task OrderIdFallback_FindsApplicationBeforeCaptureIdRecorded()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var app = AddApplication(w.Db, orderId: "ORDER-9");

        var outcome = await w.Handler.HandleAsync(
            CaptureCompletedEvent("EVT-1", "CAP-9", "30.00", orderId: "ORDER-9"),
            SignedHeaders());

        Assert.Equal(PayPalWebhookHandler.OutcomeActed, outcome.Outcome);
        var saved = await Reload(w, app.Id);
        Assert.Equal(PaymentStatus.Paid, saved.PaymentStatus);
        Assert.Equal("CAP-9", saved.PayPalCaptureId);
        Assert.NotNull(saved.ReceiptNumber);
    }

    [Fact]
    public async Task AlreadyPaidApplication_WebhookIssuesIfAbsent()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var app = AddApplication(w.Db, payment: PaymentStatus.Paid, captureId: "CAP-1");
        app.PaidAtUtc = DateTime.UtcNow;
        w.Db.SaveChanges();

        var outcome = await w.Handler.HandleAsync(
            CaptureCompletedEvent("EVT-1", "CAP-1", "30.00"),
            SignedHeaders(), "https://solvia.example.test");

        Assert.Equal(PayPalWebhookHandler.OutcomeActed, outcome.Outcome);
        var saved = await Reload(w, app.Id);
        Assert.Equal(PaymentStatus.Paid, saved.PaymentStatus);
        Assert.NotNull(saved.ReceiptNumber);
        Assert.NotNull(saved.VerifyToken);
        Assert.Single(w.Outbound.Bodies);
    }

    // ------------------------------------------------------- outbound

    [Fact]
    public async Task OutboundPayload_HasPermitFacts_AndNoStudentPii()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var app = AddApplication(w.Db, captureId: "CAP-1");

        await w.Handler.HandleAsync(
            CaptureCompletedEvent("EVT-1", "CAP-1", "30.00"),
            SignedHeaders(), "https://solvia.example.test");

        var saved = await Reload(w, app.Id);
        var payload = Assert.Single(w.Outbound.Bodies);
        Assert.Contains("\"permitNumber\":\"" + saved.ReceiptNumber + "\"", payload);
        Assert.Contains(
            "\"verifyUrl\":\"https://solvia.example.test/verify?t=" +
            saved.VerifyToken + "\"", payload);
        Assert.Contains("\"receiptIssuedAtUtc\":", payload);
        Assert.Contains("\"campus\":\"Fresno City College\"", payload);
        Assert.Contains("\"amountCents\":3000", payload);
        // No student PII, ever: no name, no email, no plate.
        Assert.DoesNotContain("Maria", payload);
        Assert.DoesNotContain("student.example", payload);
        Assert.DoesNotContain("8XYZ123", payload);
        Assert.DoesNotContain("plate", payload, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------- verifyUrl origin fallback
    // The checkout capture path (CheckoutService) has no HTTP
    // request, so it calls NotifyPermitIssuedAsync with a null
    // origin. Item 35 still wants the payload to carry the public
    // verify link — the configured Webhook:PublicBaseUrl supplies
    // the origin, an explicit caller origin beats it, and with
    // neither the link is absent (never invented).

    private static PermitApplication AddIssuedApplication(SolviaDbContext db)
    {
        var app = AddApplication(db, payment: PaymentStatus.Paid, captureId: "CAP-1");
        app.ReceiptNumber = "SOL-2026-000001";
        app.VerifyToken = "TOKEN-ABC";
        app.PaidAtUtc = DateTime.UtcNow;
        app.ReceiptIssuedAtUtc = DateTime.UtcNow;
        db.SaveChanges();
        return app;
    }

    [Fact]
    public async Task OutboundVerifyUrl_FallsBackToConfiguredPublicBaseUrl()
    {
        var w = NewWorld(publicBaseUrl: "https://config.example.test");
        using var _ = w.Connection;
        using var __ = w.Db;
        var app = AddIssuedApplication(w.Db);

        // Capture-path shape: no caller origin at all.
        await w.PaymentWebhook.NotifyPermitIssuedAsync(app, publicBaseUrl: null);

        var payload = Assert.Single(w.Outbound.Bodies);
        Assert.Contains(
            "\"verifyUrl\":\"https://config.example.test/verify?t=TOKEN-ABC\"",
            payload);
    }

    [Fact]
    public async Task OutboundVerifyUrl_ExplicitOriginWinsOverConfigured()
    {
        var w = NewWorld(publicBaseUrl: "https://config.example.test");
        using var _ = w.Connection;
        using var __ = w.Db;
        var app = AddIssuedApplication(w.Db);

        // Handler-path shape: the caller's request origin is passed.
        await w.PaymentWebhook.NotifyPermitIssuedAsync(
            app, "https://explicit.example.test");

        var payload = Assert.Single(w.Outbound.Bodies);
        Assert.Contains(
            "\"verifyUrl\":\"https://explicit.example.test/verify?t=TOKEN-ABC\"",
            payload);
        Assert.DoesNotContain("config.example.test", payload);
    }

    [Fact]
    public async Task OutboundVerifyUrl_AbsentWhenNoOriginAnywhere()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var app = AddIssuedApplication(w.Db);

        await w.PaymentWebhook.NotifyPermitIssuedAsync(app, publicBaseUrl: null);

        var payload = Assert.Single(w.Outbound.Bodies);
        Assert.Contains("\"verifyUrl\":null", payload);
        Assert.DoesNotContain("/verify?t=", payload);
    }
}
