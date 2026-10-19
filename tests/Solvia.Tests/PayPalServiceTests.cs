using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Solvia.Config;
using Solvia.Services;

namespace Solvia.Tests;

/// <summary>
/// R6 PayPal client tests (REDO-PLAN items 30–32): the one PayPal
/// talker, proven against a FAKE <see cref="HttpMessageHandler"/>
/// (the sandbox blocks app-process TCP — the round trip is proven
/// at the handler level, per the Lyceum). Exact money formatting,
/// capture-id parsing, not-configured failing BEFORE any HTTP, and
/// webhook-signature verification for R7.
/// </summary>
public sealed class PayPalServiceTests
{
    // ------------------------------------------------------- harness

    private sealed class FakePayPalHandler : HttpMessageHandler
    {
        public int TokenCalls;
        public int CreateCalls;
        public int CaptureCalls;
        public int VerifyCalls;
        public string? LastCreateBody;
        public string CaptureStatus = "COMPLETED";
        public string VerifyStatus = "SUCCESS";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/v1/oauth2/token", StringComparison.Ordinal))
            {
                TokenCalls++;
                return Json("""{"access_token":"test-token","token_type":"Bearer","expires_in":32400}""");
            }

            if (path.EndsWith("/v1/notifications/verify-webhook-signature", StringComparison.Ordinal))
            {
                VerifyCalls++;
                return Json($$"""{"verification_status":"{{VerifyStatus}}"}""");
            }

            if (path.Contains("/capture", StringComparison.Ordinal))
            {
                CaptureCalls++;
                return Json(
                    "{\"id\":\"ORDER-1\",\"status\":\"" + CaptureStatus + "\"," +
                    "\"purchase_units\":[{\"reference_id\":\"abc\",\"payments\":" +
                    "{\"captures\":[{\"id\":\"CAP-9\",\"status\":\"" + CaptureStatus +
                    "\"}]}}]}");
            }

            if (path.EndsWith("/v2/checkout/orders", StringComparison.Ordinal))
            {
                CreateCalls++;
                LastCreateBody = request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync(cancellationToken);
                return Json("""
                    {"id":"ORDER-1","status":"CREATED","links":[
                      {"href":"https://api-m.sandbox.paypal.com/v2/checkout/orders/ORDER-1","rel":"self","method":"GET"},
                      {"href":"https://www.sandbox.paypal.com/checkoutnow?token=ORDER-1","rel":"approve","method":"GET"}]}
                    """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent(
                    """{"name":"RESOURCE_NOT_FOUND","message":"not here"}""",
                    Encoding.UTF8, "application/json"),
            };
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }

    private static PayPalService NewService(
        FakePayPalHandler handler, PayPalOptions? options = null)
    {
        options ??= new PayPalOptions
        {
            ClientId = "test-client-id",
            ClientSecret = "test-client-secret",
            WebhookId = "WH-TEST-1",
        };
        return new PayPalService(
            new HttpClient(handler),
            Options.Create(options),
            NullLogger<PayPalService>.Instance);
    }

    // --------------------------------------------------------- tests

    [Fact]
    public async Task CreateOrder_ReturnsIdAndApproveUrl_SendsExactMoney()
    {
        var handler = new FakePayPalHandler();
        var service = NewService(handler);

        var order = await service.CreateOrderAsync(
            3000, "applicationref", "https://example.test/return", "https://example.test/cancel");

        Assert.Equal("ORDER-1", order.OrderId);
        Assert.Contains("sandbox.paypal.com", order.ApproveUrl);
        Assert.Equal(1, handler.TokenCalls);
        Assert.Equal(1, handler.CreateCalls);
        // Exact money: 3000 cents is the string "30.00", currency USD,
        // tagged with the reference — never a float, never a guess.
        Assert.Contains("\"value\":\"30.00\"", handler.LastCreateBody);
        Assert.Contains("\"currency_code\":\"USD\"", handler.LastCreateBody);
        Assert.Contains("\"reference_id\":\"applicationref\"", handler.LastCreateBody);
        Assert.Contains("\"return_url\":\"https://example.test/return\"", handler.LastCreateBody);
    }

    [Fact]
    public async Task Capture_Completed_ParsesStatusAndCaptureId()
    {
        var handler = new FakePayPalHandler();
        var service = NewService(handler);

        var capture = await service.CaptureOrderAsync("ORDER-1");

        Assert.Equal("COMPLETED", capture.Status);
        Assert.Equal("CAP-9", capture.CaptureId);
        Assert.Equal(1, handler.CaptureCalls);
    }

    [Fact]
    public async Task Capture_NonCompleted_StatusPassesThroughVerbatim()
    {
        var handler = new FakePayPalHandler { CaptureStatus = "PENDING" };
        var service = NewService(handler);

        var capture = await service.CaptureOrderAsync("ORDER-1");

        // The service reports; CheckoutService decides. PENDING is
        // NOT completed and must arrive unaltered.
        Assert.Equal("PENDING", capture.Status);
    }

    [Fact]
    public async Task NotConfigured_ThrowsBeforeAnyHttpCall()
    {
        var handler = new FakePayPalHandler();
        var service = NewService(handler, new PayPalOptions
        {
            ClientId = "",
            ClientSecret = "",
        });

        var ex = await Assert.ThrowsAsync<PayPalNotConfiguredException>(
            () => service.CreateOrderAsync(3000, "ref"));
        Assert.Contains("PAYPAL_CLIENT_ID", ex.Message);
        Assert.Equal(0, handler.TokenCalls);
        Assert.Equal(0, handler.CreateCalls);
    }

    [Fact]
    public async Task VerifyWebhookSignature_Success_ReturnsTrue()
    {
        var handler = new FakePayPalHandler();
        var service = NewService(handler);

        var verified = await service.VerifyWebhookSignatureAsync(
            "trans-1", "2026-10-18T00:00:00Z", "sig", "https://cert.example/x.pem",
            "SHA256withRSA", """{"id":"evt-1","event_type":"PAYMENT.CAPTURE.COMPLETED"}""");

        Assert.True(verified);
        Assert.Equal(1, handler.VerifyCalls);
    }

    [Fact]
    public async Task VerifyWebhookSignature_Failure_ReturnsFalse()
    {
        var handler = new FakePayPalHandler { VerifyStatus = "FAILURE" };
        var service = NewService(handler);

        var verified = await service.VerifyWebhookSignatureAsync(
            "trans-1", "2026-10-18T00:00:00Z", "sig", "https://cert.example/x.pem",
            "SHA256withRSA", """{"id":"evt-1"}""");

        Assert.False(verified);
    }

    [Fact]
    public async Task VerifyWebhookSignature_NoWebhookIdConfigured_ReturnsFalseWithoutCalling()
    {
        var handler = new FakePayPalHandler();
        var service = NewService(handler, new PayPalOptions
        {
            ClientId = "test-client-id",
            ClientSecret = "test-client-secret",
            WebhookId = "",
        });

        var verified = await service.VerifyWebhookSignatureAsync(
            "trans-1", "2026-10-18T00:00:00Z", "sig", "https://cert.example/x.pem",
            "SHA256withRSA", """{"id":"evt-1"}""");

        Assert.False(verified);
        Assert.Equal(0, handler.VerifyCalls);
        Assert.Equal(0, handler.TokenCalls);
    }
}
