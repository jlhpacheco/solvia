using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Solvia.Config;
using Solvia.Controllers;
using Solvia.Data;
using Solvia.Models;
using Solvia.Models.Enums;
using Solvia.Services;

namespace Solvia.Tests;

/// <summary>
/// R6 money-brain tests (REDO-PLAN items 19, 30, 31, 60): the
/// CheckoutService rules over a stub <see cref="IPayPalService"/>
/// (create is idempotent — the stub counts every call; Employee
/// flips to issued with no order; only a COMPLETED capture pays),
/// and the two controllers walked with crafted HttpContexts —
/// guards, owner checks, and the return endpoint's refusal to
/// render a success state for a bogus token.
/// </summary>
public sealed class CheckoutTests
{
    // ------------------------------------------------------- harness

    /// <summary>The PayPal seam, answered by the test.</summary>
    private sealed class StubPayPal : IPayPalService
    {
        public int CreateCalls;
        public int CaptureCalls;
        public string CreateOrderId = "ORDER-1";
        public PayPalCaptureResult CaptureResult = new("COMPLETED", "CAP-1");
        public bool ThrowNotConfigured;

        public Task<string> GetAccessTokenAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult("test-token");

        public Task<PayPalOrderResult> CreateOrderAsync(
            int amountCents, string reference, string? returnUrl = null,
            string? cancelUrl = null, CancellationToken cancellationToken = default)
        {
            if (ThrowNotConfigured)
            {
                throw new PayPalNotConfiguredException(
                    "PayPal sandbox credentials are not configured — test stub.");
            }

            CreateCalls++;
            return Task.FromResult(new PayPalOrderResult(
                CreateOrderId, "CREATED",
                $"https://www.sandbox.paypal.com/checkoutnow?token={CreateOrderId}"));
        }

        public Task<PayPalCaptureResult> CaptureOrderAsync(
            string orderId, CancellationToken cancellationToken = default)
        {
            CaptureCalls++;
            return Task.FromResult(CaptureResult);
        }

