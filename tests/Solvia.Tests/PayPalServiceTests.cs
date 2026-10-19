using System.Net;
using System.Text;
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
/// T4 PayPal tests — fully OFFLINE. A fake <see cref="HttpMessageHandler"/>
/// plays the PayPal sandbox (token, create-order, capture), so no real
/// PayPal call ever leaves the test run. The live sandbox round trip is
/// the plan's done-test on Jose Luis's PC once his sandbox keys exist.
///
/// What is proven here:
/// - create-order sends intent CAPTURE, the exact USD amount from
///   PriceCents, and our reference — after a client-credentials token
///   call (Basic auth, grant_type=client_credentials);
/// - a COMPLETED capture marks the application Paid — with PaidAtUtc,
///   the capture id, a receipt/permit number, and a verify token;
/// - a non-COMPLETED capture NEVER marks Paid;
/// - missing credentials fail fast with a clear error that contains no
///   secret and makes no HTTP call;
/// - a $0 FREE permit confirms as NotRequired with no PayPal call.
/// </summary>
public sealed class PayPalServiceTests : IDisposable
{
    private const string SandboxBase = "https://api-m.sandbox.paypal.com/";

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<SolviaDbContext> _options;

    public PayPalServiceTests()
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
    // Create order: request shape
    // ------------------------------------------------------------------

    [Fact]
    public async Task CreateOrder_SendsCaptureIntentExactUsdAmountAndReference()
    {
        var handler = new FakePayPalHandler();
        var payPal = NewPayPalService(handler);

        var result = await payPal.CreateOrderAsync(
            amountCents: 3000,
            reference: "APP-REF-1",
            returnUrl: "https://localhost:5001/Checkout/Return",
            cancelUrl: "https://localhost:5001/Checkout/Cancel");

        Assert.Equal("ORDER-123", result.OrderId);
        Assert.Equal(
            "https://www.sandbox.paypal.com/checkoutnow?token=ORDER-123",
            result.ApproveUrl);

        // First call: the OAuth token exchange.
        var tokenRequest = handler.Requests[0];
        Assert.Equal("POST", tokenRequest.Method);
        Assert.Equal("/v1/oauth2/token", tokenRequest.Path);
        var expectedBasic = "Basic " + Convert.ToBase64String(
            Encoding.UTF8.GetBytes("test-client-id:test-client-secret"));
        Assert.Equal(expectedBasic, tokenRequest.Authorization);
        Assert.Contains("grant_type=client_credentials", tokenRequest.Body);

        // Second call: create order, Bearer-authenticated, exact shape.
        var createRequest = handler.Requests[1];
        Assert.Equal("POST", createRequest.Method);
        Assert.Equal("/v2/checkout/orders", createRequest.Path);
        Assert.Equal("Bearer TEST-ACCESS-TOKEN", createRequest.Authorization);

        using var body = JsonDocument.Parse(createRequest.Body!);
        var root = body.RootElement;
        Assert.Equal("CAPTURE", root.GetProperty("intent").GetString());
        var unit = root.GetProperty("purchase_units")[0];
        Assert.Equal("APP-REF-1", unit.GetProperty("reference_id").GetString());
        var amount = unit.GetProperty("amount");
        Assert.Equal("USD", amount.GetProperty("currency_code").GetString());
        Assert.Equal("30.00", amount.GetProperty("value").GetString());
        var context = root.GetProperty("application_context");
        Assert.Equal(
            "https://localhost:5001/Checkout/Return",
            context.GetProperty("return_url").GetString());
        Assert.Equal(
            "https://localhost:5001/Checkout/Cancel",
            context.GetProperty("cancel_url").GetString());
    }

