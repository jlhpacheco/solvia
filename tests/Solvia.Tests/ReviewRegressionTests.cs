using Microsoft.EntityFrameworkCore;
using Solvia.Config;
using Solvia.Models.Enums;
using Solvia.Services;

namespace Solvia.Tests;

public sealed partial class MyPermitsTests
{
    [Theory]
    [InlineData(PaymentStatus.Refunded)]
    [InlineData(PaymentStatus.PartiallyRefunded)]
    [InlineData(PaymentStatus.Expired)]
    [InlineData(PaymentStatus.NotRequired)]
    public async Task ClosedOrExemptPermit_HidesPaymentLinks(PaymentStatus payment)
    {
        var w = NewWorld();
        using var connection = w.Connection;
        using var db = w.Db;
        var scenario = await MariaScenarioAsync(w);
        scenario.Pending.PaymentStatus = payment;
        scenario.Pending.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync();
        var controller = NewController(w, "maria");
        var view = Assert.IsType<Microsoft.AspNetCore.Mvc.ViewResult>(await controller.My(default));
        var model = Assert.IsType<Solvia.Models.ViewModels.MyPermitsViewModel>(view.Model);
        Assert.False(model.Rows.Single(row => row.Id == scenario.Pending.Id).ShowPayNow);
        Assert.Equal(0, model.PendingPaymentCount);
        var receipt = Assert.IsType<Microsoft.AspNetCore.Mvc.ViewResult>(await controller.Receipt(scenario.Pending.Id, default));
        Assert.False(Assert.IsType<Solvia.Models.ViewModels.ReceiptViewModel>(receipt.Model).CanPay);
    }
}

public sealed partial class CheckoutTests
{
    [Fact]
    public async Task StalePriceSnapshot_StopsBeforeCreatingProviderOrder()
    {
        var w = NewWorld();
        using var connection = w.Connection;
        using var db = w.Db;
        var app = AddApplication(db);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE PermitApplications SET ConcurrencyToken = {Guid.NewGuid()}, PriceCents = {4000} WHERE Id = {app.Id}");
        // Checkout now reloads authoritative facts under the write transaction,
        // so the changed amount is rejected by pricing before an optimistic-save
        // conflict is needed. It must still make no provider call or reprice.
        await Assert.ThrowsAsync<CheckoutPricingException>(() => w.Checkout.StartAsync(app.Id, null, null));
        Assert.Equal(0, w.PayPal.CreateCalls);
        Assert.Equal(0, w.PayPal.GetCalls);
        Assert.Equal(0, w.PayPal.CaptureCalls);
        var saved = await Reload(w, app.Id);
        Assert.Equal(4000, saved.PriceCents);
        Assert.Null(saved.PayPalOrderId);
    }

    [Fact]
    public async Task ConcurrentWebhookPaid_SaveConflictReturnsDurableReceipt()
    {
        var w = NewWorld();
        using var connection = w.Connection;
        using var db = w.Db;
        var app = AddApplication(db, orderId: "ORDER-1");
        w.PayPal.BeforeCapture = () => db.Database.ExecuteSqlInterpolated($"UPDATE PermitApplications SET PaymentStatus = {"Paid"}, PayPalCaptureId = {"CAP-WEBHOOK"}, ReceiptNumber = {"RECEIPT-WEBHOOK"}, ConcurrencyToken = {Guid.NewGuid()} WHERE Id = {app.Id}");
        var result = await w.Checkout.CaptureAsync(app.Id);
        Assert.True(result.Completed);
        Assert.Equal("CAP-WEBHOOK", result.CaptureId);
        Assert.Equal("RECEIPT-WEBHOOK", result.ReceiptNumber);
        Assert.Equal("RECEIPT-WEBHOOK", (await Reload(w, app.Id)).ReceiptNumber);
    }

    [Theory]
    [InlineData(2999, "USD", "CAP-1")]
    [InlineData(3001, "USD", "CAP-1")]
    [InlineData(3000, "EUR", "CAP-1")]
    [InlineData(3000, "USD", "")]
    public async Task CompletedCapture_WithInvalidMoneyOrMissingId_DoesNotIssue(int cents, string currency, string captureId)
    {
        var w = NewWorld();
        using var connection = w.Connection;
        using var db = w.Db;
        var app = AddApplication(db, orderId: "ORDER-1");
        w.PayPal.CaptureResult = new("COMPLETED", captureId, cents, currency);
        var result = await w.Checkout.CaptureAsync(app.Id);
        Assert.False(result.Completed);
        Assert.Equal("VERIFICATION_FAILED", result.Status);
        var saved = await Reload(w, app.Id);
        Assert.NotEqual(PaymentStatus.Paid, saved.PaymentStatus);
        Assert.Null(saved.ReceiptNumber);
    }

    [Fact]
    public async Task PendingCapture_RemainsPendingAndRetainsCaptureIdForWebhook()
    {
        var w = NewWorld();
        using var connection = w.Connection;
        using var db = w.Db;
        var app = AddApplication(db, orderId: "ORDER-1");
        w.PayPal.CaptureResult = new("PENDING", "CAP-1", 3000, "USD");
        var result = await w.Checkout.CaptureAsync(app.Id);
        Assert.False(result.Completed);
        var saved = await Reload(w, app.Id);
        Assert.Equal(PaymentStatus.Pending, saved.PaymentStatus);
        Assert.Equal("CAP-1", saved.PayPalCaptureId);
        Assert.Null(saved.ReceiptNumber);
    }

