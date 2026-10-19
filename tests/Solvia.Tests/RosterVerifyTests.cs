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
using Solvia.Models.ViewModels;
using Solvia.Services;

namespace Solvia.Tests;

/// <summary>
/// R8 roster tests (REDO-PLAN items 53–54): the Fresno scenario is
/// produced ENTIRELY through the real services — applications via
/// ApplicationService, Maria's payment via CheckoutService over a
/// stub IPayPalService (COMPLETED capture), Priya's free permit via
/// the $0 campus settling at creation — never hand-inserted rows
/// (the Lyceum rule). The controller is then asked for the roster
/// and its tiles / By-student section must match that scenario
/// exactly. The fee table's two renderers are proven to carry
/// identical values at the view-model level (display strings are
/// computed once, on the server).
/// </summary>
public sealed class RosterVerifyTests
{
    private const string Fresno = "Fresno City College";
    private const string Yccd = "Modesto Junior College / Columbia College / YCCD";

    // ------------------------------------------------------- harness

    private sealed class StubPayPal : IPayPalService
    {
        private int _amountCents;
        public Task<string> GetAccessTokenAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult("test-token");

        public Task<PayPalOrderResult> CreateOrderAsync(
            int amountCents, string reference, string? returnUrl = null,
            string? cancelUrl = null, CancellationToken cancellationToken = default)
        {
            _amountCents = amountCents;
            return Task.FromResult(new PayPalOrderResult(
                "ORDER-1", "CREATED",
                "https://www.sandbox.paypal.com/checkoutnow?token=ORDER-1"));
        }

        public Task<PayPalCaptureResult> CaptureOrderAsync(
            string orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PayPalCaptureResult("COMPLETED", "CAP-1", _amountCents, "USD"));

        public Task<bool> VerifyWebhookSignatureAsync(
            string transmissionId, string transmissionTime, string transmissionSig,
            string certUrl, string authAlgo, string webhookEventJson,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class StubTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) =>
            new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }

    private sealed record World(
        SolviaDbContext Db,
        SqliteConnection Connection,
        IFeeCatalog Catalog,
        ApplicationService Applications,
        CheckoutService Checkout,
        PriceRepairService Repair,
        ITermDatesService TermDates);

    private static World NewWorld()
    {
        var (db, connection) = RedoTestDb.Create();
        var fees = new FeeCatalogService(RedoTestDb.FindFeesCsv());
        var issuance = new IssuanceService(new PermitNumberService(db));
        var applications = new ApplicationService(
            db, fees, new PricingService(fees),
            new UserCategoryResolver(Options.Create(new CategoryOptions())),
            new TermDatesService(fees, db), issuance);
        var checkout = new CheckoutService(
            db, new StubPayPal(), issuance, fees, new PricingService(fees),
            NullLogger<CheckoutService>.Instance);
        var repair = new PriceRepairService(db, fees, new PricingService(fees));
        return new World(db, connection, fees, applications, checkout, repair,
            new TermDatesService(fees, db));
    }

    private static CollegeController NewController(
        World w, string renderer = "native", ICollegeSearch? search = null)
    {
        var context = new DefaultHttpContext();
        var controller = new CollegeController(
            w.Catalog, w.TermDates,
            search ?? new BuiltinCollegeSearch(w.Catalog),
            w.Repair,
            Options.Create(new FeeTableOptions { Renderer = renderer }),
            Options.Create(new CalendarOptions()),
            w.Db)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
        controller.TempData = new TempDataDictionary(context, new StubTempDataProvider());
        return controller;
    }

    private static Vehicle AddVehicle(SolviaDbContext db, string studentId, string plate)
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

    private static async Task<PermitApplication> ApplyTermAsync(
        World w, string studentId, string campus, string plate)
    {
        var vehicle = AddVehicle(w.Db, studentId, plate);
        var start = w.TermDates.GetTermDates(campus, TermKind.Fall, 2026)!.TermStart;
        var result = await w.Applications.CreateAsync(new ApplicationInput(
            studentId, vehicle.Id, campus, PermitTypeCode.Term,
            TermKind.Fall, start, "form"));
        Assert.Equal(CreateApplicationStatus.Created, result.Status);
        return result.Application!;
    }

