using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Solvia.Controllers;
using Solvia.Data;
using Solvia.Models;
using Solvia.Models.Enums;
using Solvia.Models.ViewModels;
using Solvia.Services;

namespace Solvia.Tests;

/// <summary>
/// The flow-fix tests (the "Next button stalls before PayPal" bug).
/// Root cause: the plate was harvested ONLY by the extractor's narrow
/// parser; when it missed, the draft sailed through Understood, showed
/// "Pay with PayPal" on Review, and Confirm silently bounced back to
/// Questions. The fix mirrors CarPermitApp's step STRUCTURE (fresh
/// code): the Understood screen is the gate with EDITABLE fields
/// (extraction is pre-fill only), the draft is PERSISTED at that step
/// and only its id travels on, and Confirm never bounces backwards —
/// residual problems return Review with a big error banner.
/// </summary>
public sealed class FlowFixTests : IDisposable
{
    private const string Fresno = "Fresno City College";

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<SolviaDbContext> _options;
    private readonly FeeCatalogService _catalog = new(FindFeesCsv());

    public FlowFixTests()
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

    private PermitsController NewController(SolviaDbContext db, IFeeCatalog? catalog = null)
    {
        catalog ??= _catalog;
        var controller = new PermitsController(
            db,
            catalog,
            new FallbackAiExtractor(new FallbackExtractor(catalog)),
            new StubTermDatesService(),
            new BuiltinCollegeSearch(catalog),
            new PermitPdfService(new HttpContextAccessor()));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };
        return controller;
    }

    // ------------------------------------------------------------------
    // The previously-stalling case, end to end
    // ------------------------------------------------------------------

    [Fact]
    public async Task NoPlate_UnderstoodGateBlocks_TypedPlate_ReviewsExact30_ConfirmReachesCheckout()
    {
        using var db = NewDb();
        var controller = NewController(db);

        // Maria's words carry NO plate — the extractor cannot invent one.
        var understood = Assert.IsType<ViewResult>(await controller.Understand(
            Fresno, "blue Honda Civic", PermitTypeCode.Term, TermKind.Fall, null, default));
        Assert.Equal("Understood", understood.ViewName);
        var draft = Assert.IsType<PermitDraftViewModel>(understood.Model);
        Assert.Null(draft.Plate);
        Assert.Equal("Honda", draft.CarMake);

        // She presses "Yes — show my total" with the plate still blank:
        // the gate holds — back on Understood with the big prompt,
        // NOT bounced to Questions, NOT advanced to Review.
        var gated = Assert.IsType<ViewResult>(
            await controller.Review(draft, default));
        Assert.Equal("Understood", gated.ViewName);
        var gatedDraft = Assert.IsType<PermitDraftViewModel>(gated.Model);
        Assert.True(gatedDraft.PlateMissing);

        // She types the plate into the Understood screen's Plate box.
        gatedDraft.Plate = "8XYZ123";
        var saved = Assert.IsType<RedirectToActionResult>(
            await controller.Review(gatedDraft, default));
        Assert.Equal("Review", saved.ActionName);
        var id = Assert.IsType<Guid>(saved.RouteValues!["id"]);

        // The draft was PERSISTED at the Understand step: server-priced
        // from the catalog, payment still Pending.
        var stored = await db.PermitApplications
            .Include(a => a.Vehicle)
            .SingleAsync(a => a.Id == id);
        Assert.Equal(3000, stored.PriceCents);
        Assert.Equal(PaymentStatus.Pending, stored.PaymentStatus);
        Assert.Equal("8XYZ123", stored.Vehicle!.Plate);
        Assert.Equal(Fresno, stored.Campus);

        // Review (by id alone) shows the exact total.
        var reviewResult = Assert.IsType<ViewResult>(
            await controller.Review(id, default));
        var review = Assert.IsType<ReviewViewModel>(reviewResult.Model);
        Assert.False(review.Blocked);
        Assert.Equal(3000, review.PriceCents);
        Assert.Equal("$30.00", review.TotalDisplay);
        Assert.Equal(id, review.ApplicationId);

        // Re-submitting the same draft (the Fix-it loop) UPDATES it —
        // same id, still exactly one application for Maria.
        gatedDraft.ApplicationId = id;
        var resaved = Assert.IsType<RedirectToActionResult>(
            await controller.Review(gatedDraft, default));
        Assert.Equal(id, Assert.IsType<Guid>(resaved.RouteValues!["id"]));
        Assert.Equal(1, await db.PermitApplications.CountAsync(a => a.StudentId == "maria"));

        // Confirm (id only) hands off to the checkout page.
        var confirm = Assert.IsType<RedirectResult>(
            await controller.Confirm(id, default));
        Assert.Equal($"/Checkout/{id}", confirm.Url);
    }

    // ------------------------------------------------------------------
    // The spaced plate the old flow lost
    // ------------------------------------------------------------------

    [Fact]
    public async Task Understand_SpacedPlateAfterKeyword_StillExtracts()
    {
        using var db = NewDb();
        var controller = NewController(db);

        var result = Assert.IsType<ViewResult>(await controller.Understand(
            Fresno, "blue Honda Civic, plate 8XYZ 123",
            PermitTypeCode.Term, TermKind.Fall, null, default));
        var draft = Assert.IsType<PermitDraftViewModel>(result.Model);
        Assert.Equal("8XYZ123", draft.Plate);
        Assert.False(draft.PlateMissing);
    }

    // ------------------------------------------------------------------
    // Confirm never bounces backwards
    // ------------------------------------------------------------------

    [Fact]
    public async Task Confirm_BlankPlateOnSavedDraft_ReturnsReviewWithError_NeverQuestions()
    {
        using var db = NewDb();

        // A residual draft whose vehicle plate is blank (the shape the
        // old hidden-field relay could strand) — seeded directly
        // because the new Understand gate can no longer produce one.
        var vehicle = new Vehicle
        {
            Id = Guid.NewGuid(),
            StudentId = "maria",
            Plate = "",
            State = UsState.CA,
            Color = VehicleColor.Blue,
            Make = "Honda",
            Model = "Civic",
        };
        var application = new PermitApplication
        {
            Id = Guid.NewGuid(),
            StudentId = "maria",
            Campus = Fresno,
            VehicleId = vehicle.Id,
            PermitTypeId = 2,
            Term = TermKind.Fall,
            StartDate = new DateOnly(2026, 8, 24),
            EndDate = new DateOnly(2026, 12, 22),
            Status = ApplicationStatus.Pending,
            PriceCents = 3000,
            PaymentStatus = PaymentStatus.Pending,
            SubmittedAtUtc = DateTime.UtcNow,
        };
        db.Vehicles.Add(vehicle);
        db.PermitApplications.Add(application);
        await db.SaveChangesAsync();

        var controller = NewController(db);
        var result = await controller.Confirm(application.Id, default);

        // Review + a big error banner — NOT a redirect to Questions.
        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Review", view.ViewName);
        var review = Assert.IsType<ReviewViewModel>(view.Model);
        Assert.False(review.Blocked);
        Assert.False(string.IsNullOrWhiteSpace(review.Error));
        Assert.Contains("plate", review.Error, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------
    // Blocked day pass: honest words + the one-tap term alternative
    // ------------------------------------------------------------------

    [Fact]
    public async Task DayPassPriceUnreadable_BlockedReviewOffersTerm_SwitchTypeLandsOnTermReview()
    {
        // A campus whose day-pass text carries no readable price (the
        // parse-miss shape) but whose term price IS published.
        var fee = new CampusFee(
            Campus: "Test College",
            StudentSemesterFeeText: "$30 Fall/Spring",
            OtherTermFeesText: "",
            DayPassText: "Ask at the parking office",
            MotorcycleText: "",
            FallSemesterCents: 3000,
            SpringSemesterCents: 3000,
            SummerSemesterCents: null,
            IsFree: false,
            SourceNote: "test stub");
        var catalog = new StubCatalog(fee);

        using var db = NewDb();
        var controller = NewController(db, catalog);

        var draft = new PermitDraftViewModel
        {
            College = "Test College",
            CarText = "blue Honda Civic, plate 8XYZ123",
            Plate = "8XYZ123",
            CarMake = "Honda",
            CarModel = "Civic",
            CarColor = "Blue",
            PermitType = PermitTypeCode.Daily,
            Term = TermKind.Fall,
            Source = "fallback",
        };
        var saved = Assert.IsType<RedirectToActionResult>(
            await controller.Review(draft, default));
        var id = Assert.IsType<Guid>(saved.RouteValues!["id"]);

        // The blocked Review names the day-pass problem and prices the
        // term alternative from the same catalog row.
        var blockedResult = Assert.IsType<ViewResult>(
            await controller.Review(id, default));
        var blocked = Assert.IsType<ReviewViewModel>(blockedResult.Model);
        Assert.True(blocked.Blocked);
        Assert.True(blocked.IsDayPassBlock);
        Assert.True(blocked.HasTermAlternative);
        Assert.Equal(3000, blocked.TermAlternativePriceCents);
        Assert.Equal("$30.00", blocked.TermAlternativeDisplay);

        // One tap: same draft, term permit, exact total on Review.
        var switched = Assert.IsType<RedirectToActionResult>(
            await controller.SwitchType(id, PermitTypeCode.Term, default));
        Assert.Equal("Review", switched.ActionName);
        Assert.Equal(id, Assert.IsType<Guid>(switched.RouteValues!["id"]));

        var termResult = Assert.IsType<ViewResult>(
            await controller.Review(id, default));
        var term = Assert.IsType<ReviewViewModel>(termResult.Model);
        Assert.False(term.Blocked);
        Assert.Equal(3000, term.PriceCents);
        Assert.Equal("$30.00", term.TotalDisplay);
        Assert.Equal(PermitTypeCode.Term, term.Draft.PermitType);
    }

    // ------------------------------------------------------------------
    // Test doubles
    // ------------------------------------------------------------------

    /// <summary>The deterministic extractor behind the IAiExtractor seam.</summary>
    private sealed class FallbackAiExtractor(FallbackExtractor inner) : IAiExtractor
    {
        public Task<ExtractionResult> ExtractAsync(
            string studentText, CancellationToken cancellationToken = default) =>
            Task.FromResult(inner.Extract(studentText));
    }

    /// <summary>No term-dates rows in tests: BuildReview's plain
    /// 120-day fallback window applies, deterministically.</summary>
    private sealed class StubTermDatesService : ITermDatesService
    {
        public TermDates? GetTermDates(string campusName, TermKind term, int year) => null;
    }

    /// <summary>One-campus catalog for the day-pass parse-miss shape.</summary>
    private sealed class StubCatalog(CampusFee fee) : IFeeCatalog
    {
        public CampusFee? Find(string campusName) =>
            string.Equals(fee.Campus, campusName.Trim(), StringComparison.OrdinalIgnoreCase)
                ? fee
                : null;

        public IReadOnlyList<CampusFee> All() => [fee];

        public int? SemesterPriceCents(CampusFee f, TermKind term) => term switch
        {
            TermKind.Fall => f.FallSemesterCents ?? f.SpringSemesterCents,
            TermKind.Spring => f.SpringSemesterCents ?? f.FallSemesterCents,
            TermKind.Summer => f.SummerSemesterCents,
            _ => null,
        };
    }

    private static string FindFeesCsv()
    {
        var starts = new[]
        {
            Path.GetDirectoryName(typeof(FlowFixTests).Assembly.Location),
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
