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

    private sealed class CountingExtractor : IAiExtractor
    {
        public int Calls { get; private set; }
        public ExtractionResult Result { get; init; } = new()
        {
            Source = ExtractionSource.Fallback,
            Plate = "8XYZ123",
            CarMake = "Honda",
            CarModel = "Civic",
        };

        public Task<ExtractionResult> ExtractAsync(string studentText, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Result);
        }
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
        World w, string asStudent = "maria", IAiExtractor? extractor = null)
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
            extractor ?? new FallbackAiExtractor(w.Catalog),
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

    [Theory]
    [InlineData(1000, 1, "Understood")]
    [InlineData(1001, 0, "Questions")]
    public async Task AiDoor_DescriptionLimit_GuardsReaderAndKeepsActualChoices(
        int characters, int expectedCalls, string expectedView)
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var extractor = new CountingExtractor();
        var controller = NewController(w, extractor: extractor);
        var applicationId = Guid.NewGuid();
        var text = new string('a', characters);

        var flowId = await BeginFlowAsync(controller, "Fresno City College", PermitTypeCode.Daily, TermKind.Spring);
        var response = await controller.Understand(
            "Fresno City College", text, PermitTypeCode.Daily, TermKind.Spring,
            applicationId, default, flowId: flowId);
        var result = response is RedirectToActionResult redirect
            ? Assert.IsType<ViewResult>(await controller.Understood((Guid)redirect.RouteValues!["flowId"]!, default))
            : Assert.IsType<ViewResult>(response);

        Assert.Equal(expectedView, result.ViewName);
        Assert.Equal(expectedCalls, extractor.Calls);
        if (characters > PermitDraftViewModel.MaxCarTextLength)
        {
            var model = Assert.IsType<QuestionsViewModel>(result.Model);
            Assert.Equal(text, model.CarText);
            Assert.Equal("Fresno City College", model.College);
            Assert.Equal(applicationId, model.ApplicationId);
            Assert.Equal(PermitTypeCode.Daily, model.PermitType);
            Assert.Equal(TermKind.Spring, model.Term);
            Assert.Contains("1,000", model.Notice ?? "");
        }
        else
        {
            var draft = Assert.IsType<PermitDraftViewModel>(result.Model);
            Assert.Equal(PermitTypeCode.Daily, draft.PermitType);
            Assert.Equal(TermKind.Spring, draft.Term);
        }
        Assert.Empty(w.Db.Vehicles);
        Assert.Empty(w.Db.PermitApplications);
    }

    [Fact]
    public async Task AiDoor_OversizedDescriptionPostedToReview_CannotBypassLimit()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var controller = NewController(w);
        var draft = new PermitDraftViewModel
        {
            College = "Fresno City College", Plate = "8XYZ123",
            CarText = new string('x', 1001), PermitType = PermitTypeCode.Daily,
            Term = TermKind.Spring,
        };

        var result = Assert.IsType<ViewResult>(await ReviewDraftAsync(controller, draft));

        Assert.Equal("Questions", result.ViewName);
        var model = Assert.IsType<QuestionsViewModel>(result.Model);
        Assert.Equal(draft.CarText, model.CarText);
        Assert.Equal(draft.PermitType, model.PermitType);
        Assert.Equal(draft.Term, model.Term);
        Assert.Contains("1,000", model.Notice ?? "");
        Assert.Empty(w.Db.Vehicles);
        Assert.Empty(w.Db.PermitApplications);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AiDoor_OverlongVehicleDetail_StaysEditableWithoutSaving(bool make)
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var controller = NewController(w);
        var draft = new PermitDraftViewModel
        {
            College = "Fresno City College", Plate = "8XYZ123", CarText = "my fictional car",
            CarMake = make ? new string('m', 51) : "Honda",
            CarModel = make ? "Civic" : new string('m', 51), CarColor = "Blue", CarState = "CA",
        };

        var result = Assert.IsType<ViewResult>(await ReviewDraftAsync(controller, draft));

        Assert.Equal("Understood", result.ViewName);
        Assert.Same(draft, result.Model);
        var field = make ? nameof(draft.CarMake) : nameof(draft.CarModel);
        Assert.Contains(controller.ModelState[field]!.Errors, error => error.ErrorMessage.Contains("50"));
        Assert.Empty(w.Db.Vehicles);
        Assert.Empty(w.Db.PermitApplications);
    }

    [Theory]
    [InlineData("4", "Blue", "CarState")]
    [InlineData("999", "Blue", "CarState")]
    [InlineData("+4", "Blue", "CarState")]
    [InlineData("CA", "5", "CarColor")]
    [InlineData("CA", "999", "CarColor")]
    [InlineData("CA", "-1", "CarColor")]
    public async Task AiDoor_NumericStateOrColor_IsRejectedBeforeVehicleSave(
        string state, string color, string invalidField)
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var controller = NewController(w);
        var draft = new PermitDraftViewModel
        {
            College = "Fresno City College", Plate = "8XYZ123",
            CarMake = "Honda", CarModel = "Civic", CarState = state, CarColor = color,
        };

        var result = Assert.IsType<ViewResult>(await ReviewDraftAsync(controller, draft));

        Assert.Equal("Understood", result.ViewName);
        Assert.NotEmpty(controller.ModelState[invalidField]!.Errors);
        Assert.Empty(w.Db.Vehicles);
        Assert.Empty(w.Db.PermitApplications);
    }

    [Theory]
    [InlineData(ExtractionSource.Ollama, "configured AI reader")]
    [InlineData(ExtractionSource.Together, "hosted AI reader via Together AI")]
    [InlineData(ExtractionSource.Fallback, "unavailable or returned an unusable answer")]
    public async Task AiDoor_ReaderNote_DescribesConfiguredReaderWithoutDeviceClaims(
        ExtractionSource source, string expectedNote)
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var extractor = new CountingExtractor { Result = new ExtractionResult { Source = source, Plate = "8XYZ123" } };
        var controller = NewController(w, extractor: extractor);

        var draft = await UnderstandAsync(controller, carText: "plate 8XYZ123");

        Assert.Contains(expectedNote, draft.SourceNote);
        Assert.DoesNotContain("this computer", draft.SourceNote);
        Assert.DoesNotContain("model is off", draft.SourceNote);
    }

    [Fact]
    public async Task AiDoor_NewDraftCannotChangeStateOnReferencedVehicle_OlderReceiptKeepsState()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var vehicle = AddVehicle(w.Db, "elena", "EMP1234");
        var firstController = NewController(w, "elena");
        var first = new PermitDraftViewModel
        {
            College = "Fresno City College", Plate = vehicle.Plate,
            CarMake = "Honda", CarModel = "Civic", CarState = "CA", CarColor = "Blue",
        };
        Assert.IsType<RedirectResult>(await ReviewDraftAsync(firstController, first));
        var older = Assert.Single(w.Db.PermitApplications);
        var olderReceipt = older.ReceiptNumber;
        Assert.False(string.IsNullOrWhiteSpace(olderReceipt));

        var controller = NewController(w, "elena");
        var differentState = new PermitDraftViewModel
        {
            College = "Fresno City College", Plate = vehicle.Plate,
            PermitType = PermitTypeCode.Daily, CarMake = "Changed make",
            CarModel = "Changed model", CarState = "NV", CarColor = "Red",
        };

        var held = Assert.IsType<ViewResult>(await ReviewDraftAsync(controller, differentState));

        Assert.Equal("Understood", held.ViewName);
        Assert.Contains(controller.ModelState[nameof(differentState.CarState)]!.Errors,
            error => error.ErrorMessage.Contains("linked to permit applications"));
        await w.Db.Entry(vehicle).ReloadAsync();
        Assert.Equal(UsState.CA, vehicle.State);
        Assert.Equal("Honda", vehicle.Make);
        Assert.Equal("Civic", vehicle.Model);
        Assert.Equal(VehicleColor.Blue, vehicle.Color);
        Assert.Single(w.Db.PermitApplications);

        var receipts = new MyPermitsController(w.Db) { ControllerContext = controller.ControllerContext };
        var receipt = Assert.IsType<ReceiptViewModel>(
            Assert.IsType<ViewResult>(await receipts.Receipt(older.Id, default)).Model);
        Assert.True(receipt.IsLive);
        Assert.Equal(olderReceipt, receipt.ReceiptNumber);
        Assert.Equal("EMP1234 (CA)", receipt.PlateLine);
    }

    private static async Task<Guid> BeginFlowAsync(PermitsController controller, string college,
        PermitTypeCode type = PermitTypeCode.Term, TermKind term = TermKind.Fall, DateOnly? start = null)
    {
        var response = Assert.IsType<RedirectToActionResult>(await controller.Questions(college, type, term, start));
        return Assert.IsType<Guid>(response.RouteValues!["flowId"]);
    }

    private static async Task<IActionResult> ReviewDraftAsync(PermitsController controller, PermitDraftViewModel draft)
    {
        if (draft.FlowId == Guid.Empty)
            draft.FlowId = await BeginFlowAsync(controller, draft.College, draft.PermitType, draft.Term, draft.StartDate);
        return await controller.Review(draft, default);
    }

    [Fact]
    public async Task AiDoor_ReviseLoop_KeepsSentenceCorrectionsChoicesDateAndOneApplication()
    {
        var w = NewWorld(); using var connection = w.Connection; using var db = w.Db;
        var extractor = new CountingExtractor();
        var controller = NewController(w, extractor: extractor);
        const string sentence = "white Volvo 760, license plate 234221";
        var draft = await UnderstandAsync(controller, carText: sentence, type: PermitTypeCode.Daily, term: TermKind.Summer);
        draft.Plate = "REV1234"; draft.CarMake = "Volvo"; draft.CarModel = "740";
        draft.CarColor = "White"; draft.CarState = "NV";
        draft.StartDate = DateOnly.FromDateTime(DateTime.Now).AddDays(4);
        Assert.IsType<RedirectToActionResult>(await controller.Review(draft, default));
        var application = Assert.Single(db.PermitApplications);
        var review = Assert.IsType<ReviewViewModel>(Assert.IsType<ViewResult>(await controller.Review(application.Id, default)).Model);
        Assert.Equal(sentence, review.Draft.CarText);
        Assert.Equal(draft.StartDate, review.Draft.StartDate);
        Assert.Equal("740", review.Draft.CarModel);
        Assert.IsType<RedirectToActionResult>(await controller.QuestionsBack(review.Draft));
        var questions = Assert.IsType<QuestionsViewModel>(Assert.IsType<ViewResult>(await controller.Questions(
            review.College, flowId: draft.FlowId)).Model);
        Assert.Equal(sentence, questions.CarText);
        Assert.Equal(PermitTypeCode.Daily, questions.PermitType);
        Assert.Equal(TermKind.Summer, questions.Term);
        Assert.Equal(draft.StartDate, questions.StartDate);
        Assert.IsType<RedirectToActionResult>(await controller.Understand(questions.College, questions.CarText,
            questions.PermitType, questions.Term, questions.ApplicationId, default, questions.StartDate, questions.FlowId));
        var restored = Assert.IsType<PermitDraftViewModel>(Assert.IsType<ViewResult>(await controller.Understood(draft.FlowId, default)).Model);
        Assert.Equal("740", restored.CarModel);
        Assert.Equal("NV", restored.CarState);
        Assert.Equal("White", restored.CarColor);
        Assert.Equal(application.Id, restored.ApplicationId);
        Assert.Equal(1, extractor.Calls); // GET/unchanged re-review never replays inference.
        Assert.IsType<RedirectToActionResult>(await controller.Review(restored, default));
        Assert.Single(db.PermitApplications);
        Assert.Equal(100, application.PriceCents);
        Assert.Null(application.PayPalOrderId);
    }

    [Fact]
    public async Task AiDoor_EditedSentenceRunsReaderAgain_AndIgnoresOldTabCorrections()
    {
        var w = NewWorld(); using var connection = w.Connection; using var db = w.Db;
        var extractor = new CountingExtractor();
        var controller = NewController(w, extractor: extractor);
        var draft = await UnderstandAsync(controller, carText: "white Volvo 760, plate REV1234");
        var revision = new PermitDraftViewModel { CarText = draft.CarText, CarModel = "740", Plate = "REV1234" };
        await controller.Understand(draft.College, "blue Honda Civic, plate NEW1234", draft.PermitType,
            draft.Term, null, default, flowId: draft.FlowId, revision: revision);
        var fresh = Assert.IsType<PermitDraftViewModel>(Assert.IsType<ViewResult>(await controller.Understood(draft.FlowId, default)).Model);
        Assert.Equal(2, extractor.Calls);
        Assert.Equal("Civic", fresh.CarModel);
        Assert.Equal("blue Honda Civic, plate NEW1234", fresh.CarText);
        Assert.Empty(db.PermitApplications);
    }

    [Fact]
    public async Task AiDoor_StalePersonAndMissingFlow_CannotPersistOrInvokeReader()
    {
        var w = NewWorld(); using var connection = w.Connection; using var db = w.Db;
        var extractor = new CountingExtractor();
        var maria = NewController(w, extractor: extractor);
        var draft = await UnderstandAsync(maria, carText: "blue Honda Civic, plate REV1234");
        var pat = NewController(w, "pat", extractor);
        pat.TempData = maria.TempData; // Same protected browser cookie; different selected person.
        Assert.Equal("Ask", Assert.IsType<RedirectToActionResult>(await pat.Review(draft, default)).ActionName);
        Assert.Equal("Ask", Assert.IsType<RedirectToActionResult>(await pat.Understand(
            draft.College, draft.CarText, draft.PermitType, draft.Term, null, default, flowId: draft.FlowId)).ActionName);
        draft.FlowId = Guid.Empty;
        Assert.Equal("Ask", Assert.IsType<RedirectToActionResult>(await maria.Review(draft, default)).ActionName);
        Assert.Equal(1, extractor.Calls);
        Assert.Empty(db.Vehicles); Assert.Empty(db.PermitApplications);
    }

    [Fact]
    public async Task AiDoor_ForeignApplicationId_IsNotClonedIntoCurrentPerson()
    {
        var w = NewWorld(); using var connection = w.Connection; using var db = w.Db;
        var maria = NewController(w);
        var draft = await UnderstandAsync(maria, carText: "blue Honda Civic, plate REV1234");
        await maria.Review(draft, default);
        var original = Assert.Single(db.PermitApplications);
        var pat = NewController(w, "pat");
        var patDraft = await UnderstandAsync(pat, carText: "red Honda Civic, plate PAT1234");
        patDraft.ApplicationId = original.Id;
        Assert.IsType<NotFoundResult>(await pat.Review(patDraft, default));
        Assert.Single(db.PermitApplications);
        Assert.DoesNotContain(db.Vehicles, vehicle => vehicle.StudentId == "pat");
    }

    [Fact]
    public async Task AiDoor_StartedOrder_ReturnsToCheckoutBeforeChangingSharedVehicle()
    {
        var w = NewWorld(); using var connection = w.Connection; using var db = w.Db;
        var controller = NewController(w);
        var draft = await UnderstandAsync(controller, carText: "blue Honda Civic, plate REV1234");
        await controller.Review(draft, default);
        var app = Assert.Single(db.PermitApplications);
        app.PayPalOrderId = "SANDBOX-SAVED-ORDER";
        await db.SaveChangesAsync();
        draft.CarModel = "Changed"; draft.PermitType = PermitTypeCode.Daily;
        var result = Assert.IsType<RedirectResult>(await controller.Review(draft, default));
        Assert.Equal($"/Checkout/{app.Id}", result.Url);
        Assert.Equal("Civic", app.Vehicle!.Model);
        Assert.Equal(3000, app.PriceCents);
        Assert.Equal("SANDBOX-SAVED-ORDER", app.PayPalOrderId);
        Assert.Single(db.PermitApplications);
    }

    private static async Task<PermitDraftViewModel> UnderstandAsync(
        PermitsController controller,
        string college = "Fresno City College",
        string carText = "blue Honda Civic",
        PermitTypeCode type = PermitTypeCode.Term,
        TermKind term = TermKind.Fall)
    {
        var flowId = await BeginFlowAsync(controller, college, type, term);
        var result = Assert.IsType<RedirectToActionResult>(await controller.Understand(
            college, carText, type, term, applicationId: null, default, flowId: flowId));
        Assert.Equal("Understood", result.ActionName);
        var view = Assert.IsType<ViewResult>(await controller.Understood(flowId, default));
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

        var held = await ReviewDraftAsync(controller, draft);

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

        var posted = await ReviewDraftAsync(controller, draft);

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

        var posted = await ReviewDraftAsync(controller, draft);

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

        var posted = await ReviewDraftAsync(controller, draft);
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
