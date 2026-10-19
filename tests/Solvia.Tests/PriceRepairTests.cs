using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
/// R8 price-repair tests (REDO-PLAN item 55): repair recomputes
/// PENDING prices from the fee catalog through PricingService and
/// reports every change; a Paid snapshot is historical truth and is
/// provably untouched even after being tampered with; a row whose
/// campus publishes no such price is skipped, untouched. Scenario
/// rows are produced through ApplicationService — the tamper itself
/// is a direct write, because tampering is the fault being repaired.
/// </summary>
public sealed class PriceRepairTests
{
    private const string Fresno = "Fresno City College";
    private const string Bakersfield = "CSU Bakersfield";

    // ------------------------------------------------------- harness

    private sealed record World(
        SolviaDbContext Db,
        SqliteConnection Connection,
        IFeeCatalog Catalog,
        ApplicationService Applications,
        PriceRepairService Repair);

    private static World NewWorld(IFeeCatalog? catalog = null)
    {
        var (db, connection) = RedoTestDb.Create();
        var fees = catalog ?? new FeeCatalogService(RedoTestDb.FindFeesCsv());
        var issuance = new IssuanceService(new PermitNumberService(db));
        var applications = new ApplicationService(
            db, fees, new PricingService(fees),
            new UserCategoryResolver(Options.Create(new CategoryOptions())),
            new TermDatesService(fees, db), issuance);
        var repair = new PriceRepairService(db, fees, new PricingService(fees));
        return new World(db, connection, fees, applications, repair);
    }

    /// <summary>A catalog with ONE synthetic campus, priced by the test's row.</summary>
    private static FeeCatalogService SyntheticCatalog(string row)
    {
        var header = File.ReadLines(RedoTestDb.FindFeesCsv()).First();
        var path = Path.Combine(
            Path.GetTempPath(), $"solvia-r8-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, header + "\n" + row + "\n");
        return new FeeCatalogService(path);
    }

    // Term published ($30); day pass NOT published.
    private const string DayPassUnpublishedRow =
        "Synthetic College,$30 Fall/Spring (cars),not listed on PMB page," +
        "not listed on PMB page,not listed on PMB page,not listed on PMB page," +
        "not listed on PMB page,synthetic R8 test row";

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

    private static async Task<PermitApplication> ApplyAsync(
        World w, string studentId, string campus,
        PermitTypeCode code = PermitTypeCode.Term,
        bool persistWhenPriceBlocked = false)
    {
        var vehicle = AddVehicle(w.Db, studentId, "8XYZ123");
        var result = await w.Applications.CreateAsync(new ApplicationInput(
            studentId, vehicle.Id, campus, code, TermKind.Fall,
            new DateOnly(2026, 8, 24), "form",
            PersistWhenPriceBlocked: persistWhenPriceBlocked));
        Assert.Equal(CreateApplicationStatus.Created, result.Status);
        return result.Application!;
    }

    /// <summary>The fault injection: a wrong price written straight to the row.</summary>
    private static async Task TamperPriceAsync(World w, Guid id, int wrongCents)
    {
        var db = RedoTestDb.NewContext(w.Connection);
        var app = await db.PermitApplications.SingleAsync(a => a.Id == id);
        app.PriceCents = wrongCents;
        app.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync();
    }

    private static async Task<int> PriceOfAsync(World w, Guid id) =>
        (await RedoTestDb.NewContext(w.Connection).PermitApplications
            .SingleAsync(a => a.Id == id)).PriceCents;

    /// <summary>
    /// A repair service over a FRESH context on the same database —
    /// the production lifetime (one scoped context per request).
    /// The World's long-lived context still tracks the pre-tamper
    /// entities, and EF identity resolution would serve those stale
    /// values to a repair running on it; the app never has that
    /// shape, so the tests don't either.
    /// </summary>
    private static PriceRepairService FreshRepair(World w) =>
        new(RedoTestDb.NewContext(w.Connection),
            w.Catalog, new PricingService(w.Catalog));

    // ------------------------------------------------------------ service

    [Fact]
    public async Task Repair_TamperedPendingPrice_ReturnsToCatalogValue_AndReports()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var diego = await ApplyAsync(w, "diego", Fresno);
        Assert.Equal(3000, diego.PriceCents);
        await TamperPriceAsync(w, diego.Id, 9999);

        var repair = FreshRepair(w);
        var report = await repair.RepairPendingAsync();

        var change = Assert.Single(report.Changes);
        Assert.Equal(diego.Id, change.ApplicationId);
        Assert.Equal(9999, change.OldPriceCents);
        Assert.Equal(3000, change.NewPriceCents);
        Assert.Equal(3000, await PriceOfAsync(w, diego.Id));

        // A second run finds nothing to do — the row now matches.
        var again = await repair.RepairPendingAsync();
        Assert.Empty(again.Changes);
        Assert.Equal(1, again.UnchangedCount);
    }