    /// <summary>
    /// The done-test scenario, produced through the real services:
    /// Maria Paid (Fresno), Diego Pending (Fresno), Priya Free-Issued
    /// (YCCD, a $0 campus), Sam nothing anywhere.
    /// </summary>
    private static async Task<(PermitApplication Maria, PermitApplication Diego, PermitApplication Priya)>
        BuildScenarioAsync(World w)
    {
        var maria = await ApplyTermAsync(w, "maria", Fresno, "8XYZ123");
        var start = await w.Checkout.StartAsync(maria.Id, null, null);
        Assert.Equal(CheckoutStartKind.OrderReady, start.Kind);
        var capture = await w.Checkout.CaptureAsync(maria.Id);
        Assert.True(capture.Completed);

        var diego = await ApplyTermAsync(w, "diego", Fresno, "7DGO456");
        var priya = await ApplyTermAsync(w, "priya", Yccd, "9PRY789");
        return (maria, diego, priya);
    }

    private static async Task<RosterViewModel> RosterAsync(World w, string campus)
    {
        var controller = NewController(w);
        var result = await controller.Roster(campus, default);
        var view = Assert.IsType<ViewResult>(result);
        return Assert.IsType<RosterViewModel>(view.Model);
    }

    // ------------------------------------------------------------ roster

    [Fact]
    public async Task FresnoRoster_StatesAndTiles_MatchFlowProducedScenario()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var (maria, _, _) = await BuildScenarioAsync(w);

        var model = await RosterAsync(w, Fresno);

        Assert.Equal(Fresno, model.Campus);
        Assert.Equal(6, model.Rows.Count);
        var byName = model.Rows.ToDictionary(r => r.StudentName);

        // Maria: PAID, with her real issued permit number + dates.
        Assert.Equal(RosterPermitState.Paid, byName["Maria"].State);
        Assert.Equal("PAID", byName["Maria"].StateLabel);
        Assert.Equal(maria.ReceiptNumber, byName["Maria"].PermitNumber);
        Assert.NotNull(byName["Maria"].PermitNumber);

        // Diego started but never paid; Priya's permit is at YCCD,
        // not Fresno; Sam never applied. Nobody's data is invented.
        Assert.Equal(RosterPermitState.Pending, byName["Diego"].State);
        Assert.Null(byName["Diego"].PermitNumber);
        Assert.Equal(RosterPermitState.NoPermit, byName["Priya"].State);
        Assert.Equal(RosterPermitState.NoPermit, byName["Sam"].State);