        public Task<bool> VerifyWebhookSignatureAsync(
            string transmissionId, string transmissionTime, string transmissionSig,
            string certUrl, string authAlgo, string webhookEventJson,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed record World(
        SolviaDbContext Db,
        SqliteConnection Connection,
        StubPayPal PayPal,
        CheckoutService Checkout,
        ApplicationService Applications,
        IFeeCatalog Catalog);

    private static World NewWorld(IFeeCatalog? catalog = null)
    {
        var (db, connection) = RedoTestDb.Create();
        var fees = catalog ?? new FeeCatalogService(RedoTestDb.FindFeesCsv());
        var numbers = new PermitNumberService(db);
        var issuance = new IssuanceService(numbers);
        var payPal = new StubPayPal();
        var checkout = new CheckoutService(
            db, payPal, issuance, fees, new PricingService(fees),
            NullLogger<CheckoutService>.Instance);
        var applications = new ApplicationService(
            db, fees, new PricingService(fees),
            new UserCategoryResolver(Options.Create(new CategoryOptions())),
            new TermDatesService(fees, db), issuance);
        return new World(db, connection, payPal, checkout, applications, fees);
    }

    /// <summary>A catalog with ONE synthetic campus, priced by the test's row.</summary>
    private static FeeCatalogService SyntheticCatalog(string row)
    {
        var header = File.ReadLines(RedoTestDb.FindFeesCsv()).First();
        var path = Path.Combine(
            Path.GetTempPath(), $"solvia-r6-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, header + "\n" + row + "\n");
        return new FeeCatalogService(path);
    }

    // Term published ($30), day-pass NOT published (R4's proven row).
    private const string DayPassUnpublishedRow =
        "Synthetic College,$30 Fall/Spring (cars),not listed on PMB page," +
        "not listed on PMB page,not listed on PMB page,not listed on PMB page," +
        "not listed on PMB page,synthetic R6 test row";

    // Term published as $0 — a genuinely free campus.
    private const string FreeTermRow =
        "Free College,$0 Fall/Spring (cars),not listed on PMB page," +
        "not listed on PMB page,not listed on PMB page,not listed on PMB page," +
        "not listed on PMB page,synthetic R6 test row";

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
        string campus = "Fresno City College",
        PermitTypeCode code = PermitTypeCode.Term,
        string? orderId = null,
        Vehicle? vehicle = null)
    {
        var type = db.PermitTypes.Single(t => t.Code == code);
        var application = new PermitApplication
        {
            Id = Guid.NewGuid(),
            StudentId = studentId,
            Campus = campus,
            Category = category,
            VehicleId = (vehicle ?? AddVehicle(db, studentId)).Id,
            PermitTypeId = type.Id,
            Term = TermKind.Fall,
            Source = "form",
            StartDate = new DateOnly(2026, 8, 24),
            EndDate = new DateOnly(2026, 12, 22),
            Status = ApplicationStatus.Pending,
            PriceCents = priceCents,
            PaymentStatus = payment,
            PayPalOrderId = orderId,
            SubmittedAtUtc = DateTime.UtcNow,
            ConcurrencyToken = Guid.NewGuid(),
        };
        db.PermitApplications.Add(application);
        db.SaveChanges();
        return application;
    }

    private static async Task<PermitApplication> Reload(
        World w, Guid id) =>
        await RedoTestDb.NewContext(w.Connection).PermitApplications
            .SingleAsync(a => a.Id == id);

    // -------------------------------------------- CheckoutService: start

    [Fact]
    public async Task Start_Twice_Idempotent_HandlerSawExactlyOneCreate()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var app = AddApplication(w.Db);

        var first = await w.Checkout.StartAsync(app.Id, null, null);
        var second = await w.Checkout.StartAsync(app.Id, null, null);

        Assert.Equal(CheckoutStartKind.OrderReady, first.Kind);
        Assert.Equal(CheckoutStartKind.OrderReady, second.Kind);
        Assert.Equal("ORDER-1", first.OrderId);
        // The second start returns the STORED order — same id, and
        // PayPal saw exactly one create call across both starts.
        Assert.Equal(first.OrderId, second.OrderId);
        Assert.Equal(1, w.PayPal.CreateCalls);
        Assert.Equal("ORDER-1", (await Reload(w, app.Id)).PayPalOrderId);
    }

    [Fact]
    public async Task Start_Employee_FlipsToNotRequired_Issues_NoOrder()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        // A Pending Employee row (the defensive state item 30 ii
        // exists for — creation normally settles employees itself).
        var app = AddApplication(
            w.Db, studentId: "elena", category: UserCategory.Employee);

        var result = await w.Checkout.StartAsync(app.Id, null, null);

        Assert.Equal(CheckoutStartKind.NotRequired, result.Kind);
        Assert.Equal(0, w.PayPal.CreateCalls);
        Assert.Equal(0, w.PayPal.CaptureCalls);
        var saved = await Reload(w, app.Id);
        Assert.Equal(PaymentStatus.NotRequired, saved.PaymentStatus);
        Assert.NotNull(saved.ReceiptNumber);
        Assert.NotNull(saved.VerifyToken);
        Assert.NotNull(saved.ReceiptIssuedAtUtc);
    }

    [Fact]
    public async Task Start_AlreadyPaid_AnswersNotRequired_NoPayPalCall()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var app = AddApplication(w.Db, payment: PaymentStatus.Paid, orderId: "ORDER-OLD");

        var result = await w.Checkout.StartAsync(app.Id, null, null);

