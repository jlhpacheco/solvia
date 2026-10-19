using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Solvia.Config;
using Solvia.Controllers;
using Solvia.Data;
using Solvia.Models;
using Solvia.Models.Enums;
using Solvia.Models.ViewModels;
using Solvia.Services;

namespace Solvia.Tests;

/// <summary>
/// T5 tests — the college roster ("who paid / who hasn't") and the
/// public /verify page. All roster states are produced through the REAL
/// flows: an application row shaped exactly like PermitsController
/// creates it (price snapshotted from the real fees.csv), then
/// CheckoutService with a stub IPayPalService (the same seam T4's tests
/// use — a COMPLETED capture is what marks Paid; a $0 permit confirms
/// free with no PayPal call at all). Nothing here inserts a fake "Paid"
/// row by hand.
///
/// - Maria pays at Fresno City College ($30)  → PAID
/// - Priya's campus (YCCD) is FREE ($0)        → FREE — ISSUED
/// - Diego starts at Fresno, never captures   → PENDING
/// - Sam never applies                        → NO PERMIT
/// </summary>
public sealed class RosterVerifyTests : IDisposable
{
    private const string Fresno = "Fresno City College";
    private const string Yccd = "Modesto Junior College / Columbia College / YCCD";

    // The demo Fall 2026 window (matches TermDatesService).
    private static readonly DateOnly FallStart = new(2026, 8, 24);
    private static readonly DateOnly FallEnd = new(2026, 12, 11);
    private static readonly DateOnly TodayInFall = new(2026, 10, 18);

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<SolviaDbContext> _options;
    private readonly FeeCatalogService _catalog = new(FindFeesCsv());

    public RosterVerifyTests()
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

    private SolviaDbContext NewDb() => new(_options);

    // ------------------------------------------------------------------
    // PermitValidity — the contract (BUILD-PLAN §2)
    // ------------------------------------------------------------------

    private static PermitApplication BareApp(
        PaymentStatus payment,
        ApplicationStatus status = ApplicationStatus.Pending,
        DateOnly? start = null,
        DateOnly? end = null) => new()
        {
            Id = Guid.NewGuid(),
            StudentId = "maria",
            Campus = Fresno,
            VehicleId = Guid.NewGuid(),
            PermitTypeId = 2,
            StartDate = start ?? FallStart,
            EndDate = end ?? FallEnd,
            Status = status,
            PriceCents = 3000,
            PaymentStatus = payment,
            SubmittedAtUtc = DateTime.UtcNow,
        };

    [Fact]
    public void Validity_PaidInsideWindow_IsActive()
    {
        var app = BareApp(PaymentStatus.Paid);
        Assert.True(PermitValidity.IsActive(app, TodayInFall));
    }

    [Fact]
    public void Validity_FreeNotRequiredInsideWindow_IsActive()
    {
        var app = BareApp(PaymentStatus.NotRequired);
        Assert.True(PermitValidity.IsActive(app, TodayInFall));
    }

    [Fact]
    public void Validity_FirstAndLastDay_AreActive()
    {
        var app = BareApp(PaymentStatus.Paid);
        Assert.True(PermitValidity.IsActive(app, FallStart));
        Assert.True(PermitValidity.IsActive(app, FallEnd));
    }

    [Fact]
    public void Validity_BeforeStart_NotActiveWithReason()
    {
        var app = BareApp(PaymentStatus.Paid);
        var check = PermitValidity.Check(app, FallStart.AddDays(-1));
        Assert.False(check.IsActive);
        Assert.Contains("not valid yet", check.Reason);
    }

    [Fact]
    public void Validity_AfterEnd_NotActiveWithReason()
    {
        var app = BareApp(PaymentStatus.Paid);
        var check = PermitValidity.Check(app, FallEnd.AddDays(1));
        Assert.False(check.IsActive);
        Assert.Contains("ended on", check.Reason);
    }

