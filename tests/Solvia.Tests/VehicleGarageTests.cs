using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Solvia.Controllers;
using Solvia.Data;
using Solvia.Models.Enums;
using Solvia.Models.ViewModels;
using Solvia.Services;

namespace Solvia.Tests;

/// <summary>
/// The garage tests (Duvara-exact vehicle set). Duvara has a vehicle
/// Create screen outside the permit flow; Solvia had only Index +
/// Edit, and Edit free-typed Make/Model. Now: Create (GET+POST)
/// exists, Make/Model are catalog dropdowns (the server still
/// accepts any text — the dropdowns' "Other — type it" contract),
/// and the plate state (Vehicle.State, default CA) persists end to
/// end. The Maria flow's vehicle is covered too: no state named in
/// her sentence → the saved car carries CA.
/// </summary>
public sealed class VehicleGarageTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<SolviaDbContext> _options;
    private readonly FeeCatalogService _catalog = new(FindFeesCsv());

    public VehicleGarageTests()
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

    private VehiclesController NewVehiclesController(SolviaDbContext db)
    {
        var controller = new VehiclesController(db);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };
        return controller;
    }

    private PermitsController NewPermitsController(SolviaDbContext db)
    {
        var controller = new PermitsController(
            db,
            _catalog,
            new FallbackAiExtractor(new FallbackExtractor(_catalog)),
            new StubTermDatesService(),
            new BuiltinCollegeSearch(_catalog),
            new PermitPdfService(new HttpContextAccessor()));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };
        return controller;
    }

    // ------------------------------------------------------------------
    // Create: the screen Duvara had and Solvia was missing
    // ------------------------------------------------------------------

    [Fact]
    public async Task Create_PersistsVehicle_StateAndOwnerStampedByServer()
    {
        using var db = NewDb();
        var controller = NewVehiclesController(db);

        var result = Assert.IsType<RedirectToActionResult>(await controller.Create(
            new VehicleEditViewModel
            {
                Plate = "8xyz 123",
                State = UsState.OR,
                Make = "Honda",
                Model = "Civic",
                Color = VehicleColor.Blue,
            }, default));
        Assert.Equal("Index", result.ActionName);

        var saved = await db.Vehicles.SingleAsync();
        Assert.Equal("maria", saved.StudentId);   // owner stamped server-side
        Assert.Equal("8XYZ123", saved.Plate);     // normalized, as in Edit
        Assert.Equal(UsState.OR, saved.State);     // the plate state persists
        Assert.Equal("Honda", saved.Make);
        Assert.Equal("Civic", saved.Model);
        Assert.Equal(VehicleColor.Blue, saved.Color);
    }

    [Fact]
    public async Task Create_BlankPlate_SavesNothing()
    {
        using var db = NewDb();
        var controller = NewVehiclesController(db);

        var result = await controller.Create(
            new VehicleEditViewModel { Plate = "   ", State = UsState.CA },
            default);

        Assert.IsType<ViewResult>(result);
        Assert.Equal(0, await db.Vehicles.CountAsync());
    }

    [Fact]
    public async Task Create_OffCatalogMakeAndModel_AreKeptAsTyped()
    {
        using var db = NewDb();
        var controller = NewVehiclesController(db);

        // The dropdowns' "Other — type it" fallback posts plain text;
        // the server must not reject what the catalog doesn't know.
        var result = await controller.Create(
            new VehicleEditViewModel
            {
                Plate = "7GMC777",
                State = UsState.CA,
                Make = "GMC",
                Model = "Sierra 1500",
                Color = VehicleColor.Black,
            }, default);

        Assert.IsType<RedirectToActionResult>(result);
        var saved = await db.Vehicles.SingleAsync();
        Assert.Equal("GMC", saved.Make);
        Assert.Equal("Sierra 1500", saved.Model);
    }

    // ------------------------------------------------------------------
    // The Maria flow: no state in her words → CA on the saved vehicle
    // ------------------------------------------------------------------

    [Fact]
    public async Task Flow_VehicleCreated_StateDefaultsToCA()
    {
        using var db = NewDb();
        var controller = NewPermitsController(db);

        var understood = Assert.IsType<ViewResult>(await controller.Understand(
            "Fresno City College", "blue 2019 Honda Civic, plate 8XYZ123",
            PermitTypeCode.Term, TermKind.Fall, null, default));
        var draft = Assert.IsType<PermitDraftViewModel>(understood.Model);
        Assert.Equal("8XYZ123", draft.Plate);

        var saved = Assert.IsType<RedirectToActionResult>(
            await controller.Review(draft, default));
        var id = Assert.IsType<Guid>(saved.RouteValues!["id"]);

        var stored = await db.PermitApplications
            .Include(a => a.Vehicle)
            .SingleAsync(a => a.Id == id);
        Assert.Equal("8XYZ123", stored.Vehicle!.Plate);
        Assert.Equal(UsState.CA, stored.Vehicle.State);
    }

    // ------------------------------------------------------------------
    // Test doubles (same shapes as FlowFixTests)
    // ------------------------------------------------------------------

    private sealed class FallbackAiExtractor(FallbackExtractor inner) : IAiExtractor
    {
        public Task<ExtractionResult> ExtractAsync(
            string studentText, CancellationToken cancellationToken = default) =>
            Task.FromResult(inner.Extract(studentText));
    }

    private sealed class StubTermDatesService : ITermDatesService
    {
        public TermDates? GetTermDates(string campusName, TermKind term, int year) => null;
    }

    private static string FindFeesCsv()
    {
        var starts = new[]
        {
            Path.GetDirectoryName(typeof(VehicleGarageTests).Assembly.Location),
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
