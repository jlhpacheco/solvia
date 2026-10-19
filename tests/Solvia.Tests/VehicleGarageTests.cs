using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Solvia.Controllers;
using Solvia.Data;
using Solvia.Models;
using Solvia.Models.Enums;
using Solvia.Models.ViewModels;

namespace Solvia.Tests;

/// <summary>
/// R2 vehicle-garage tests (REDO-PLAN §3): the reference app's CRUD
/// shape on the demo-person model — the owner is stamped on the
/// server, the plate is required and normalized, off-catalog
/// values survive, and another person's car is invisible (404).
/// Controller tests: no view renders; the TempData stub below is
/// what lets the success paths run outside the real pipeline.
/// </summary>
public sealed class VehicleGarageTests
{
    // ------------------------------------------------------- harness

    /// <summary>TempData writes need a provider; this one holds nothing.</summary>
    private sealed class StubTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) =>
            new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }

    /// <summary>
    /// A garage controller wired for one demo person. The demo base
    /// reads the person from the solvia_student cookie, so the "as"
    /// argument chooses who the test acts as (maria when omitted).
    /// </summary>
    private static VehiclesController NewController(
        SolviaDbContext db, string asStudent = "maria")
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.Cookie = $"solvia_student={asStudent}";

        var controller = new VehiclesController(db)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
        controller.TempData = new TempDataDictionary(http, new StubTempDataProvider());
        return controller;
    }

    private static (VehiclesController Controller, SolviaDbContext Db, Microsoft.Data.Sqlite.SqliteConnection Connection)
        CreateController(string asStudent = "maria")
    {
        var (db, connection) = RedoTestDb.Create();
        return (NewController(db, asStudent), db, connection);
    }

    private static VehicleEditViewModel Form(
        string plate = "8XYZ123",
        UsState state = UsState.CA,
        string? make = "Honda",
        string? model = "Civic",
        VehicleColor color = VehicleColor.Blue) => new()
    {
        Plate = plate,
        State = state,
        Make = make,
        Model = model,
        Color = color,
    };

    private static string? ReadMessage(ITempDataDictionary tempData) =>
        tempData.TryGetValue("StatusMessage", out var message)
            ? message?.ToString()
            : null;

    // ---------------------------------------------------------- create

    [Fact]
    public async Task Create_Persists_WithServerStampedOwner_AndNormalizedPlate()
    {
        var (controller, db, connection) = CreateController("diego");
        using var _ = connection;
        using var __ = db;

        // The form carries no owner at all — there is no StudentId
        // property on the view model to trust or abuse.
        var result = await controller.Create(
            Form(plate: " 7abc 123 ", state: UsState.OR, make: "Toyota", model: "Camry"),
            default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(VehiclesController.Index), redirect.ActionName);
        Assert.Contains("7ABC123", ReadMessage(controller.TempData) ?? "");

        var vehicle = db.Vehicles.Single();
        Assert.Equal("diego", vehicle.StudentId);       // server-stamped owner
        Assert.Equal("7ABC123", vehicle.Plate);        // trimmed, de-spaced, uppercased
        Assert.Equal(UsState.OR, vehicle.State);
        Assert.Equal("Toyota", vehicle.Make);
        Assert.Equal("Camry", vehicle.Model);
        Assert.Equal(VehicleColor.Blue, vehicle.Color);
    }

    [Fact]
    public async Task Create_BlankPlate_SavesNothing_AndSaysSo()
    {
        var (controller, db, connection) = CreateController();
        using var _ = connection;
        using var __ = db;

        var result = await controller.Create(Form(plate: "   "), default);

        var view = Assert.IsType<ViewResult>(result);
        var returned = Assert.IsType<VehicleEditViewModel>(view.Model);
        Assert.False(string.IsNullOrWhiteSpace(returned.Error));
        Assert.Contains("plate", returned.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(db.Vehicles);
    }

    [Fact]
    public async Task Create_OffCatalogMakeModel_PersistExactlyAsTyped()
    {
        var (controller, db, connection) = CreateController();
        using var _ = connection;
        using var __ = db;

        // "GMC" is in no dropdown — the catalog is a helper, never a
        // gate, so the test can POST it the way a browser never could.
        var result = await controller.Create(
            Form(make: "GMC", model: "Sierra 1500"), default);

        Assert.IsType<RedirectToActionResult>(result);
        var vehicle = db.Vehicles.Single();
        Assert.Equal("GMC", vehicle.Make);
        Assert.Equal("Sierra 1500", vehicle.Model);
    }

    [Fact]
    public async Task Create_OtherSentinel_UsesTheTypedText()
    {
        var (controller, db, connection) = CreateController();
        using var _ = connection;
        using var __ = db;

        var form = Form(make: VehicleCatalog.OtherMakeValue, model: VehicleCatalog.OtherModelValue);
        form.MakeOther = "Rivian";
        form.ModelOther = "R1T";
        var result = await controller.Create(form, default);

        Assert.IsType<RedirectToActionResult>(result);
        var vehicle = db.Vehicles.Single();
        Assert.Equal("Rivian", vehicle.Make);
        Assert.Equal("R1T", vehicle.Model);
    }

    // ------------------------------------------------------------ edit

    [Fact]
    public async Task Edit_AnotherPersonsVehicle_Is404_ForGetAndPost()
    {
        var (mariaController, db, connection) = CreateController("maria");
        using var _ = connection;
        using var __ = db;
        var created = await mariaController.Create(Form(plate: "8XYZ123"), default);
        Assert.IsType<RedirectToActionResult>(created);
        var vehicle = db.Vehicles.Single();

        // Diego acts against Maria's car: it must not even look found.
        var diego = NewController(db, "diego");
        Assert.IsType<NotFoundResult>(await diego.Edit(vehicle.Id, default));

        var post = await diego.Edit(vehicle.Id, Form(plate: "STOLEN1"), default);
        Assert.IsType<NotFoundResult>(post);
        Assert.Equal("8XYZ123", db.Vehicles.Single().Plate);   // untouched
        Assert.Equal(1, db.Vehicles.Count());
    }

    [Fact]
    public async Task Edit_OwnVehicle_ReoffersStoredValues_AndSavesEdits()
    {
        var (controller, db, connection) = CreateController();
        using var _ = connection;
        using var __ = db;
        var created = await controller.Create(
            Form(plate: "8XYZ123", make: "Honda", model: "Civic", color: VehicleColor.Blue),
            default);
        Assert.IsType<RedirectToActionResult>(created);
        var vehicle = db.Vehicles.Single();

        // The GET pre-fills the form with what is stored — nothing lost.
        var get = Assert.IsType<ViewResult>(await controller.Edit(vehicle.Id, default));
        var shown = Assert.IsType<VehicleEditViewModel>(get.Model);
        Assert.Equal("8XYZ123", shown.Plate);
        Assert.Equal(UsState.CA, shown.State);
        Assert.Equal("Honda", shown.Make);
        Assert.Equal("Civic", shown.Model);
        Assert.Equal(VehicleColor.Blue, shown.Color);

        // And an edit lands on the same row.
        var editForm = Form(plate: " 9new 999 ", make: "Honda", model: "Accord", color: VehicleColor.Red);
        var post = await controller.Edit(vehicle.Id, editForm, default);
        var redirect = Assert.IsType<RedirectToActionResult>(post);
        Assert.Equal(nameof(VehiclesController.Index), redirect.ActionName);

        var row = db.Vehicles.Single(v => v.Id == vehicle.Id);
        Assert.Equal("9NEW999", row.Plate);
        Assert.Equal("Accord", row.Model);
        Assert.Equal(VehicleColor.Red, row.Color);
        Assert.Equal("maria", row.StudentId);   // the owner never moves
    }
}