    [Theory]
    [InlineData(PaymentStatus.Refunded)]
    [InlineData(PaymentStatus.PartiallyRefunded)]
    [InlineData(PaymentStatus.Expired)]
    public async Task ClosedPaymentStates_CannotRestartCheckout(PaymentStatus status)
    {
        var w = NewWorld();
        using var connection = w.Connection;
        using var db = w.Db;
        var app = AddApplication(db, payment: status, orderId: "ORDER-1");
        await Assert.ThrowsAsync<CheckoutPricingException>(() => w.Checkout.StartAsync(app.Id, null, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => w.Checkout.CaptureAsync(app.Id));
        Assert.Equal(0, w.PayPal.CreateCalls);
        Assert.Equal(0, w.PayPal.CaptureCalls);
    }
}

public sealed partial class WebhookTests
{
    [Theory]
    [InlineData("30.001")]
    [InlineData("30,00")]
    [InlineData("99999999999999999999999")]
    public async Task CaptureWithInvalidPrecisionOrRange_NeverIssues(string value)
    {
        var w = NewWorld();
        using var connection = w.Connection;
        using var db = w.Db;
        var app = AddApplication(db, captureId: "CAP-1");
        await w.Handler.HandleAsync(CaptureCompletedEvent("EVT-MONEY", "CAP-1", value), SignedHeaders());
        var saved = await Reload(w, app.Id);
        Assert.NotEqual(PaymentStatus.Paid, saved.PaymentStatus);
        Assert.Null(saved.ReceiptNumber);
    }

    [Fact]
    public async Task CurrencyMismatch_NeverIssues()
    {
        var w = NewWorld();
        using var connection = w.Connection;
        using var db = w.Db;
        var app = AddApplication(db, captureId: "CAP-1");
        var body = CaptureCompletedEvent("EVT-CURRENCY", "CAP-1", "30.00").Replace("USD", "EUR");
        await w.Handler.HandleAsync(body, SignedHeaders());
        Assert.NotEqual(PaymentStatus.Paid, (await Reload(w, app.Id)).PaymentStatus);
    }

    [Fact]
    public async Task UnmatchedEvent_CanRecoverOnRedeliveryAfterOrderIsSaved()
    {
        var w = NewWorld();
        using var connection = w.Connection;
        using var db = w.Db;
        var app = AddApplication(db);
        var body = CaptureCompletedEvent("EVT-LATE", "CAP-1", "30.00", "ORDER-LATE");
        Assert.Equal(503, (await w.Handler.HandleAsync(body, SignedHeaders())).StatusCode);
        Assert.Empty(await EventLogs(w));
        var tracked = await db.PermitApplications.SingleAsync(a => a.Id == app.Id);
        tracked.PayPalOrderId = "ORDER-LATE";
        tracked.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync();
        Assert.Equal(200, (await w.Handler.HandleAsync(body, SignedHeaders())).StatusCode);
        Assert.Equal(PaymentStatus.Paid, (await Reload(w, app.Id)).PaymentStatus);
        Assert.Single(await EventLogs(w));
    }

    [Fact]
    public async Task DatabaseWriteFailure_IsRetryableAndNotMistakenForDuplicate()
    {
        var w = NewWorld();
        using var connection = w.Connection;
        using var db = w.Db;
        var app = AddApplication(db, captureId: "CAP-1");
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_payment BEFORE UPDATE ON PermitApplications BEGIN SELECT RAISE(FAIL, 'injected write failure'); END;");
        var body = CaptureCompletedEvent("EVT-RETRY", "CAP-1", "30.00");
        Assert.Equal(503, (await w.Handler.HandleAsync(body, SignedHeaders())).StatusCode);
        Assert.Empty(await EventLogs(w));
        Assert.Equal(PaymentStatus.Pending, (await Reload(w, app.Id)).PaymentStatus);
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_payment;");
        Assert.Equal(200, (await w.Handler.HandleAsync(body, SignedHeaders())).StatusCode);
        Assert.Equal(PaymentStatus.Paid, (await Reload(w, app.Id)).PaymentStatus);
    }
}

public sealed partial class PriceRepairTests
{
    [Fact]
    public async Task PendingApplicationWithOpenPayPalOrder_KeepsItsAgreedPrice()
    {
        var w = NewWorld();
        using var connection = w.Connection;
        using var db = w.Db;
        var app = await ApplyAsync(w, "maria", Fresno);
        app.PayPalOrderId = "ORDER-PRICE";
        app.PriceCents = 2500;
        app.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync();
        var report = await FreshRepair(w).RepairPendingAsync();
        Assert.Empty(report.Changes);
        Assert.Single(report.Skipped);
        Assert.Equal(2500, await PriceOfAsync(w, app.Id));
    }
}

public sealed partial class PayPalServiceTests
{
    [Theory]
    [InlineData("live", "https://api-m.sandbox.paypal.com", "USD")]
    [InlineData("sandbox", "https://api-m.paypal.com", "USD")]
    [InlineData("sandbox", "http://api-m.sandbox.paypal.com", "USD")]
    [InlineData("sandbox", "https://api-m.sandbox.paypal.com", "EUR")]
    public async Task UnsafeDemoConfiguration_IsRejectedBeforeSendingCredentials(string environment, string url, string currency)
    {
        var handler = new FakePayPalHandler();
        var service = NewService(handler, new PayPalOptions
        { ClientId = "fake", ClientSecret = "fake", Environment = environment, BaseUrl = url, Currency = currency });
        await Assert.ThrowsAsync<PayPalNotConfiguredException>(() => service.GetAccessTokenAsync());
        Assert.Equal(0, handler.TokenCalls);
    }
}