    [Fact]
    public void Validity_DeniedPaid_NotActive()
    {
        var app = BareApp(PaymentStatus.Paid, ApplicationStatus.Denied);
        var check = PermitValidity.Check(app, TodayInFall);
        Assert.False(check.IsActive);
        Assert.Contains("denied", check.Reason);
    }

    [Fact]
    public void Validity_Unpaid_NotActive()
    {
        var app = BareApp(PaymentStatus.Pending);
        var check = PermitValidity.Check(app, TodayInFall);
        Assert.False(check.IsActive);
        Assert.Contains("not been paid", check.Reason);
    }

    [Fact]
    public void Validity_Refunded_NotActive()
    {
        var app = BareApp(PaymentStatus.Refunded);
        var check = PermitValidity.Check(app, TodayInFall);
        Assert.False(check.IsActive);
        Assert.Contains("refunded", check.Reason);
    }

    // ------------------------------------------------------------------
    // The real flows → roster states
    // ------------------------------------------------------------------

    /// <summary>
    /// Creates a vehicle + application exactly the way PermitsController
    /// does: server-set student id, canonical campus name, and the price
    /// snapshotted from the college's real fees.csv row.
    /// </summary>
    private static async Task<PermitApplication> ApplyAsync(
        SolviaDbContext db, IFeeCatalog catalog,
        string studentId, string campusName, string plate)
    {
        var fee = catalog.Find(campusName)
            ?? throw new InvalidOperationException($"Campus '{campusName}' not in catalog.");
        var price = catalog.SemesterPriceCents(fee, TermKind.Fall)
            ?? throw new InvalidOperationException($"Campus '{campusName}' publishes no Fall price.");

        var vehicle = new Vehicle
        {
            Id = Guid.NewGuid(),
            StudentId = studentId,
            Plate = plate,
            State = UsState.CA,
            Color = VehicleColor.Blue,
            Make = "Toyota",
            Model = "Corolla",
        };
        db.Vehicles.Add(vehicle);

        var application = new PermitApplication
        {
            Id = Guid.NewGuid(),
            StudentId = studentId,
            Campus = fee.Campus,
            VehicleId = vehicle.Id,
            PermitTypeId = 2, // seeded Term type
            StartDate = FallStart,
            EndDate = FallEnd,
            Status = ApplicationStatus.Pending,
            PriceCents = price,
            PaymentStatus = PaymentStatus.Pending,
            SubmittedAtUtc = DateTime.UtcNow,
        };
        db.PermitApplications.Add(application);
        await db.SaveChangesAsync();
        return application;
    }

    private static CheckoutService NewCheckout(
        SolviaDbContext db, IPayPalService payPal) =>
        new(db, payPal, new PermitNumberService(db), NullLogger<CheckoutService>.Instance);

    /// <summary>Runs the full seeded cast through their flows on one context.</summary>
    private async Task<StubPayPalService> RunCastFlowsAsync(SolviaDbContext db)
    {
        var payPal = new StubPayPalService();
        var checkout = NewCheckout(db, payPal);

        // Maria — Fresno, $30: create order, then a COMPLETED capture.
        var maria = await ApplyAsync(db, _catalog, "maria", Fresno, "8MAR100");
        await checkout.StartAsync(maria.Id, null, null);
        var capture = await checkout.CaptureAsync(maria.Id);
        Assert.True(capture.Completed);

        // Diego — Fresno: order created, capture never happens.
        var diego = await ApplyAsync(db, _catalog, "diego", Fresno, "8DIE200");
        await checkout.StartAsync(diego.Id, null, null);

        // Priya — YCCD is FREE: StartAsync confirms it, no PayPal call.
        var priya = await ApplyAsync(db, _catalog, "priya", Yccd, "8PRI300");
        var free = await checkout.StartAsync(priya.Id, null, null);
        Assert.Equal(CheckoutStartKind.FreePermitConfirmed, free.Kind);

        // Sam — no application anywhere.
        return payPal;
    }

