using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Solvia.Controllers;
using Solvia.Models.Enums;
using Solvia.Services;

namespace Solvia.Tests;

public sealed partial class CheckoutTests
{
    [Fact]
    public async Task RedirectResume_UsesSameOrderAndApprovalLink()
    {
        var w = NewWorld();
        using var connection = w.Connection;
        using var db = w.Db;
        var app = AddApplication(db, orderId: "ORDER-1");
        var result = await w.Checkout.StartAsync(app.Id, "https://example.test/return", "https://example.test/cancel");
        Assert.Equal("https://www.sandbox.paypal.com/checkoutnow?token=ORDER-1", result.ApproveUrl);
        Assert.Equal(0, w.PayPal.CreateCalls);
        Assert.Equal(1, w.PayPal.GetCalls);
        Assert.Equal(PaymentStatus.Pending, (await Reload(w, app.Id)).PaymentStatus);
    }

    [Theory]
    [InlineData("APPROVED")]
    [InlineData("COMPLETED")]
    public async Task RedirectResume_ConfirmedCaptureIssuesOriginalApplication(string orderStatus)
    {
        var w = NewWorld();
        using var connection = w.Connection;
        using var db = w.Db;
        var app = AddApplication(db, orderId: "ORDER-1");
        w.PayPal.OrderStatus = orderStatus;
        var result = await w.Checkout.StartAsync(app.Id, "https://example.test/return", null);
        Assert.Equal(CheckoutStartKind.AlreadyPaid, result.Kind);
        Assert.Equal(0, w.PayPal.CreateCalls);
        var saved = await Reload(w, app.Id);
        Assert.Equal(PaymentStatus.Paid, saved.PaymentStatus);
        Assert.Equal("CAP-1", saved.PayPalCaptureId);
        Assert.NotNull(saved.ReceiptNumber);
    }

    [Fact]
    public async Task RedirectResume_PendingCaptureDoesNotIssue()
    {
        var w = NewWorld();
        using var connection = w.Connection;
        using var db = w.Db;
        var app = AddApplication(db, orderId: "ORDER-1");
        w.PayPal.OrderStatus = "APPROVED";
        w.PayPal.CaptureResult = new("PENDING", "CAP-1", 3000, "USD");
        await Assert.ThrowsAsync<CheckoutPricingException>(() => w.Checkout.StartAsync(app.Id, "https://example.test/return", null));
        Assert.Equal(PaymentStatus.Pending, (await Reload(w, app.Id)).PaymentStatus);
        Assert.Null((await Reload(w, app.Id)).ReceiptNumber);
    }

    [Theory]
    [InlineData("VOIDED")]
    [InlineData("SAVED")]
    public async Task RedirectResume_UnusableOrderReturnsErrorWithoutNewCharge(string status)
    {
        var w = NewWorld();
        using var connection = w.Connection;
        using var db = w.Db;
        var app = AddApplication(db, orderId: "ORDER-1");
        w.PayPal.OrderStatus = status;
        await Assert.ThrowsAsync<CheckoutPricingException>(() => w.Checkout.StartAsync(app.Id, "https://example.test/return", null));
        Assert.Equal(0, w.PayPal.CreateCalls);
        Assert.Equal(0, w.PayPal.CaptureCalls);
    }

    [Fact]
    public async Task ApiCapture_UnapprovedOrderReportsActionAndSupportReference()
    {
        var w = NewWorld();
        using var connection = w.Connection;
        using var db = w.Db;
        var app = AddApplication(db, orderId: "ORDER-1");
        w.PayPal.CaptureException = new PayPalApiException("capture", HttpStatusCode.UnprocessableEntity,
            "UNPROCESSABLE_ENTITY", "Approval required", "ORDER_NOT_APPROVED", "DEBUG-TEST");
        var response = Assert.IsType<ConflictObjectResult>(await NewPayPalController(w, "maria").Capture(new("ORDER-1"), default));
        var json = System.Text.Json.JsonSerializer.Serialize(response.Value);
        Assert.Contains("Personal buyer test account", json);
        Assert.Contains("ORDER_NOT_APPROVED", json);
        Assert.Contains("DEBUG-TEST", json);
        Assert.Null((await Reload(w, app.Id)).ReceiptNumber);
    }

