using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Solvia.Config;
using Solvia.Data;
using Solvia.Models;
using Solvia.Models.Enums;
using Solvia.Services;

namespace Solvia.Tests;

/// <summary>
/// R4 application-core tests (REDO-PLAN §2c/§2d): the ONE creation
/// brain both doors share. Category snapshot, catalog price, the
/// computed end date, the duplicate outcome (never a 500), the
/// blocked-price fork (form door: nothing created; AI door: the
/// draft persists), and $0 issuance inside the same flow.
/// Everything runs against the real EF model + the real fee file.
/// </summary>
public sealed class ApplicationTests
{
    // ------------------------------------------------------- harness

    private sealed record World(
        SolviaDbContext Db,
        SqliteConnection Connection,
        IFeeCatalog Catalog,
        ApplicationService Applications,
        IIssuanceService Issuance,
        PermitNumberService Numbers,
        ITermDatesService TermDates);

    private static World NewWorld(IFeeCatalog? catalog = null)
    {
        var (db, connection) = RedoTestDb.Create();
        var fees = catalog ?? new FeeCatalogService(RedoTestDb.FindFeesCsv());
        var numbers = new PermitNumberService(db);
        var issuance = new IssuanceService(numbers);
        var applications = new ApplicationService(
            db,
            fees,
            new PricingService(fees),
            new UserCategoryResolver(Options.Create(new CategoryOptions())),
            new TermDatesService(fees, db),
            issuance);
        return new World(
            db, connection, fees, applications, issuance, numbers,
            new TermDatesService(fees, db));
    }

    /// <summary>A catalog with ONE synthetic campus, priced by the test's row.</summary>
    private static FeeCatalogService SyntheticCatalog(string row)
    {
        var header = File.ReadLines(RedoTestDb.FindFeesCsv()).First();
        var path = Path.Combine(
            Path.GetTempPath(), $"solvia-r4-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, header + "\n" + row + "\n");
        return new FeeCatalogService(path);
    }

    // Term price published ($30), day pass NOT published.
    private const string DayPassUnpublishedRow =
        "Synthetic College,$30 Fall/Spring (cars),not listed on PMB page," +
        "not listed on PMB page,not listed on PMB page,not listed on PMB page," +
        "not listed on PMB page,synthetic R4 test row";

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

    private static ApplicationInput Input(
        string studentId,
        Guid vehicleId,
        string campus = "Fresno City College",
        PermitTypeCode type = PermitTypeCode.Term,
        TermKind term = TermKind.Fall,
        DateOnly? start = null,
        string source = "form",
        bool persistWhenBlocked = false) => new(
            studentId, vehicleId, campus, type, term,
            start ?? new DateOnly(2026, 8, 24), source, persistWhenBlocked);

    // --------------------------------------------------------- create

    [Fact]
    public async Task Create_Term_Student_PendingWithCatalogPrice_AndWindowEnd()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var vehicle = AddVehicle(w.Db, "maria");
        var dates = w.TermDates.GetTermDates(
            "Fresno City College", TermKind.Fall, 2026)!;

        var result = await w.Applications.CreateAsync(
            Input("maria", vehicle.Id, start: dates.TermStart));

        Assert.Equal(CreateApplicationStatus.Created, result.Status);
        var app = result.Application!;
        Assert.Equal(3000, app.PriceCents);                 // the catalog, not the type row
        Assert.Equal(PaymentStatus.Pending, app.PaymentStatus);
        Assert.Equal(UserCategory.Student, app.Category);   // snapshot
        Assert.Equal("Fresno City College", app.Campus);
        Assert.Equal("form", app.Source);
        Assert.Equal(dates.TermStart, app.StartDate);
        Assert.Equal(dates.PermitEndDate, app.EndDate);     // computed, never typed
        Assert.Null(app.ReceiptNumber);                     // not live yet
        Assert.NotEqual(Guid.Empty, app.ConcurrencyToken);
    }

    [Fact]
    public async Task Create_Employee_IsExempt_IssuedInsideTheSameFlow()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var vehicle = AddVehicle(w.Db, "elena", "EMP1234");

        var result = await w.Applications.CreateAsync(
            Input("elena", vehicle.Id));