    [Fact]
    public async Task Repair_PaidSnapshot_IsHistoricalTruth_NeverRewritten()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var maria = await ApplyAsync(w, "maria", Fresno);

        // Maria's row settles Paid (the state CheckoutService lands
        // on a COMPLETED capture), and then the snapshot is tampered
        // with — repair must STILL leave it alone: Paid is history.
        var db = RedoTestDb.NewContext(w.Connection);
        var row = await db.PermitApplications.SingleAsync(a => a.Id == maria.Id);
        row.PaymentStatus = PaymentStatus.Paid;
        row.PriceCents = 1234;
        row.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync();

        var report = await FreshRepair(w).RepairPendingAsync();

        Assert.Empty(report.Changes);
        Assert.Equal(1234, await PriceOfAsync(w, maria.Id));
    }

    [Fact]
    public async Task Repair_UnpublishedPrice_Skipped_RowUntouched()
    {
        var catalog = SyntheticCatalog(DayPassUnpublishedRow);
        var w = NewWorld(catalog);
        using var _ = w.Connection;
        using var __ = w.Db;
        // The AI door's blocked draft: Daily at a campus that
        // publishes no day-pass price, parked at 0 / Pending.
        var draft = await ApplyAsync(
            w, "maria", "Synthetic College",
            PermitTypeCode.Daily, persistWhenPriceBlocked: true);
        Assert.Equal(0, draft.PriceCents);
        Assert.Equal(PaymentStatus.Pending, draft.PaymentStatus);

        var report = await FreshRepair(w).RepairPendingAsync();

        Assert.Empty(report.Changes);
        var skip = Assert.Single(report.Skipped);
        Assert.Equal(draft.Id, skip.ApplicationId);
        Assert.Equal(0, await PriceOfAsync(w, draft.Id));
    }

    [Fact]
    public async Task Repair_CampusFilter_TouchesOnlyThatCampus()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var diego = await ApplyAsync(w, "diego", Fresno);
        var sam = await ApplyAsync(w, "sam", Bakersfield);
        Assert.Equal(9300, sam.PriceCents);
        await TamperPriceAsync(w, diego.Id, 1);
        await TamperPriceAsync(w, sam.Id, 2);

        var report = await FreshRepair(w).RepairPendingAsync(Fresno);

        Assert.Single(report.Changes);
        Assert.Equal(3000, await PriceOfAsync(w, diego.Id));
        Assert.Equal(2, await PriceOfAsync(w, sam.Id)); // untouched

        var rest = await FreshRepair(w).RepairPendingAsync();
        Assert.Single(rest.Changes);
        Assert.Equal(9300, await PriceOfAsync(w, sam.Id));
    }

    // ---------------------------------------------------------- controller

    private sealed class StubTempDataProvider : ITempDataProvider
    {
        private readonly Dictionary<string, object> _saved = new();

        public IDictionary<string, object> LoadTempData(HttpContext context) =>
            new Dictionary<string, object>(_saved);

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
            _saved.Clear();
            foreach (var (key, value) in values)
            {
                _saved[key] = value;
            }
        }
    }

    [Fact]
    public async Task RepairPrices_Post_RedirectsToRoster_WithBannerReport()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var diego = await ApplyAsync(w, "diego", Fresno);
        await TamperPriceAsync(w, diego.Id, 9999);

        var tempData = new StubTempDataProvider();
        var context = new DefaultHttpContext();
        // Fresh request-scoped context, as in production (see
        // FreshRepair): the controller + repair share it.
        var requestDb = RedoTestDb.NewContext(w.Connection);
        var controller = new CollegeController(
            w.Catalog, new TermDatesService(w.Catalog, requestDb),
            new BuiltinCollegeSearch(w.Catalog),
            new PriceRepairService(requestDb, w.Catalog, new PricingService(w.Catalog)),
            Options.Create(new FeeTableOptions()),
            Options.Create(new CalendarOptions()),
            requestDb)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
        controller.TempData = new TempDataDictionary(context, tempData);

        var result = await controller.RepairPrices(Fresno, default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Roster", redirect.ActionName);
        Assert.Equal(3000, await PriceOfAsync(w, diego.Id));

        // The roster GET (with the same TempData store) surfaces the
        // banner list naming the repair.
        var roster = Assert.IsType<ViewResult>(
            await controller.Roster(Fresno, default));
        var model = Assert.IsType<RosterViewModel>(roster.Model);
        Assert.NotNull(model.RepairReport);
        Assert.Contains(model.RepairReport,
            line => line.Contains("1 price(s) repaired"));
        Assert.Contains(model.RepairReport,
            line => line.Contains("$99.99") && line.Contains("$30.00"));
    }
}