    [Fact]
    public async Task ApiCapture_TimeoutPreservesUnpaidStateAndReturns503()
    {
        var w = NewWorld();
        using var connection = w.Connection;
        using var db = w.Db;
        var app = AddApplication(db, orderId: "ORDER-1");
        w.PayPal.CaptureException = new TaskCanceledException("simulated provider timeout");
        var response = Assert.IsType<ObjectResult>(await NewPayPalController(w, "maria").Capture(new("ORDER-1"), default));
        Assert.Equal(503, response.StatusCode);
        Assert.Equal(PaymentStatus.Pending, (await Reload(w, app.Id)).PaymentStatus);
    }
}

public sealed partial class PayPalServiceTests
{
    private sealed class ResumeHandler(string getBody, string? captureIssue = null) : HttpMessageHandler
    {
        public int GetCalls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/token"))
                return Task.FromResult(Response(HttpStatusCode.OK, "{\"access_token\":\"test\"}"));
            if (request.Method == HttpMethod.Get)
            {
                GetCalls++;
                return Task.FromResult(Response(HttpStatusCode.OK, getBody));
            }
            return Task.FromResult(Response(HttpStatusCode.UnprocessableEntity,
                "{\"name\":\"UNPROCESSABLE_ENTITY\",\"debug_id\":\"DEBUG-TEST\",\"details\":[{\"issue\":\"" + captureIssue + "\"}]}"));
        }
        private static HttpResponseMessage Response(HttpStatusCode status, string body) => new(status)
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static PayPalService ResumeService(ResumeHandler handler) => new(new HttpClient(handler),
        Microsoft.Extensions.Options.Options.Create(new Solvia.Config.PayPalOptions { ClientId = "test", ClientSecret = "test" }),
        Microsoft.Extensions.Logging.Abstractions.NullLogger<PayPalService>.Instance);

    [Fact]
    public async Task AlreadyCaptured_ReconcilesRealProviderCapture()
    {
        var handler = new ResumeHandler("""
            {"id":"ORDER-1","status":"COMPLETED","purchase_units":[{"payments":{"captures":[
            {"id":"CAP-RECOVERED","status":"COMPLETED","amount":{"value":"30.00","currency_code":"USD"}}]}}]}
            """, "ORDER_ALREADY_CAPTURED");
        var capture = await ResumeService(handler).CaptureOrderAsync("ORDER-1");
        Assert.Equal("CAP-RECOVERED", capture.CaptureId);
        Assert.Equal(3000, capture.AmountCents);
        Assert.Equal("USD", capture.Currency);
        Assert.Equal("COMPLETED", capture.Status);
        Assert.Equal(1, handler.GetCalls);
    }

    [Fact]
    public async Task UnapprovedCapture_PreservesProviderIssueWithoutInventingCapture()
    {
        var handler = new ResumeHandler("{}", "ORDER_NOT_APPROVED");
        var error = await Assert.ThrowsAsync<PayPalApiException>(() => ResumeService(handler).CaptureOrderAsync("ORDER-1"));
        Assert.Equal("ORDER_NOT_APPROVED", error.PayPalIssue);
        Assert.Equal("DEBUG-TEST", error.DebugId);
        Assert.Equal(0, handler.GetCalls);
    }

    [Fact]
    public async Task GetOrder_ReturnsExistingApprovalLink()
    {
        var handler = new ResumeHandler("""
            {"id":"ORDER-1","status":"CREATED","links":[{"rel":"approve","href":"https://www.sandbox.paypal.com/checkoutnow?token=ORDER-1"}]}
            """);
        var order = await ResumeService(handler).GetOrderAsync("ORDER-1");
        Assert.Equal("CREATED", order.Status);
        Assert.EndsWith("token=ORDER-1", order.ApproveUrl);
    }

    [Theory]
    [InlineData("ORDER-OTHER", "https://www.sandbox.paypal.com/checkoutnow?token=ORDER-1")]
    [InlineData("ORDER-1", "https://example.test/fake-approval")]
    public async Task GetOrder_RejectsWrongOrderOrApprovalHost(string id, string approval)
    {
        var handler = new ResumeHandler($$"""{"id":"{{id}}","status":"CREATED","links":[{"rel":"approve","href":"{{approval}}"}]}""");
        await Assert.ThrowsAsync<PayPalApiException>(() => ResumeService(handler).GetOrderAsync("ORDER-1"));
    }
}