    [Fact]
    public async Task Flows_ProduceTheFourRosterStates()
    {
        using var db = NewDb();
        var payPal = await RunCastFlowsAsync(db);

        var fresnoApps = await db.PermitApplications
            .Where(a => a.Campus == Fresno).ToListAsync();
        var yccdApps = await db.PermitApplications
            .Where(a => a.Campus == Yccd).ToListAsync();

        PermitApplication For(string studentId, List<PermitApplication> apps) =>
            apps.Single(a => a.StudentId == studentId);

        // Maria → PAID, with the permit number + token issued by the flow.
        var (mariaState, mariaPermit) = PermitValidity.Classify(
            [For("maria", fresnoApps)], TodayInFall);
        Assert.Equal(RosterPermitState.Paid, mariaState);
        Assert.NotNull(mariaPermit);
        Assert.False(string.IsNullOrWhiteSpace(mariaPermit!.ReceiptNumber));
        Assert.False(string.IsNullOrWhiteSpace(mariaPermit.VerifyToken));
        Assert.Equal(PaymentStatus.Paid, mariaPermit.PaymentStatus);
        Assert.NotNull(mariaPermit.PaidAtUtc);
        Assert.False(string.IsNullOrWhiteSpace(mariaPermit.PayPalCaptureId));

        // Diego → PENDING (order exists, money never landed, no number).
        var (diegoState, diegoPermit) = PermitValidity.Classify(
            [For("diego", fresnoApps)], TodayInFall);
        Assert.Equal(RosterPermitState.Pending, diegoState);
        Assert.Null(diegoPermit);
        Assert.False(string.IsNullOrWhiteSpace(For("diego", fresnoApps).PayPalOrderId));
        Assert.Null(For("diego", fresnoApps).ReceiptNumber);

        // Priya at YCCD → FREE — ISSUED, and PayPal was never asked for $0.
        var (priyaState, priyaPermit) = PermitValidity.Classify(
            [For("priya", yccdApps)], TodayInFall);
        Assert.Equal(RosterPermitState.FreeIssued, priyaState);
        Assert.NotNull(priyaPermit);
        Assert.Equal(PaymentStatus.NotRequired, priyaPermit!.PaymentStatus);
        Assert.False(string.IsNullOrWhiteSpace(priyaPermit.ReceiptNumber));
        Assert.Equal(2, payPal.CreateOrderCalls); // Maria + Diego only.

        // Priya at Fresno (she never applied there) → NO PERMIT.
        var (priyaAtFresno, _) = PermitValidity.Classify(
            fresnoApps.Where(a => a.StudentId == "priya").ToList(), TodayInFall);
        Assert.Equal(RosterPermitState.NoPermit, priyaAtFresno);

        // Sam anywhere → NO PERMIT.
        var (samState, samPermit) = PermitValidity.Classify([], TodayInFall);
        Assert.Equal(RosterPermitState.NoPermit, samState);
        Assert.Null(samPermit);
    }

    // ------------------------------------------------------------------
    // Controllers — roster rows + verify pages
    // ------------------------------------------------------------------

    private CollegeController NewCollegeController(SolviaDbContext db) => new(
        _catalog,
        new StubTermDatesService(),
        Options.Create(new FeeTableOptions()),
        Options.Create(new CalendarOptions()),
        db);

    [Fact]
    public async Task Roster_FresnoPage_ListsEveryStudentWithTruthfulStates()
    {
        using var db = NewDb();
        await RunCastFlowsAsync(db);
        var controller = NewCollegeController(db);

        var result = await controller.Roster(Fresno);
        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<RosterViewModel>(view.Model);

        Assert.Equal(Fresno, model.Campus);
        Assert.Equal(4, model.Rows.Count); // every seeded student
        Assert.Equal(1, model.WithPermitCount);
        Assert.Equal(3, model.WithoutPermitCount);

        var maria = model.Rows.Single(r => r.StudentName == "Maria");
        Assert.Equal(RosterPermitState.Paid, maria.State);
        Assert.Equal("PAID", maria.StateLabel);
        Assert.False(string.IsNullOrWhiteSpace(maria.PermitNumber));
        Assert.Equal(FallStart, maria.StartDate);
        Assert.Equal(FallEnd, maria.EndDate);

        var diego = model.Rows.Single(r => r.StudentName == "Diego");
        Assert.Equal(RosterPermitState.Pending, diego.State);
        Assert.Null(diego.PermitNumber);

        Assert.Equal(RosterPermitState.NoPermit,
            model.Rows.Single(r => r.StudentName == "Priya").State);
        Assert.Equal(RosterPermitState.NoPermit,
            model.Rows.Single(r => r.StudentName == "Sam").State);
    }