    [Theory]
    [InlineData(3000, "30.00")]    // Fresno City semester fee
    [InlineData(22950, "229.50")]  // CSU Monterey Bay semester fee
    [InlineData(5, "0.05")]        // exact cents, never rounded dollars
    public async Task CreateOrder_FormatsCentsExactly(int cents, string expectedValue)
    {
        var handler = new FakePayPalHandler();
        var payPal = NewPayPalService(handler);

        await payPal.CreateOrderAsync(cents, "APP-REF-2");

        var createRequest = handler.Requests.Single(r => r.Path == "/v2/checkout/orders");
        using var body = JsonDocument.Parse(createRequest.Body!);
        var value = body.RootElement
            .GetProperty("purchase_units")[0]
            .GetProperty("amount")
            .GetProperty("value")
            .GetString();
        Assert.Equal(expectedValue, value);
    }

    // ------------------------------------------------------------------
    // Capture: the ONLY road to Paid
    // ------------------------------------------------------------------

    [Fact]
    public async Task Capture_Completed_MarksApplicationPaidWithCaptureId()
    {
        var handler = new FakePayPalHandler(); // capture answers COMPLETED
        var payPal = NewPayPalService(handler);

        Guid applicationId;
        using (var db = NewDb())
        {
            applicationId = SeedApplication(db, priceCents: 3000, orderId: "ORDER-123");
        }

        using (var db = NewDb())
        {
            var checkout = NewCheckoutService(db, payPal);
            var result = await checkout.CaptureAsync(applicationId);
            Assert.True(result.Completed);
            Assert.Equal("COMPLETED", result.Status);
            Assert.Equal("CAPTURE-789", result.CaptureId);
            Assert.False(string.IsNullOrWhiteSpace(result.ReceiptNumber));
        }

        using (var db = NewDb())
        {
            var saved = await db.PermitApplications.SingleAsync(a => a.Id == applicationId);
            Assert.Equal(PaymentStatus.Paid, saved.PaymentStatus);
            Assert.NotNull(saved.PaidAtUtc);
            Assert.Equal("CAPTURE-789", saved.PayPalCaptureId);
            Assert.Equal("ORDER-123", saved.PayPalOrderId);
            Assert.False(string.IsNullOrWhiteSpace(saved.ReceiptNumber));
            Assert.False(string.IsNullOrWhiteSpace(saved.VerifyToken));
        }
    }

    [Fact]
    public async Task Capture_NotCompleted_NeverMarksApplicationPaid()
    {
        var handler = new FakePayPalHandler
        {
            CaptureBody = """
                {
                  "id": "ORDER-123",
                  "status": "PENDING",
                  "purchase_units": [
                    { "payments": { "captures": [
                      { "id": "CAPTURE-PENDING", "status": "PENDING" }
                    ] } }
                  ]
                }
                """,
        };
        var payPal = NewPayPalService(handler);

        Guid applicationId;
        using (var db = NewDb())
        {
            applicationId = SeedApplication(db, priceCents: 3000, orderId: "ORDER-123");
        }

        using (var db = NewDb())
        {
            var checkout = NewCheckoutService(db, payPal);
            var result = await checkout.CaptureAsync(applicationId);
            Assert.False(result.Completed);
            Assert.Equal("PENDING", result.Status);
        }

        using (var db = NewDb())
        {
            var saved = await db.PermitApplications.SingleAsync(a => a.Id == applicationId);
            Assert.NotEqual(PaymentStatus.Paid, saved.PaymentStatus);
            Assert.Equal(PaymentStatus.Failed, saved.PaymentStatus);
            Assert.Null(saved.PaidAtUtc);
            Assert.Null(saved.PayPalCaptureId);
            Assert.Null(saved.ReceiptNumber);
        }
    }

    // ------------------------------------------------------------------
    // Credentials + the free case
    // ------------------------------------------------------------------

    [Fact]
    public async Task MissingCredentials_FailsFastWithClearErrorAndNoSecret()
    {
        var handler = new FakePayPalHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri(SandboxBase) };
        var options = Options.Create(new PayPalOptions
        {
            ClientId = "", // missing
            ClientSecret = "SENTINEL-SECRET-VALUE",
            BaseUrl = SandboxBase,
        });
        var payPal = new PayPalService(http, options, NullLogger<PayPalService>.Instance);

        var ex = await Assert.ThrowsAsync<PayPalNotConfiguredException>(
            () => payPal.CreateOrderAsync(3000, "APP-REF-3"));