        // The three admin tiles, exactly the scenario's numbers:
        // 1 issued (Maria), $30.00 revenue (her Paid snapshot),
        // nothing expired (her Fall permit ends in December).
        Assert.Equal(1, model.IssuedCount);
        Assert.Equal(3000, model.RevenueCents);
        Assert.Equal(0, model.ExpiredCount);
    }

    [Fact]
    public async Task FresnoRoster_ByStudent_SortedByPermitCount_WithReceiptNumbers()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var (maria, _, _) = await BuildScenarioAsync(w);

        var model = await RosterAsync(w, Fresno);

        Assert.Equal(6, model.ByStudent.Count);
        // Maria first (the only Fresno permit holder), count 1, her
        // issued receipt number listed; everyone else counts 0.
        Assert.Equal("Maria", model.ByStudent[0].StudentName);
        Assert.Equal(1, model.ByStudent[0].PermitCount);
        Assert.Equal(
            new[] { maria.ReceiptNumber! },
            model.ByStudent[0].ReceiptNumbers);
        Assert.All(model.ByStudent.Skip(1),
            s => Assert.Equal(0, s.PermitCount));
        // Diego's PENDING application is not a permit: no count,
        // no number — even though he applied at this campus.
        var diego = model.ByStudent.Single(s => s.StudentName == "Diego");
        Assert.Equal(0, diego.PermitCount);
        Assert.Empty(diego.ReceiptNumbers);
    }

    [Fact]
    public async Task YccdRoster_PriyaFreeIssued_RevenueZero()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var (_, _, priya) = await BuildScenarioAsync(w);

        var model = await RosterAsync(w, Yccd);

        var priyaRow = model.Rows.Single(r => r.StudentName == "Priya");
        Assert.Equal(RosterPermitState.FreeIssued, priyaRow.State);
        Assert.Equal("FREE — ISSUED", priyaRow.StateLabel);
        Assert.Equal(priya.ReceiptNumber, priyaRow.PermitNumber);

        // Issued tile counts her; revenue is $0 — free is real, and
        // it is not money.
        Assert.Equal(1, model.IssuedCount);
        Assert.Equal(0, model.RevenueCents);
        Assert.Equal(0, model.ExpiredCount);
        Assert.Equal("Priya", model.ByStudent[0].StudentName);
        Assert.Equal(1, model.ByStudent[0].PermitCount);
    }

    [Fact]
    public async Task ExpiredPaidPermit_CountsInExpiredTile_StillPaidOnRoster()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;

        // A Daily permit that started (and so ended) yesterday,
        // bought and paid through the real services.
        var vehicle = AddVehicle(w.Db, "maria", "8XYZ123");
        var yesterday = DateOnly.FromDateTime(DateTime.Today).AddDays(-1);
        var created = await w.Applications.CreateAsync(new ApplicationInput(
            "maria", vehicle.Id, Fresno, PermitTypeCode.Daily,
            TermKind.Fall, yesterday, "form"));
        Assert.Equal(CreateApplicationStatus.Created, created.Status);
        var app = created.Application!;
        Assert.Equal(100, app.PriceCents); // Fresno's published $1 day pass
        await w.Checkout.StartAsync(app.Id, null, null);
        var capture = await w.Checkout.CaptureAsync(app.Id);
        Assert.True(capture.Completed);

        var model = await RosterAsync(w, Fresno);

        Assert.Equal(1, model.IssuedCount);
        Assert.Equal(100, model.RevenueCents);
        Assert.Equal(1, model.ExpiredCount);
        // Roster truth (it WAS paid) vs verify truth (it is no
        // longer valid) — the split the Lyceum pins: both honest.
        Assert.Equal(RosterPermitState.Paid,
            model.Rows.Single(r => r.StudentName == "Maria").State);
        var saved = await w.Db.PermitApplications.SingleAsync(a => a.Id == app.Id);
        Assert.False(PermitValidity.IsActive(
            saved, DateOnly.FromDateTime(DateTime.Today)));
    }

    [Fact]
    public async Task Roster_UnknownCampus_404()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var controller = NewController(w);

        var result = await controller.Roster("Harvard", default);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // ---------------------------------------------------------- fee table

    [Fact]
    public void Fees_TwoRenderers_CarryIdenticalValues()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;

        var native = Assert.IsType<FeeTableViewModel>(
            Assert.IsType<ViewResult>(NewController(w, "native").Fees()).Model);
        var aggrid = Assert.IsType<FeeTableViewModel>(
            Assert.IsType<ViewResult>(NewController(w, "aggrid").Fees()).Model);

        Assert.Equal("native", native.Renderer);
        Assert.Equal("aggrid", aggrid.Renderer);
        Assert.Equal(10, native.Rows.Count);
        // The seam's whole promise: same rows, byte-identical values,
        // whichever renderer draws them.
        Assert.Equal(native.Rows, aggrid.Rows);

        var fresno = native.Rows.Single(r => r.Campus == Fresno);
        Assert.Equal("$30.00", fresno.SemesterPrice);
        Assert.Equal(3000, fresno.SemesterPriceCents);
        var yccd = native.Rows.Single(r => r.Campus == Yccd);
        Assert.Equal("$0 FREE", yccd.SemesterPrice);
        Assert.Equal(0, yccd.SemesterPriceCents);
    }

    [Fact]
    public void FeeTablePartial_HasBothRenderBranches_AndTheSharedJsonIsland()
    {
        // The partial is the single rendering point: it must carry
        // the AG Grid branch, the native table, and the one JSON
        // payload both client renderers consume.
        var partial = FindRepoFile("src/Solvia/Views/Shared/_FeeTable.cshtml");
        var text = File.ReadAllText(partial);
        Assert.Contains("aggrid", text);
        Assert.Contains("feeTableNative", text);
        Assert.Contains("fee-table-data", text);
        Assert.Contains("ag-grid-community@34.3.1", text);
        Assert.Contains("~/js/feetable.aggrid.js", text);
    }

    private static string FindRepoFile(string relative)
    {
        // Start from the TEST ASSEMBLY's folder: under the in-process
        // console runner, AppContext.BaseDirectory is the runner's
        // own folder (the Lyceum gotcha RedoTestDb also guards).
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(typeof(RosterVerifyTests).Assembly.Location)!);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate {relative} above the test assembly.");
    }
}
