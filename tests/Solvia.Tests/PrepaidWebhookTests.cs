using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Solvia.Config;
using Solvia.Data;
using Solvia.Models;
using Solvia.Models.Enums;
using Solvia.Services;

namespace Solvia.Tests;

/// <summary>Verified-signature stubs and an in-memory database, never actual PayPal calls.</summary>
public sealed partial class WebhookTests
{
    private sealed class PrepaidClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 24, 15, 0, 0, TimeSpan.Zero);
    }
    private sealed record PrepaidWorld(World Base, PrepaidPurchaseService Purchases,
        PayPalWebhookHandler GroupHandler, PrepaidPurchase Purchase) : IDisposable
    {
        public void Dispose() { Base.Db.Dispose(); Base.Connection.Dispose(); }
    }
    private static async Task<PrepaidWorld> NewPrepaidWorld()
    {
        var world = NewWorld();
        var catalog = new FeeCatalogService(RedoTestDb.FindFeesCsv());
        var pricing = new PricingService(catalog);
        var issuance = new IssuanceService(new PermitNumberService(world.Db));
        var applications = new ApplicationService(world.Db, catalog, pricing,
            new UserCategoryResolver(Options.Create(new CategoryOptions())), new TermDatesService(catalog, world.Db), issuance);
        var purchases = new PrepaidPurchaseService(world.Db, applications, world.PayPal, issuance, pricing, catalog, new PrepaidClock());
        var vehicle = AddVehicle(world.Db, "maria");
        var purchase = await purchases.CreateAsync("maria", vehicle.Id, "Fresno City College", TermKind.Fall,
            [new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 4)]);
        purchase.PayPalOrderId = "OFFLINE-PREPAID-ORDER";
        await world.Db.SaveChangesAsync();
        var handler = new PayPalWebhookHandler(world.Db, world.PayPal, issuance, world.PaymentWebhook,
            NullLogger<PayPalWebhookHandler>.Instance, purchases);
        return new(world, purchases, handler, purchase);
    }
    private static string PrepaidEvent(string eventId = "PREPAID-EVENT-1", string captureId = "OFFLINE-PREPAID-CAPTURE",
        string orderId = "OFFLINE-PREPAID-ORDER", string amount = "2.00", string currency = "USD", string status = "COMPLETED") =>
        JsonSerializer.Serialize(new
        {
            id = eventId, event_type = PayPalWebhookHandler.CaptureCompletedEventType,
            resource = new
            {
                id = captureId, status, amount = new { value = amount, currency_code = currency },
                supplementary_data = new { related_ids = new { order_id = orderId } }
            }
        });
    private static async Task<PrepaidPurchase> SavedPurchase(PrepaidWorld world)
    {
        await using var fresh = RedoTestDb.NewContext(world.Base.Connection);
        return await fresh.PrepaidPurchases.Include(p => p.Items).ThenInclude(i => i.Application)
            .SingleAsync(p => p.Id == world.Purchase.Id);
    }
    private static void AssertUnissued(PrepaidPurchase purchase)
    {
        Assert.Equal(PaymentStatus.Pending, purchase.PaymentStatus);
        Assert.Null(purchase.PayPalCaptureId); Assert.Null(purchase.PaidAtUtc);
        Assert.All(purchase.Items, item =>
        {
            Assert.Equal(PaymentStatus.Pending, item.Application.PaymentStatus);
            Assert.Null(item.Application.ReceiptNumber); Assert.Null(item.Application.VerifyToken);
            Assert.Null(item.Application.PayPalOrderId); Assert.Null(item.Application.PayPalCaptureId);
        });
    }

    [Fact]
    public async Task PrepaidWebhookRecoversOneWholePurchaseAndNotifiesEachSavedDatedPermit()
    {
        using var world = await NewPrepaidWorld();
        var outcome = await world.GroupHandler.HandleAsync(PrepaidEvent(), SignedHeaders(), "https://solvia.test");
        Assert.Equal(200, outcome.StatusCode); Assert.Equal(PayPalWebhookHandler.OutcomeActed, outcome.Outcome);
        var saved = await SavedPurchase(world);
        Assert.Equal(PaymentStatus.Paid, saved.PaymentStatus);
        Assert.Equal("OFFLINE-PREPAID-CAPTURE", saved.PayPalCaptureId); Assert.NotNull(saved.PaidAtUtc);
        Assert.All(saved.Items, item =>
        {
            Assert.Equal(PaymentStatus.Paid, item.Application.PaymentStatus);
            Assert.NotNull(item.Application.ReceiptNumber); Assert.NotNull(item.Application.VerifyToken);
            Assert.Equal(100, item.Application.PriceCents);
            Assert.Null(item.Application.PayPalOrderId); Assert.Null(item.Application.PayPalCaptureId);
        });
        Assert.Single(await EventLogs(world.Base));
        Assert.Equal(2, world.Base.Outbound.Bodies.Count);
        foreach (var body in world.Base.Outbound.Bodies)
        {
            using var json = JsonDocument.Parse(body);
            Assert.Equal(100, json.RootElement.GetProperty("amountCents").GetInt32());
            Assert.Equal("OFFLINE-PREPAID-CAPTURE", json.RootElement.GetProperty("paypalCaptureId").GetString());
            Assert.Contains("solvia.test/verify", json.RootElement.GetProperty("verifyUrl").GetString());
            Assert.DoesNotContain("8XYZ123", body);
        }
        Assert.Equal(1, world.Base.PayPal.VerifyCalls);
    }

    [Fact]
    public async Task PrepaidRedeliveryAndASecondEventForTheSameCaptureNeverNotifyAgain()
    {
        using var world = await NewPrepaidWorld();
        await world.GroupHandler.HandleAsync(PrepaidEvent(), SignedHeaders());
        var first = await SavedPurchase(world);
        var numbers = first.Items.OrderBy(i => i.ApplicationId).Select(i => i.Application.ReceiptNumber).ToArray();
        var duplicate = await world.GroupHandler.HandleAsync(PrepaidEvent(), SignedHeaders());
        Assert.Equal(PayPalWebhookHandler.OutcomeDuplicate, duplicate.Outcome);
        var repeatedCapture = await world.GroupHandler.HandleAsync(PrepaidEvent("PREPAID-EVENT-2"), SignedHeaders());
        Assert.Equal(PayPalWebhookHandler.OutcomeActed, repeatedCapture.Outcome);
        var saved = await SavedPurchase(world);
        Assert.Equal(numbers, saved.Items.OrderBy(i => i.ApplicationId).Select(i => i.Application.ReceiptNumber));
        Assert.Equal(first.PaidAtUtc, saved.PaidAtUtc);
        Assert.Equal(2, world.Base.Outbound.Bodies.Count); Assert.Equal(2, (await EventLogs(world.Base)).Count);
    }

    [Theory]
    [InlineData("1.00", "USD", "COMPLETED")]
    [InlineData("2.001", "USD", "COMPLETED")]
    [InlineData("2.00", "EUR", "COMPLETED")]
    [InlineData("2.00", "USD", "PENDING")]
    public async Task PrepaidMismatchedCompletedResourceCannotIssueAnyChild(string amount, string currency, string status)
    {
        using var world = await NewPrepaidWorld();
        var outcome = await world.GroupHandler.HandleAsync(PrepaidEvent(amount: amount, currency: currency, status: status), SignedHeaders());
        Assert.Equal(200, outcome.StatusCode); Assert.Equal(PayPalWebhookHandler.OutcomeFailed, outcome.Outcome);
        AssertUnissued(await SavedPurchase(world));
        Assert.Empty(world.Base.Outbound.Bodies);
        Assert.Equal(PayPalWebhookHandler.OutcomeFailed, Assert.Single(await EventLogs(world.Base)).Outcome);
    }

    [Theory]
    [InlineData("order")]
    [InlineData("capture")]
    public async Task PrepaidKnownCaptureOrOrderCannotAuthorizeAConflictingPartner(string conflict)
    {
        using var world = await NewPrepaidWorld();
        await world.GroupHandler.HandleAsync(PrepaidEvent(), SignedHeaders());
        var before = await SavedPurchase(world);
        var bad = conflict == "order"
            ? PrepaidEvent("CONFLICTING-ORDER", orderId: "OTHER-ORDER")
            : PrepaidEvent("CONFLICTING-CAPTURE", captureId: "OTHER-CAPTURE");
        var outcome = await world.GroupHandler.HandleAsync(bad, SignedHeaders());
        Assert.Equal(PayPalWebhookHandler.OutcomeFailed, outcome.Outcome);
        var saved = await SavedPurchase(world);
        Assert.Equal(before.PayPalOrderId, saved.PayPalOrderId); Assert.Equal(before.PayPalCaptureId, saved.PayPalCaptureId);
        Assert.Equal(PaymentStatus.Paid, saved.PaymentStatus); Assert.Equal(before.PaidAtUtc, saved.PaidAtUtc);
        Assert.Equal(2, world.Base.Outbound.Bodies.Count);
        Assert.All(saved.Items, item => Assert.Null(item.Application.PayPalCaptureId));
    }

    [Fact]
    public async Task PrepaidInvalidSignatureSavesNoLogAndLeavesTheWholePurchaseUnissued()
    {
        using var world = await NewPrepaidWorld(); world.Base.PayPal.VerifyResult = false;
        var outcome = await world.GroupHandler.HandleAsync(PrepaidEvent(), SignedHeaders());
        Assert.Equal(401, outcome.StatusCode); AssertUnissued(await SavedPurchase(world));
        Assert.Empty(await EventLogs(world.Base)); Assert.Empty(world.Base.Outbound.Bodies);
    }

    [Fact]
    public async Task PrepaidSaveFailureRollsBackParentChildrenAndEventLogBeforeAnyNotification()
    {
        using var world = await NewPrepaidWorld();
        await world.Base.Db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER reject_prepaid_paid BEFORE UPDATE OF PaymentStatus ON PermitApplications
            WHEN NEW.PaymentStatus = 'Paid'
            BEGIN SELECT RAISE(ABORT, 'offline atomic-save fixture'); END;
            """);
        var outcome = await world.GroupHandler.HandleAsync(PrepaidEvent(), SignedHeaders());
        Assert.Equal(503, outcome.StatusCode); Assert.Equal(PayPalWebhookHandler.OutcomeFailed, outcome.Outcome);
        AssertUnissued(await SavedPurchase(world));
        Assert.Empty(await EventLogs(world.Base)); Assert.Empty(world.Base.Outbound.Bodies);
    }

    [Fact]
    public async Task PrepaidTerminalRefundIsNotReissuedByACompletedEvent()
    {
        using var world = await NewPrepaidWorld();
        world.Purchase.PaymentStatus = PaymentStatus.Refunded;
        foreach (var item in world.Purchase.Items) item.Application.PaymentStatus = PaymentStatus.Refunded;
        await world.Base.Db.SaveChangesAsync();
        var outcome = await world.GroupHandler.HandleAsync(PrepaidEvent(), SignedHeaders());
        Assert.Equal(503, outcome.StatusCode);
        var saved = await SavedPurchase(world); Assert.Equal(PaymentStatus.Refunded, saved.PaymentStatus);
        Assert.All(saved.Items, item => Assert.Null(item.Application.ReceiptNumber));
        Assert.Empty(await EventLogs(world.Base)); Assert.Empty(world.Base.Outbound.Bodies);
    }
}