        // Clear about WHAT is missing; silent about every value.
        Assert.Contains("PAYPAL_CLIENT_ID", ex.Message);
        Assert.DoesNotContain("SENTINEL-SECRET-VALUE", ex.Message);

        // Fail-fast: no HTTP call was even attempted (no crash loop).
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task FreePermit_Start_ConfirmsNotRequiredWithoutAnyPayPalCall()
    {
        var handler = new FakePayPalHandler();
        var payPal = NewPayPalService(handler);

        Guid applicationId;
        using (var db = NewDb())
        {
            applicationId = SeedApplication(db, priceCents: 0, orderId: null);
        }

        using (var db = NewDb())
        {
            var checkout = NewCheckoutService(db, payPal);
            var result = await checkout.StartAsync(
                applicationId, "https://localhost/return", "https://localhost/cancel");
            Assert.Equal(CheckoutStartKind.FreePermitConfirmed, result.Kind);
        }

        Assert.Empty(handler.Requests); // PayPal was never called for $0.

        using (var db = NewDb())
        {
            var saved = await db.PermitApplications.SingleAsync(a => a.Id == applicationId);
            Assert.Equal(PaymentStatus.NotRequired, saved.PaymentStatus);
            Assert.Null(saved.PayPalOrderId);
            Assert.False(string.IsNullOrWhiteSpace(saved.ReceiptNumber));
            Assert.False(string.IsNullOrWhiteSpace(saved.VerifyToken));
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private SolviaDbContext NewDb() => new(_options);

    private static PayPalService NewPayPalService(FakePayPalHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri(SandboxBase) };
        var options = Options.Create(new PayPalOptions
        {
            ClientId = "test-client-id",
            ClientSecret = "test-client-secret",
            Environment = "sandbox",
            BaseUrl = SandboxBase,
        });
        return new PayPalService(http, options, NullLogger<PayPalService>.Instance);
    }

    private static CheckoutService NewCheckoutService(SolviaDbContext db, IPayPalService payPal) =>
        new(db, payPal, new PermitNumberService(db), NullLogger<CheckoutService>.Instance);

    /// <summary>
    /// Seeds Maria's vehicle + one Pending application at the given price,
    /// optionally already carrying a PayPal order id. Returns the id.
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

    /// <summary>
    /// An offline stand-in for the PayPal sandbox: records every request
    /// and answers token / create-order / capture with canned JSON.
    /// <see cref="CaptureBody"/> is settable so a test can make the
    /// capture come back as something other than COMPLETED.
    /// </summary>
    private sealed class FakePayPalHandler : HttpMessageHandler
    {
        public sealed record RecordedRequest(
            string Method, string Path, string? Body, string? Authorization);

        public List<RecordedRequest> Requests { get; } = [];

        public string CaptureBody { get; set; } = """
            {
              "id": "ORDER-123",
              "status": "COMPLETED",
              "purchase_units": [
                { "payments": { "captures": [
                  { "id": "CAPTURE-789", "status": "COMPLETED" }
                ] } }
              ]
            }
            """;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add(new RecordedRequest(
                request.Method.Method,
                path,
                body,
                request.Headers.Authorization?.ToString()));

            if (path == "/v1/oauth2/token")
            {
                return Json("""
                    { "access_token": "TEST-ACCESS-TOKEN", "token_type": "Bearer", "expires_in": 32400 }
                    """);
            }

            if (path == "/v2/checkout/orders")
            {
                return Json("""
                    {
                      "id": "ORDER-123",
                      "status": "CREATED",
                      "links": [
                        { "href": "https://api-m.sandbox.paypal.com/v2/checkout/orders/ORDER-123", "rel": "self", "method": "GET" },
                        { "href": "https://www.sandbox.paypal.com/checkoutnow?token=ORDER-123", "rel": "approve", "method": "GET" }
                      ]
                    }
                    """);
            }

            if (path.EndsWith("/capture", StringComparison.Ordinal))
            {
                return Json(CaptureBody);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string json) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
    }
}