    [Fact]
    public async Task Roster_YccdPage_ShowsPriyaFreeIssued()
    {
        using var db = NewDb();
        await RunCastFlowsAsync(db);
        var controller = NewCollegeController(db);

        var result = await controller.Roster(Yccd);
        var model = Assert.IsType<RosterViewModel>(
            Assert.IsType<ViewResult>(result).Model);

        var priya = model.Rows.Single(r => r.StudentName == "Priya");
        Assert.Equal(RosterPermitState.FreeIssued, priya.State);
        Assert.Equal("FREE — ISSUED", priya.StateLabel);
        Assert.False(string.IsNullOrWhiteSpace(priya.PermitNumber));
        Assert.Equal(1, model.WithPermitCount);
    }

    [Fact]
    public async Task Roster_UnknownCampus_IsNotFound()
    {
        using var db = NewDb();
        var controller = NewCollegeController(db);
        var result = await controller.Roster("Nowhere University");
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Verify_MariasRealToken_ShowsValidWithPermitFacts()
    {
        using var db = NewDb();
        await RunCastFlowsAsync(db);
        var maria = await db.PermitApplications.SingleAsync(a => a.StudentId == "maria");
        var controller = new VerifyController(db);

        var result = await controller.Index(maria.VerifyToken);
        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Verify", view.ViewName);
        var model = Assert.IsType<VerifyViewModel>(view.Model);
        Assert.True(model.IsValid);
        Assert.Equal(maria.ReceiptNumber, model.PermitNumber);
        Assert.Equal(Fresno, model.Campus);
        Assert.Equal("Maria", model.StudentName);
        Assert.Equal(FallStart, model.StartDate);
        Assert.Equal(FallEnd, model.EndDate);
    }

    [Fact]
    public async Task Verify_MadeUpToken_GetsNeutralNotFoundPage()
    {
        using var db = NewDb();
        await RunCastFlowsAsync(db);
        var controller = new VerifyController(db);

        var result = await controller.Index("this-token-was-made-up");
        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Invalid", view.ViewName);
        var model = Assert.IsType<VerifyViewModel>(view.Model);
        Assert.False(model.IsValid);
        Assert.Contains("could not find", model.Reason);
        // Nothing about anyone's real permit leaks onto the page.
        Assert.Null(model.PermitNumber);
        Assert.Null(model.Campus);
        Assert.Null(model.StudentName);
    }

    [Fact]
    public async Task Verify_MissingToken_GetsSameNeutralPage()
    {
        using var db = NewDb();
        var controller = new VerifyController(db);

        foreach (var token in new string?[] { null, "", "   " })
        {
            var result = await controller.Index(token);
            var view = Assert.IsType<ViewResult>(result);
            Assert.Equal("Invalid", view.ViewName);
            var model = Assert.IsType<VerifyViewModel>(view.Model);
            Assert.Contains("could not find", model.Reason);
        }
    }

    [Fact]
    public async Task Verify_ExpiredPermitToken_ShowsNotValidWithPlainReason()
    {
        using var db = NewDb();
        await RunCastFlowsAsync(db);

        // Priya's free permit was really issued (token + number exist);
        // move its window into last Spring — time passing is what makes
        // a once-valid permit stop being valid.
        var priya = await db.PermitApplications.SingleAsync(a => a.StudentId == "priya");
        priya.StartDate = new DateOnly(2026, 1, 20);
        priya.EndDate = new DateOnly(2026, 5, 15);
        await db.SaveChangesAsync();

        var controller = new VerifyController(db);
        var result = await controller.Index(priya.VerifyToken);
        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Invalid", view.ViewName);
        var model = Assert.IsType<VerifyViewModel>(view.Model);
        Assert.False(model.IsValid);
        Assert.Contains("ended on", model.Reason);
        Assert.Equal(priya.ReceiptNumber, model.PermitNumber); // her own number, nobody else's
    }

    [Fact]
    public async Task Verify_UnpaidApplicationHasNoTokenToFind()
    {
        using var db = NewDb();
        await RunCastFlowsAsync(db);
        var diego = await db.PermitApplications.SingleAsync(a => a.StudentId == "diego");
        // Tokens are issued only when a permit goes live — Diego's
        // pending application never got one.
        Assert.Null(diego.VerifyToken);
        Assert.Null(diego.ReceiptNumber);
    }

    // ------------------------------------------------------------------
    // File-backed seed for the live curl checks (the T5 done-test).
    // Produces /tmp/solvia-t5-verify.db through the same real flows as
    // above; the shell step then runs the actual app against this file
    // and curls /College/Roster and /verify. Self-contained: the file
    // is recreated from scratch on every run.
    // ------------------------------------------------------------------

    public const string CurlSeedDbPath = "/tmp/solvia-t5-verify.db";

    [Fact]
    public async Task SeedFileDatabase_ForCurlVerification()
    {
        if (File.Exists(CurlSeedDbPath))
        {
            File.Delete(CurlSeedDbPath);
        }

        var options = new DbContextOptionsBuilder<SolviaDbContext>()
            .UseSqlite($"Data Source={CurlSeedDbPath}")
            .Options;

        using var db = new SolviaDbContext(options);
        db.Database.EnsureCreated();
        await RunCastFlowsAsync(db);

        var maria = await db.PermitApplications.SingleAsync(a => a.StudentId == "maria");
        Assert.Equal(PaymentStatus.Paid, maria.PaymentStatus);
        Assert.False(string.IsNullOrWhiteSpace(maria.VerifyToken));
        Assert.False(string.IsNullOrWhiteSpace(maria.ReceiptNumber));
    }

    // ------------------------------------------------------------------
    // Test doubles + helpers
    // ------------------------------------------------------------------

    /// <summary>Plays the PayPal sandbox for CheckoutService: orders are
    /// created, and captures come back COMPLETED with a capture id —
    /// the only status that may mark a permit Paid.</summary>
    private sealed class StubPayPalService : IPayPalService
    {
        public int CreateOrderCalls;

        public Task<string> GetAccessTokenAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult("stub-access-token");

        public Task<PayPalOrderResult> CreateOrderAsync(
            int amountCents,
            string reference,
            string? returnUrl = null,
            string? cancelUrl = null,
            CancellationToken cancellationToken = default)
        {
            CreateOrderCalls++;
            return Task.FromResult(new PayPalOrderResult(
                "ORDER-T5-STUB", "CREATED",
                "https://www.sandbox.paypal.com/checkoutnow?token=ORDER-T5-STUB"));
        }

        public Task<PayPalCaptureResult> CaptureOrderAsync(
            string orderId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PayPalCaptureResult("COMPLETED", "CAPTURE-T5-STUB"));
    }

    private sealed class StubTermDatesService : ITermDatesService
    {
        public TermDates? GetTermDates(string campusName, TermKind term, int year) => null;
    }

    private static string FindFeesCsv()
    {
        var starts = new[]
        {
            Path.GetDirectoryName(typeof(RosterVerifyTests).Assembly.Location),
            AppContext.BaseDirectory,
        };

        foreach (var start in starts)
        {
            var dir = start is null ? null : new DirectoryInfo(start);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "data", "fees.csv");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }
        }

        throw new FileNotFoundException(
            "Could not locate data/fees.csv above the test assembly.");
    }
}
