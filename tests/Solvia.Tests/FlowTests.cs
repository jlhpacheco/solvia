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
/// R4 door tests (REDO-PLAN §3): PermitsController over the real EF
/// model + the real (or a synthetic) fee catalog. The form door is
/// Duvara's shape — same-form errors with Duvara's messages (item
/// 16), free issued at the POST, paid redirected to Checkout. The
/// AI door is Solvia's — the Understood gate holds without a plate,
/// the draft persists through ApplicationService, Review shows the
/// exact catalog total, and Confirm takes the id only.
///
/// Extraction is answered by the deterministic fallback (the
/// production failure path when Ollama is off) — the same fields,
/// with Source=fallback, exactly as a judge's machine with no model
/// would answer.
/// </summary>
public sealed class FlowTests
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

    /// <summary>The fallback behind the IAiExtractor seam (Ollama-off path).</summary>
    private sealed class FallbackAiExtractor : IAiExtractor
    {
        private readonly FallbackExtractor _inner;

        public FallbackAiExtractor(IFeeCatalog catalog) =>
            _inner = new FallbackExtractor(catalog);

        public Task<ExtractionResult> ExtractAsync(
            string studentText, CancellationToken cancellationToken = default) =>
            Task.FromResult(_inner.Extract(studentText));
    }

    private sealed record World(
        SolviaDbContext Db,
        SqliteConnection Connection,
        IFeeCatalog Catalog,
        ITermDatesService TermDates);

    private static World NewWorld(IFeeCatalog? catalog = null)
    {
        var (db, connection) = RedoTestDb.Create();
        var fees = catalog ?? new FeeCatalogService(RedoTestDb.FindFeesCsv());
        return new World(db, connection, fees, new TermDatesService(fees, db));
    }

    private static PermitsController NewController(
        World w, string asStudent = "maria")
    {
        var db = w.Db;
        var numbers = new PermitNumberService(db);
        var applications = new ApplicationService(
            db,
            w.Catalog,
            new PricingService(w.Catalog),
            new UserCategoryResolver(Options.Create(new CategoryOptions())),
            w.TermDates,
            new IssuanceService(numbers));

        var http = new DefaultHttpContext();
        http.Request.Headers.Cookie = $"solvia_student={asStudent}";
        var controller = new PermitsController(
            db,
            w.Catalog,
            new FallbackAiExtractor(w.Catalog),
            w.TermDates,
            new PricingService(w.Catalog),
            new UserCategoryResolver(Options.Create(new CategoryOptions())),
            applications)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
        controller.TempData = new TempDataDictionary(http, new StubTempDataProvider());
        return controller;
    }

    private static FeeCatalogService SyntheticCatalog(string row)
    {
        var header = File.ReadLines(RedoTestDb.FindFeesCsv()).First();
        var path = Path.Combine(
            Path.GetTempPath(), $"solvia-r4flow-{Guid.NewGuid():N}.csv");
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

    private static int TermTypeId(SolviaDbContext db) =>
        db.PermitTypes.Single(t => t.Code == PermitTypeCode.Term).Id;

    private static ApplyViewModel ApplyForm(
        World w, Vehicle vehicle, DateOnly start) => new()
    {
        Campus = "Fresno City College",
        VehicleId = vehicle.Id,
        PermitTypeId = TermTypeId(w.Db),
        Term = TermKind.Fall,
        StartDate = start,
    };

    // ------------------------------------------------------ form door

    [Fact]
    public async Task ApplyGet_ZeroVehicles_ReplacesTheFormWithTheCard()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var controller = NewController(w);

        var result = await controller.Apply(default);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ApplyViewModel>(view.Model);
        Assert.True(model.NoVehicles);                      // item 6 card
        Assert.False(model.NoTypes);
    }

    [Fact]
    public async Task ApplyGet_WithVehicleAndTypes_ShowsTheForm()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        AddVehicle(w.Db, "maria");
        var controller = NewController(w);

        var result = await controller.Apply(default);

        var model = Assert.IsType<ApplyViewModel>(
            Assert.IsType<ViewResult>(result).Model);
        Assert.False(model.NoVehicles);
        Assert.False(model.NoTypes);
        Assert.Equal(10, model.Campuses.Count);
        Assert.Equal(2, model.Types.Count);                 // active only
        var option = Assert.Single(model.Vehicles);
        Assert.Equal("8XYZ123 · CA · Honda Civic", option.Label);   // item 2 label
    }

    [Fact]
    public async Task ApplyPost_PaidApplication_RedirectsStraightToCheckout()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var vehicle = AddVehicle(w.Db, "maria");
        var controller = NewController(w);
        var window = w.TermDates.GetTermDates(
            "Fresno City College", TermKind.Fall, DateTime.Now.Year)!;

        var result = await controller.Apply(
            ApplyForm(w, vehicle, window.TermStart), default);

        var app = w.Db.PermitApplications.Single();
        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal($"/Checkout/{app.Id}", redirect.Url);  // item 18
        Assert.Equal(3000, app.PriceCents);
        Assert.Equal("form", app.Source);
        Assert.Equal(PaymentStatus.Pending, app.PaymentStatus);
    }

    [Fact]
    public async Task ApplyPost_Employee_IsIssuedAtThePost_AndNeverSeesCheckout()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var vehicle = AddVehicle(w.Db, "elena", "EMP1234");
        var controller = NewController(w, "elena");
        var window = w.TermDates.GetTermDates(
            "Fresno City College", TermKind.Fall, DateTime.Now.Year)!;

        var result = await controller.Apply(
            ApplyForm(w, vehicle, window.TermStart), default);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/Permits/My", redirect.Url);          // item 17
        var app = w.Db.PermitApplications.Single();
        Assert.Equal(PaymentStatus.NotRequired, app.PaymentStatus);
        Assert.False(string.IsNullOrWhiteSpace(app.ReceiptNumber));
        Assert.Contains("live", controller.TempData["StatusMessage"]?.ToString() ?? "");
    }

    [Fact]
    public async Task ApplyPost_NoVehicle_SameForm_SelectAVehicle()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        AddVehicle(w.Db, "maria");
        var controller = NewController(w);
        var form = ApplyForm(w, new Vehicle { Id = Guid.Empty }, DateOnly.FromDateTime(DateTime.Now));
        form.VehicleId = Guid.Empty;

        var result = await controller.Apply(form, default);

        var model = Assert.IsType<ApplyViewModel>(
            Assert.IsType<ViewResult>(result).Model);
        Assert.Contains("Please select a vehicle.", model.Errors);
        Assert.Empty(w.Db.PermitApplications);
    }

    [Fact]
    public async Task ApplyPost_NoType_SameForm_SelectAPermitType()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var vehicle = AddVehicle(w.Db, "maria");
        var controller = NewController(w);
        var form = ApplyForm(w, vehicle, DateOnly.FromDateTime(DateTime.Now));
        form.PermitTypeId = 0;

        var result = await controller.Apply(form, default);

        var model = Assert.IsType<ApplyViewModel>(
            Assert.IsType<ViewResult>(result).Model);
        Assert.Contains("Please select a permit type.", model.Errors);
        Assert.Empty(w.Db.PermitApplications);
    }

    [Fact]
    public async Task ApplyPost_SomeoneElsesVehicle_GeneralInvalidSelection()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var diegosCar = AddVehicle(w.Db, "diego", "DGO1234");
        var controller = NewController(w, "maria");

        var result = await controller.Apply(
            ApplyForm(w, diegosCar, DateOnly.FromDateTime(DateTime.Now)), default);

        var model = Assert.IsType<ApplyViewModel>(
            Assert.IsType<ViewResult>(result).Model);
        Assert.Contains(model.Errors, e => e.Contains("not valid"));
        Assert.Empty(w.Db.PermitApplications);
    }

    [Fact]
    public async Task ApplyPost_StartOutsideTheTermWindow_ErrorNamesTheWindow()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var vehicle = AddVehicle(w.Db, "maria");
        var controller = NewController(w);
        var window = w.TermDates.GetTermDates(
            "Fresno City College", TermKind.Fall, DateTime.Now.Year)!;
        var form = ApplyForm(w, vehicle, window.TermStart.AddDays(-7));

        var result = await controller.Apply(form, default);

        var model = Assert.IsType<ApplyViewModel>(
            Assert.IsType<ViewResult>(result).Model);
        var error = Assert.Single(model.Errors);
        Assert.Contains("Fall runs", error);
        Assert.Contains(window.TermStart.ToString("MMM d"), error);
        Assert.Contains(window.TermEnd.ToString("MMM d"), error);
        Assert.Empty(w.Db.PermitApplications);
    }

    [Fact]
    public async Task ApplyPost_Duplicate_SameForm_AlreadySubmitted()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var vehicle = AddVehicle(w.Db, "maria");
        var controller = NewController(w);
        var window = w.TermDates.GetTermDates(
            "Fresno City College", TermKind.Fall, DateTime.Now.Year)!;
        var form = ApplyForm(w, vehicle, window.TermStart);

        var first = await controller.Apply(form, default);
        Assert.IsType<RedirectResult>(first);

        var second = await controller.Apply(
            ApplyForm(w, vehicle, window.TermStart), default);

        var model = Assert.IsType<ApplyViewModel>(
            Assert.IsType<ViewResult>(second).Model);
        Assert.Contains(model.Errors, e => e.Contains("already submitted"));
        Assert.Single(w.Db.PermitApplications);            // still exactly one
    }

    // -------------------------------------------------------- AI door

    private static async Task<PermitDraftViewModel> UnderstandAsync(
        PermitsController controller,
        string college = "Fresno City College",
        string carText = "blue Honda Civic",
        PermitTypeCode type = PermitTypeCode.Term,
        TermKind term = TermKind.Fall)
    {
        var result = await controller.Understand(
            college, carText, type, term, applicationId: null, default);
        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Understood", view.ViewName);
        return Assert.IsType<PermitDraftViewModel>(view.Model);
    }

    [Fact]
    public async Task AiDoor_NoPlateInSentence_TheGateHoldsAtUnderstood()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var controller = NewController(w);

        // The sentence that stalled v1: a car with NO plate in it.
        var draft = await UnderstandAsync(controller);
        Assert.Null(draft.Plate);
        Assert.Equal("Honda", draft.CarMake);
        Assert.Equal("fallback", draft.Source);

        var held = await controller.Review(draft, default);

        var view = Assert.IsType<ViewResult>(held);
        Assert.Equal("Understood", view.ViewName);          // stays at the gate
        var gated = Assert.IsType<PermitDraftViewModel>(view.Model);
        Assert.True(gated.PlateMissing);
        Assert.Empty(w.Db.PermitApplications);              // nothing saved
    }

    [Fact]
    public async Task AiDoor_TypedPlate_PersistsDraft_ReviewShowsExactTotal_ConfirmHandsOff()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var controller = NewController(w);
        var draft = await UnderstandAsync(controller);
        draft.Plate = "8XYZ123";                            // typed at the gate

        var posted = await controller.Review(draft, default);

        var toReview = Assert.IsType<RedirectToActionResult>(posted);
        Assert.Equal("Review", toReview.ActionName);
        var app = w.Db.PermitApplications.Single();
        Assert.Equal(app.Id, toReview.RouteValues?["id"]);
        Assert.Equal(3000, app.PriceCents);
        Assert.Equal(PaymentStatus.Pending, app.PaymentStatus);
        Assert.Equal("fallback", app.Source);
        var vehicle = w.Db.Vehicles.Single();
        Assert.Equal("8XYZ123", vehicle.Plate);
        Assert.Equal("Honda", vehicle.Make);
        Assert.Equal("maria", vehicle.StudentId);

        var reviewPage = await controller.Review(app.Id, default);
        var review = Assert.IsType<ReviewViewModel>(
            Assert.IsType<ViewResult>(reviewPage).Model);
        Assert.False(review.Blocked);
        Assert.Equal("$30.00", review.TotalDisplay);        // the trust moment
        Assert.Equal("8XYZ123 (CA)", review.PlateLine);

        var confirmed = await controller.Confirm(app.Id, default);
        var redirect = Assert.IsType<RedirectResult>(confirmed);
        Assert.Equal($"/Checkout/{app.Id}", redirect.Url);  // id only → Checkout
    }

    [Fact]
    public async Task AiDoor_FreeCampus_IssuesAtUnderstood_AndSkipsReview()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var controller = NewController(w, "priya");
        var draft = await UnderstandAsync(
            controller,
            college: "Modesto Junior College / Columbia College / YCCD",
            carText: "white Toyota Camry, plate 9ABC123");
        Assert.Equal("9ABC123", draft.Plate);               // the reader caught it

        var posted = await controller.Review(draft, default);

        var redirect = Assert.IsType<RedirectResult>(posted);
        var app = w.Db.PermitApplications.Single();
        Assert.Equal($"/Permits/Receipt/{app.Id}", redirect.Url);
        Assert.Equal(PaymentStatus.NotRequired, app.PaymentStatus);
        Assert.False(string.IsNullOrWhiteSpace(app.ReceiptNumber));
    }

    [Fact]
    public async Task AiDoor_DayPassBlock_OffersTerm_SwitchTypeUnblocks()
    {
        var w = NewWorld(SyntheticCatalog(DayPassUnpublishedRow));
        using var _ = w.Connection;
        using var __ = w.Db;
        var controller = NewController(w);
        var draft = await UnderstandAsync(
            controller, college: "Synthetic College",
            carText: "blue Honda Civic, plate 8XYZ123",
            type: PermitTypeCode.Daily);
        draft.Plate ??= "8XYZ123";

        var posted = await controller.Review(draft, default);
        Assert.IsType<RedirectToActionResult>(posted);
        var app = w.Db.PermitApplications.Single();

        var blockedPage = await controller.Review(app.Id, default);
        var blocked = Assert.IsType<ReviewViewModel>(
            Assert.IsType<ViewResult>(blockedPage).Model);
        Assert.True(blocked.Blocked);
        Assert.True(blocked.IsDayPassBlock);
        Assert.Equal(3000, blocked.TermAlternativePriceCents);

        // Confirm on a blocked draft stays on the blocked Review.
        var confirmBlocked = await controller.Confirm(app.Id, default);
        Assert.Equal("Review", Assert.IsType<ViewResult>(confirmBlocked).ViewName);

        var switched = await controller.SwitchType(app.Id, PermitTypeCode.Term, default);
        Assert.IsType<RedirectToActionResult>(switched);

        var unblockedPage = await controller.Review(app.Id, default);
        var unblocked = Assert.IsType<ReviewViewModel>(
            Assert.IsType<ViewResult>(unblockedPage).Model);
        Assert.False(unblocked.Blocked);
        Assert.Equal("$30.00", unblocked.TotalDisplay);
        Assert.Equal(3000, w.Db.PermitApplications.Single().PriceCents);
    }
}