        Assert.Equal(CreateApplicationStatus.Created, result.Status);
        var app = result.Application!;
        Assert.Equal(UserCategory.Employee, app.Category);
        Assert.Equal(0, app.PriceCents);                     // exempt AFTER resolution
        Assert.Equal(PaymentStatus.NotRequired, app.PaymentStatus);
        Assert.Matches(@"^SOL-\d{4}-\d{6}$", app.ReceiptNumber!);
        Assert.False(string.IsNullOrWhiteSpace(app.VerifyToken));
        Assert.NotNull(app.ReceiptIssuedAtUtc);
    }

    [Fact]
    public async Task Create_FreeCampus_IssuedInsideTheSameFlow()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var vehicle = AddVehicle(w.Db, "priya", "FREE123");

        var result = await w.Applications.CreateAsync(Input(
            "priya", vehicle.Id,
            campus: "Modesto Junior College / Columbia College / YCCD"));

        Assert.Equal(CreateApplicationStatus.Created, result.Status);
        var app = result.Application!;
        Assert.Equal(0, app.PriceCents);
        Assert.Equal(PaymentStatus.NotRequired, app.PaymentStatus);
        Assert.Matches(@"^SOL-\d{4}-\d{6}$", app.ReceiptNumber!);
        Assert.NotNull(app.ReceiptIssuedAtUtc);
    }

    [Fact]
    public async Task Create_Daily_EndsTheDayItStarts_AtTheDayPassPrice()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var vehicle = AddVehicle(w.Db, "maria");
        var fee = w.Catalog.Find("Fresno City College")!;
        var start = new DateOnly(2026, 10, 19);

        var result = await w.Applications.CreateAsync(
            Input("maria", vehicle.Id, type: PermitTypeCode.Daily, start: start));

        Assert.Equal(CreateApplicationStatus.Created, result.Status);
        var app = result.Application!;
        Assert.Equal(start, app.StartDate);
        Assert.Equal(start, app.EndDate);                   // daily: end == start
        Assert.Equal(fee.DayPassCents, app.PriceCents);     // parsed by the catalog
        Assert.Equal(100, app.PriceCents);                  // Fresno publishes $1.00
    }

    [Fact]
    public async Task Create_Duplicate_IsAnOutcome_NeverASecondRow()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var vehicle = AddVehicle(w.Db, "maria");
        var input = Input("maria", vehicle.Id);

        var first = await w.Applications.CreateAsync(input);
        var second = await w.Applications.CreateAsync(input);

        Assert.Equal(CreateApplicationStatus.Created, first.Status);
        Assert.Equal(CreateApplicationStatus.Duplicate, second.Status);
        Assert.Equal(first.Application!.Id, second.Application!.Id);   // the twin
        Assert.Contains("already submitted", second.ErrorMessage!);
        Assert.Single(w.Db.PermitApplications);
    }

    [Fact]
    public async Task Create_UnknownCampus_IsAnOutcome_NotAnException()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var vehicle = AddVehicle(w.Db, "maria");

        var result = await w.Applications.CreateAsync(
            Input("maria", vehicle.Id, campus: "Nowhere State"));

        Assert.Equal(CreateApplicationStatus.CampusNotFound, result.Status);
        Assert.Empty(w.Db.PermitApplications);
    }

    // ------------------------------------------- the blocked-price fork

    [Fact]
    public async Task Create_UnpublishedPrice_FormDoorStyle_CreatesNothing()
    {
        var w = NewWorld(SyntheticCatalog(DayPassUnpublishedRow));
        using var _ = w.Connection;
        using var __ = w.Db;
        var vehicle = AddVehicle(w.Db, "maria");

        var result = await w.Applications.CreateAsync(Input(
            "maria", vehicle.Id, campus: "Synthetic College",
            type: PermitTypeCode.Daily, persistWhenBlocked: false));

        Assert.Equal(CreateApplicationStatus.PriceNotPublished, result.Status);
        Assert.Contains("Synthetic College", result.ErrorMessage!);   // names the campus
        Assert.Contains("day-pass", result.ErrorMessage!);            // and the missing price
        Assert.Empty(w.Db.PermitApplications);
    }

    [Fact]
    public async Task Create_UnpublishedPrice_AiDoorStyle_PersistsTheBlockedDraft()
    {
        var w = NewWorld(SyntheticCatalog(DayPassUnpublishedRow));
        using var _ = w.Connection;
        using var __ = w.Db;
        var vehicle = AddVehicle(w.Db, "maria");

        var result = await w.Applications.CreateAsync(Input(
            "maria", vehicle.Id, campus: "Synthetic College",
            type: PermitTypeCode.Daily, source: "fallback",
            persistWhenBlocked: true));

        Assert.Equal(CreateApplicationStatus.Created, result.Status);
        var app = result.Application!;
        Assert.Equal(0, app.PriceCents);                    // parked placeholder
        Assert.Equal(PaymentStatus.Pending, app.PaymentStatus);
        Assert.Null(app.ReceiptNumber);                     // NOT issued — it is blocked
    }

    [Fact]
    public async Task Create_UnpublishedPrice_BlocksEmployeeToo()
    {
        // The catalog is consulted BEFORE the exemption (§2a): an
        // unpublished fee blocks even the exempt category.
        var w = NewWorld(SyntheticCatalog(DayPassUnpublishedRow));
        using var _ = w.Connection;
        using var __ = w.Db;
        var vehicle = AddVehicle(w.Db, "elena", "EMP1234");

        var result = await w.Applications.CreateAsync(Input(
            "elena", vehicle.Id, campus: "Synthetic College",
            type: PermitTypeCode.Daily));

        Assert.Equal(CreateApplicationStatus.PriceNotPublished, result.Status);
        Assert.Empty(w.Db.PermitApplications);
    }

    // --------------------------------------------------------- revise

    [Fact]
    public async Task Revise_ChangesTypeAndPrice_RotatesToken_KeepsCategorySnapshot()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var vehicle = AddVehicle(w.Db, "maria");
        var created = await w.Applications.CreateAsync(Input("maria", vehicle.Id));
        var app = created.Application!;
        var tokenBefore = app.ConcurrencyToken;

        var revised = await w.Applications.ReviseDraftAsync(
            app.Id,
            Input("maria", vehicle.Id, type: PermitTypeCode.Daily,
                start: new DateOnly(2026, 10, 19), source: "fallback",
                persistWhenBlocked: true));

        Assert.Equal(CreateApplicationStatus.Created, revised.Status);
        var row = w.Db.PermitApplications.Single();
        Assert.Equal(100, row.PriceCents);                  // Fresno day pass
        Assert.Equal(new DateOnly(2026, 10, 19), row.EndDate);
        Assert.Equal(UserCategory.Student, row.Category);   // snapshot unmoved
        Assert.NotEqual(tokenBefore, row.ConcurrencyToken);
    }

    [Fact]
    public async Task Revise_IssuedApplication_IsAlreadyLive_AndUntouched()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var vehicle = AddVehicle(w.Db, "elena", "EMP1234");
        var created = await w.Applications.CreateAsync(Input("elena", vehicle.Id));
        var app = created.Application!;
        var number = app.ReceiptNumber;

        var revised = await w.Applications.ReviseDraftAsync(
            app.Id,
            Input("elena", vehicle.Id, type: PermitTypeCode.Daily,
                persistWhenBlocked: true));

        Assert.Equal(CreateApplicationStatus.AlreadyLive, revised.Status);
        var row = w.Db.PermitApplications.Single();
        Assert.Equal(number, row.ReceiptNumber);
        Assert.Equal(0, row.PriceCents);                    // still the $0 exemption
        Assert.Equal(PaymentStatus.NotRequired, row.PaymentStatus);
    }

    // ------------------------------------------- vehicle resolution

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ResolveVehicle_OverlongDetail_CannotMutateExistingVehicle(bool make)
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var existing = AddVehicle(w.Db, "maria");

        await Assert.ThrowsAsync<ArgumentException>(() => w.Applications.ResolveVehicleForDraftAsync(
            "maria", existing.Plate, make ? new string('x', 51) : "Changed make",
            make ? "Changed model" : new string('x', 51), "Red", "NV"));

        Assert.Equal("Honda", existing.Make);
        Assert.Equal("Civic", existing.Model);
        Assert.Equal(VehicleColor.Blue, existing.Color);
        Assert.Equal(UsState.CA, existing.State);
        Assert.Single(w.Db.Vehicles);
        Assert.Equal(EntityState.Unchanged, w.Db.Entry(existing).State);
    }

    [Theory]
    [InlineData("4", "Blue")]
    [InlineData("999", "Blue")]
    [InlineData("+4", "Blue")]
    [InlineData("CA", "5")]
    [InlineData("CA", "999")]
    [InlineData("CA", "-1")]
    [InlineData("CA,AL", "Blue")]
    public async Task ResolveVehicle_NumericOrCompositeEnum_IsNotSaved(string state, string color)
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;

        await Assert.ThrowsAsync<ArgumentException>(() => w.Applications.ResolveVehicleForDraftAsync(
            "maria", "8XYZ123", "Honda", "Civic", color, state));

        Assert.Empty(w.Db.Vehicles);
        Assert.DoesNotContain(w.Db.ChangeTracker.Entries<Vehicle>(), entry => entry.State == EntityState.Added);
    }

    [Fact]
    public async Task ResolveVehicle_ExactFieldLimitsAndNamedEnums_AreAccepted()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;

        var vehicle = await w.Applications.ResolveVehicleForDraftAsync(
            "maria", "8XYZ123", new string('a', 50), new string('b', 50), "blue", "ca");

        Assert.Equal(50, vehicle.Make!.Length);
        Assert.Equal(50, vehicle.Model!.Length);
        Assert.Equal(UsState.CA, vehicle.State);
        Assert.Equal(VehicleColor.Blue, vehicle.Color);
        Assert.Single(w.Db.Vehicles);
    }

    [Fact]
    public async Task ResolveVehicle_CreatesThenReusesAndUpdates()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;

        var first = await w.Applications.ResolveVehicleForDraftAsync(
            "maria", "8xyz 123", "Honda", "Civic", "Blue", "CA");
        Assert.Equal("8XYZ123", first.Plate);               // normalized
        Assert.Equal("maria", first.StudentId);             // server-stamped owner
        Assert.Equal(VehicleColor.Blue, first.Color);
        Assert.Equal(UsState.CA, first.State);

        var second = await w.Applications.ResolveVehicleForDraftAsync(
            "maria", "8XYZ123", "Honda", "Accord", "Red", null);
        Assert.Equal(first.Id, second.Id);                  // same row, updated
        Assert.Equal("Accord", second.Model);
        Assert.Equal(VehicleColor.Red, second.Color);
        Assert.Single(w.Db.Vehicles);
    }

    // -------------------------------------------------------- issuance

    [Fact]
    public async Task Issuance_MintsOnce_SecondCallIsANoOp()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var vehicle = AddVehicle(w.Db, "maria");
        var created = await w.Applications.CreateAsync(Input("maria", vehicle.Id));
        var app = created.Application!;
        Assert.Null(app.ReceiptNumber);

        var minted = await w.Issuance.IssueIfAbsentAsync(app);
        w.Db.SaveChanges();
        var number = app.ReceiptNumber;
        Assert.True(minted);
        Assert.Matches(@"^SOL-\d{4}-\d{6}$", number!);
        Assert.NotNull(app.ReceiptIssuedAtUtc);

        var again = await w.Issuance.IssueIfAbsentAsync(app);
        Assert.False(again);                                // mint-if-absent
        Assert.Equal(number, app.ReceiptNumber);
    }

    [Fact]
    public async Task Numbers_AreUniqueFormatted_AndTokensUnguessable()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;

        var drawn = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            drawn.Add(await w.Numbers.IssueReceiptNumberAsync());
        }

        Assert.All(drawn, n => Assert.Matches(@"^SOL-\d{4}-\d{6}$", n));
        Assert.Equal(5, drawn.Distinct().Count());

        var tokenA = w.Numbers.NewVerifyToken();
        var tokenB = w.Numbers.NewVerifyToken();
        Assert.Equal(32, tokenA.Length);
        Assert.NotEqual(tokenA, tokenB);
    }
}