        Assert.Equal(CheckoutStartKind.AlreadyPaid, result.Kind);
        Assert.Equal(0, w.PayPal.CreateCalls);
    }

    [Fact]
    public async Task Start_BlockedDraft_UnpublishedPrice_HardPricingError()
    {
        var catalog = SyntheticCatalog(DayPassUnpublishedRow);
        var w = NewWorld(catalog);
        using var _ = w.Connection;
        using var __ = w.Db;
        // A Daily draft whose price was parked at 0 because the
        // campus publishes no day-pass price (the AI door's blocked
        // draft). Charging $0 — or waving it through as free —
        // would both be lies.
        var app = AddApplication(
            w.Db, campus: "Synthetic College", priceCents: 0,
            code: PermitTypeCode.Daily);

        var ex = await Assert.ThrowsAsync<CheckoutPricingException>(
            () => w.Checkout.StartAsync(app.Id, null, null));

        Assert.Contains("Synthetic College", ex.Message);
        Assert.Equal(0, w.PayPal.CreateCalls);
        var saved = await Reload(w, app.Id);
        Assert.Equal(PaymentStatus.Pending, saved.PaymentStatus);
        Assert.Null(saved.ReceiptNumber);
    }

    [Fact]
    public async Task Start_GenuineZeroFee_SettlesNotRequired_AndIssues()
    {
        var catalog = SyntheticCatalog(FreeTermRow);
        var w = NewWorld(catalog);
        using var _ = w.Connection;
        using var __ = w.Db;
        var app = AddApplication(
            w.Db, campus: "Free College", priceCents: 0);

        var result = await w.Checkout.StartAsync(app.Id, null, null);

        Assert.Equal(CheckoutStartKind.NotRequired, result.Kind);
        Assert.Equal(0, w.PayPal.CreateCalls);
        var saved = await Reload(w, app.Id);
        Assert.Equal(PaymentStatus.NotRequired, saved.PaymentStatus);
        Assert.NotNull(saved.ReceiptNumber);
    }

    [Fact]
    public async Task Start_PayPalNotConfigured_PropagatesHonestly()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        w.PayPal.ThrowNotConfigured = true;
        var app = AddApplication(w.Db);

        await Assert.ThrowsAsync<PayPalNotConfiguredException>(
            () => w.Checkout.StartAsync(app.Id, null, null));
        // Nothing was stamped: no order id, still Pending.
        var saved = await Reload(w, app.Id);
        Assert.Null(saved.PayPalOrderId);
        Assert.Equal(PaymentStatus.Pending, saved.PaymentStatus);
    }

    // ------------------------------------------ CheckoutService: capture

    [Fact]
    public async Task Capture_Completed_IsTheOnlyPaidPath()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var app = AddApplication(w.Db, orderId: "ORDER-1");

        var result = await w.Checkout.CaptureAsync(app.Id);

        Assert.True(result.Completed);
        Assert.Equal("CAP-1", result.CaptureId);
        Assert.NotNull(result.ReceiptNumber);
        var saved = await Reload(w, app.Id);
        Assert.Equal(PaymentStatus.Paid, saved.PaymentStatus);
        Assert.Equal("CAP-1", saved.PayPalCaptureId);
        Assert.NotNull(saved.PaidAtUtc);
        Assert.NotNull(saved.ReceiptNumber);
        Assert.NotNull(saved.VerifyToken);
        Assert.NotNull(saved.ReceiptIssuedAtUtc);
        Assert.Equal(1, w.PayPal.CaptureCalls);
    }

    [Fact]
    public async Task Capture_NonCompleted_Fails_WithoutIssuance()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        w.PayPal.CaptureResult = new PayPalCaptureResult("DECLINED", null);
        var app = AddApplication(w.Db, orderId: "ORDER-1");

        var result = await w.Checkout.CaptureAsync(app.Id);

        Assert.False(result.Completed);
        Assert.Equal("DECLINED", result.Status);
        var saved = await Reload(w, app.Id);
        Assert.Equal(PaymentStatus.Failed, saved.PaymentStatus);
        Assert.Null(saved.PaidAtUtc);
        Assert.Null(saved.ReceiptNumber);
        Assert.Null(saved.VerifyToken);
    }

    [Fact]
    public async Task Capture_AlreadyPaid_ReportsTruth_NoSecondCaptureCall()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var app = AddApplication(
            w.Db, payment: PaymentStatus.Paid, orderId: "ORDER-1");
        app.PayPalCaptureId = "CAP-OLD";
        await w.Db.SaveChangesAsync();

        var result = await w.Checkout.CaptureAsync(app.Id);

        Assert.True(result.Completed);
        Assert.Equal("CAP-OLD", result.CaptureId);
        Assert.Equal(0, w.PayPal.CaptureCalls);
    }

    [Fact]
    public async Task CaptureByOrder_UnknownOrder_ThrowsKeyNotFound()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => w.Checkout.CaptureByOrderAsync("NO-SUCH-ORDER"));
    }

    // ------------------------------------------- CheckoutController walks

    private sealed class StubTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) =>
            new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }

    private static CheckoutController NewCheckoutController(World w, string student)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = $"solvia_student={student}";
        var controller = new CheckoutController(
            w.Db, w.Checkout,
            Options.Create(new PayPalOptions { ClientId = "test-client-id" }),
            NullLogger<CheckoutController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
        controller.TempData = new TempDataDictionary(context, new StubTempDataProvider());
        return controller;
    }

    [Fact]
    public async Task Checkout_UnknownId_404()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var controller = NewCheckoutController(w, "maria");

        var result = await controller.Index(Guid.NewGuid(), default);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Checkout_OtherPersonsApplication_404()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var app = AddApplication(w.Db, studentId: "maria");
        var controller = NewCheckoutController(w, "diego");

        var result = await controller.Index(app.Id, default);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Checkout_Paid_RedirectsToMyPermits_NeverRenders()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var app = AddApplication(w.Db, payment: PaymentStatus.Paid, orderId: "ORDER-1");
        var controller = NewCheckoutController(w, "maria");

        var result = await controller.Index(app.Id, default);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/Permits/My", redirect.Url);
    }

    [Fact]
    public async Task Checkout_ZeroPrice_RedirectsToMyPermits_NeverRenders()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var app = AddApplication(
            w.Db, priceCents: 0, payment: PaymentStatus.NotRequired);
        var controller = NewCheckoutController(w, "maria");

        var result = await controller.Index(app.Id, default);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/Permits/My", redirect.Url);
    }

    [Fact]
    public async Task Checkout_PayablePending_RendersCheckoutPage()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var app = AddApplication(w.Db);
        var controller = NewCheckoutController(w, "maria");

        var result = await controller.Index(app.Id, default);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("~/Views/Permits/Checkout.cshtml", view.ViewName);
        var model = Assert.IsType<PermitApplication>(view.Model);
        Assert.Equal(app.Id, model.Id);
        Assert.Equal("Fresno City College", model.Campus);
        Assert.Equal(UserCategory.Student, model.Category);
    }

    [Fact]
    public async Task Return_BogusToken_RendersNoSuccessState()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var controller = NewCheckoutController(w, "maria");

        var result = await controller.Return("totally-bogus-token", default);

        // Plumbing, not a page (item 60): a redirect home with an
        // error banner. No view, no success message, and nothing in
        // the database moved.
        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/", redirect.Url);
        Assert.False(controller.TempData.ContainsKey("StatusMessage"));
        Assert.True(controller.TempData.ContainsKey("ErrorMessage"));
        Assert.Equal(0, w.PayPal.CaptureCalls);
        Assert.False(await RedoTestDb.NewContext(w.Connection)
            .PermitApplications.AnyAsync(
                a => a.PaymentStatus == PaymentStatus.Paid));
    }

    [Fact]
    public async Task Return_KnownOrder_Captures_AndRedirectsToDone()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var app = AddApplication(w.Db, orderId: "ORDER-1");
        var controller = NewCheckoutController(w, "maria");

        var result = await controller.Return("ORDER-1", default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Done", redirect.ActionName);
        Assert.Equal(PaymentStatus.Paid, (await Reload(w, app.Id)).PaymentStatus);
    }

    [Fact]
    public async Task Return_LandingAlone_NeverMarksPaid_WhenCaptureSaysNo()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        w.PayPal.CaptureResult = new PayPalCaptureResult("PENDING", null);
        var app = AddApplication(w.Db, orderId: "ORDER-1");
        var controller = NewCheckoutController(w, "maria");

        var result = await controller.Return("ORDER-1", default);

        // Back to checkout with an error — NOT to Done, NOT Paid.
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.True(controller.TempData.ContainsKey("CheckoutError"));
        Assert.Equal(PaymentStatus.Failed, (await Reload(w, app.Id)).PaymentStatus);
    }

    [Fact]
    public async Task Cancel_KnownOrder_RedirectsToCheckout_WithInfoBanner()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var app = AddApplication(w.Db, orderId: "ORDER-1");
        var controller = NewCheckoutController(w, "maria");

        var result = await controller.Cancel("ORDER-1", default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Contains("nothing was charged",
            controller.TempData["CheckoutInfo"] as string);
        Assert.Equal(PaymentStatus.Pending, (await Reload(w, app.Id)).PaymentStatus);
    }

    [Fact]
    public async Task Done_Paid_RendersReceiptPage()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var app = AddApplication(
            w.Db, payment: PaymentStatus.Paid, orderId: "ORDER-1");
        var controller = NewCheckoutController(w, "maria");

        var result = await controller.Done(app.Id, default);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal(app.Id, Assert.IsType<PermitApplication>(view.Model).Id);
    }

    // -------------------------------------------- PayPalController walks

    private static PayPalController NewPayPalController(World w, string student)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = $"solvia_student={student}";
        return new PayPalController(
            w.Db, w.Checkout, NullLogger<PayPalController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
    }

    [Fact]
    public async Task Api_CreateOrder_UnknownApplication_404()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var controller = NewPayPalController(w, "maria");

        var result = await controller.CreateOrder(
            new PayPalController.CreateOrderRequest(Guid.NewGuid()), default);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Api_CreateOrder_WrongOwner_403()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var app = AddApplication(w.Db, studentId: "maria");
        var controller = NewPayPalController(w, "diego");

        var result = await controller.CreateOrder(
            new PayPalController.CreateOrderRequest(app.Id), default);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, status.StatusCode);
        Assert.Equal(0, w.PayPal.CreateCalls);
    }

    [Fact]
    public async Task Api_CreateOrder_PayPalNotConfigured_503()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        w.PayPal.ThrowNotConfigured = true;
        var app = AddApplication(w.Db);
        var controller = NewPayPalController(w, "maria");

        var result = await controller.CreateOrder(
            new PayPalController.CreateOrderRequest(app.Id), default);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, status.StatusCode);
    }

    [Fact]
    public async Task Api_CreateOrder_HappyPath_ReturnsStoredOrderId()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var app = AddApplication(w.Db);
        var controller = NewPayPalController(w, "maria");

        var result = await controller.CreateOrder(
            new PayPalController.CreateOrderRequest(app.Id), default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("\"id\":\"ORDER-1\"", json);
        Assert.Equal(1, w.PayPal.CreateCalls);
    }

    [Fact]
    public async Task Api_Capture_UnknownOrder_404()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var controller = NewPayPalController(w, "maria");

        var result = await controller.Capture(
            new PayPalController.CaptureRequest("NO-SUCH-ORDER"), default);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Api_Capture_WrongOwner_403_NoCaptureCall()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        AddApplication(w.Db, studentId: "maria", orderId: "ORDER-1");
        var controller = NewPayPalController(w, "diego");

        var result = await controller.Capture(
            new PayPalController.CaptureRequest("ORDER-1"), default);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, status.StatusCode);
        Assert.Equal(0, w.PayPal.CaptureCalls);
    }

    [Fact]
    public async Task Api_Capture_Completed_ReportsPaidTruth()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var app = AddApplication(w.Db, orderId: "ORDER-1");
        var controller = NewPayPalController(w, "maria");

        var result = await controller.Capture(
            new PayPalController.CaptureRequest("ORDER-1"), default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("\"completed\":true", json);
        Assert.Contains("\"captureId\":\"CAP-1\"", json);
        Assert.Equal(PaymentStatus.Paid, (await Reload(w, app.Id)).PaymentStatus);
    }
}
