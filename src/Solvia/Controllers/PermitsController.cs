using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Solvia.Data;
using Solvia.Models;
using Solvia.Models.Enums;
using Solvia.Models.ViewModels;
using Solvia.Services;

namespace Solvia.Controllers;

/// <summary>
/// The Maria student flow (BUILD-PLAN T2), screen by screen:
///
///   Apply     — pick your college (the 10 campuses from IFeeCatalog,
///               each with its real published price — or the honest
///               "Price not published — ask your college").
///   Questions — 3 questions ONLY: your college (prefilled), your car
///               (one free-text box, read by IAiExtractor), your term
///               (big tap choices).
///   Understand— what the AI understood, in plain words, EDITABLE:
///               the extraction only pre-fills the car fields (plate
///               included); she can type or fix any of them right
///               there, with a "Fix it" path back to Questions. This
///               screen is THE GATE: the flow does not advance until
///               the plate is non-blank (client nicety + server check).
///   Review    — submitting Understood SAVES the draft application
///               (vehicle + application rows, payment still Pending)
///               and Review/Confirm then carry ONLY its id — no more
///               hidden-field relay of the car facts. College, car,
///               term, dates, and the EXACT total from the fee
///               catalog. A price the catalog does not publish BLOCKS
///               here: no invented price, no way to pay. A blocked
///               day pass says so plainly and offers the term permit
///               as a one-tap alternative.
///   Confirm   — takes the draft id, recomputes the price on the
///               server from the catalog (never a posted price), and
///               hands off to T4's /Checkout. It NEVER bounces back to
///               Questions: a residual problem (e.g. a blank plate on
///               an old draft) returns the Review screen with a big
///               error banner.
///   My        — her permits.   Receipt — the big live-permit screen.
///
/// The signed-in student is the seeded fictional Maria (server-set
/// StudentId via the demo cookie; see DemoControllerBase).
/// </summary>
public sealed class PermitsController : DemoControllerBase
{
    private static readonly Regex DayPassMoney = new(
        @"\$(\d{1,3}(?:,\d{3})*(?:\.\d{1,2})?|\d+(?:\.\d{1,2})?)",
        RegexOptions.Compiled);

    private readonly SolviaDbContext _db;
    private readonly IFeeCatalog _catalog;
    private readonly IAiExtractor _extractor;
    private readonly ITermDatesService _termDates;
    private readonly ICollegeSearch _search;
    private readonly PermitPdfService _permitPdf;

    public PermitsController(
        SolviaDbContext db,
        IFeeCatalog catalog,
        IAiExtractor extractor,
        ITermDatesService termDates,
        ICollegeSearch search,
        PermitPdfService permitPdf)
    {
        _db = db;
        _catalog = catalog;
        _extractor = extractor;
        _termDates = termDates;
        _search = search;
        _permitPdf = permitPdf;
    }

    // ------------------------------------------------------------------
    // 1. Pick your college
    // ------------------------------------------------------------------

    [HttpGet]
    public async Task<IActionResult> Apply(string? q, CancellationToken cancellationToken)
    {
        // With a query, the T8 search seam (ICollegeSearch — built-in
        // match by default, local Elastic index when configured)
        // ranks the campuses; without one, the full catalog list
        // shows in catalog order, exactly as before. Prices and
        // labels always come from the catalog rows either way.
        IReadOnlyList<CampusFee> fees;
        string? servedBy = null;
        if (string.IsNullOrWhiteSpace(q))
        {
            fees = _catalog.All();
        }
        else
        {
            var result = await _search.SearchAsync(q, cancellationToken);
            fees = result.Campuses;
            servedBy = result.ServedBy;
        }

        var campuses = fees.Select(fee =>
        {
            var cents = _catalog.SemesterPriceCents(fee, TermKind.Fall);
            var label = fee.IsFree
                ? "$0 — FREE"
                : cents is not null
                    ? $"{Money(cents.Value)} per term"
                    : "Price not published — ask your college";
            return new CampusPickItem(fee.Campus, label, fee.HasPublishedSemesterPrice);
        }).ToList();

        return View(new CollegePickViewModel
        {
            Campuses = campuses,
            Query = q,
            SearchServedBy = servedBy,
        });
    }

    // ------------------------------------------------------------------
    // 2. Three questions only
    // ------------------------------------------------------------------

    [HttpGet]
    public IActionResult Questions(string? college)
    {
        var fee = college is null ? null : _catalog.Find(college);
        if (fee is null)
        {
            return RedirectToAction(nameof(Apply));
        }

        return View(new QuestionsViewModel
        {
            College = fee.Campus,
            CarText = TempData["CarText"] as string,
            Notice = TempData["Notice"] as string,
        });
    }

    /// <summary>The "Fix it" path back: re-render the questions, answers kept.</summary>
    [HttpPost]
    [ActionName("Questions")]
    [ValidateAntiForgeryToken]
    public IActionResult QuestionsBack(PermitDraftViewModel draft)
    {
        var fee = _catalog.Find(draft.College);
        if (fee is null)
        {
            return RedirectToAction(nameof(Apply));
        }

        return View(new QuestionsViewModel
        {
            ApplicationId = draft.ApplicationId,
            College = fee.Campus,
            CarText = draft.CarText,
            PermitType = draft.PermitType,
            Term = draft.Term,
        });
    }

    // ------------------------------------------------------------------
    // 3. What the AI understood
    // ------------------------------------------------------------------

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Understand(
        string college, string? carText,
        PermitTypeCode permitType, TermKind term,
        Guid? applicationId,
        CancellationToken cancellationToken)
    {
        var fee = _catalog.Find(college);
        if (fee is null)
        {
            return RedirectToAction(nameof(Apply));
        }

        // Her car, in her own words, through the AI extractor. The
        // extractor can only return catalog colleges and car facts —
        // never a price. Her tap choices (type + term) and the college
        // she picked stay exactly as she set them. The extraction is
        // a PRE-FILL only: the Understood screen's fields are editable
        // and are what actually gets saved — a value the reader missed
        // (the plate above all) is typed there, not lost.
        var result = await _extractor.ExtractAsync(carText ?? "", cancellationToken);

        var draft = new PermitDraftViewModel
        {
            ApplicationId = applicationId,
            College = fee.Campus,
            CarText = carText,
            Plate = result.Plate,
            CarMake = result.CarMake,
            CarModel = result.CarModel,
            CarColor = result.CarColor,
            CarState = result.CarState,
            PermitType = permitType,
            Term = term,
            Source = result.Source == ExtractionSource.Ollama ? "ollama" : "fallback",
        };

        return View("Understood", draft);
    }

    // ------------------------------------------------------------------
    // 4. Review — the exact total, before any payment
    // ------------------------------------------------------------------

    /// <summary>
    /// The Understood screen's continue button lands here with the
    /// (possibly hand-corrected) draft. THE GATE: a blank plate goes
    /// straight back to the Understood screen with its big prompt —
    /// the flow never advances past Understood without a plate, and
    /// it never silently restarts at Questions. With a plate, the
    /// draft is SAVED (vehicle + application, payment Pending) and
    /// Review is reached by id alone.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(
        PermitDraftViewModel draft, CancellationToken cancellationToken)
    {
        var fee = _catalog.Find(draft.College);
        if (fee is null)
        {
            return RedirectToAction(nameof(Apply));
        }

        draft.College = fee.Campus;

        if (string.IsNullOrWhiteSpace(draft.Plate))
        {
            draft.PlateMissing = true;
            return View("Understood", draft);
        }

        var student = await CurrentStudentAsync(_db, cancellationToken);
        var application = await SaveDraftAsync(draft, fee, student, cancellationToken);

        return application.ReceiptNumber is not null
            ? RedirectToAction(nameof(Receipt), new { id = application.Id })
            : RedirectToAction(nameof(Review), new { id = application.Id });
    }

    /// <summary>The Review screen itself, rendered from the saved draft.</summary>
    [HttpGet]
    public async Task<IActionResult> Review(Guid id, CancellationToken cancellationToken)
    {
        var application = await _db.PermitApplications
            .Include(a => a.Vehicle)
            .Include(a => a.PermitType)
            .SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (application is null)
        {
            return NotFound();
        }

        var fee = _catalog.Find(application.Campus);
        if (fee is null)
        {
            return NotFound();
        }

        var draft = DraftFromApplication(application);
        return View(BuildReview(draft, fee, application.Id));
    }

    /// <summary>
    /// The blocked day-pass screen's one-tap alternative: switch the
    /// saved draft to another permit type (the term permit) and land
    /// back on its Review — same draft, same car, no re-typing.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SwitchType(
        Guid id, PermitTypeCode permitType, CancellationToken cancellationToken)
    {
        var application = await _db.PermitApplications
            .Include(a => a.Vehicle)
            .Include(a => a.PermitType)
            .SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (application is null)
        {
            return NotFound();
        }

        if (application.ReceiptNumber is not null)
        {
            return RedirectToAction(nameof(Receipt), new { id = application.Id });
        }

        var fee = _catalog.Find(application.Campus);
        if (fee is null)
        {
            return NotFound();
        }

        var type = await _db.PermitTypes
            .SingleAsync(t => t.Code == permitType && t.Active, cancellationToken);
        application.PermitTypeId = type.Id;

        var draft = DraftFromApplication(application);
        draft.PermitType = permitType;
        var review = BuildReview(draft, fee, application.Id);
        application.StartDate = review.StartDate;
        application.EndDate = review.EndDate;
        // Placeholder 0 only while the new type is ALSO blocked;
        // Confirm recomputes from the catalog before any money moves.
        application.PriceCents = review.PriceCents ?? 0;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The same permit in the new type already exists for her —
            // send her to that one's Review instead of duplicating it.
            var other = await _db.PermitApplications
                .Include(a => a.Vehicle)
                .Where(a => a.StudentId == application.StudentId &&
                            a.PermitTypeId == type.Id &&
                            a.StartDate == review.StartDate &&
                            a.EndDate == review.EndDate)
                .FirstOrDefaultAsync(
                    a => a.Vehicle!.Plate == application.Vehicle!.Plate,
                    cancellationToken);
            if (other is not null && other.Id != application.Id)
            {
                return other.ReceiptNumber is not null
                    ? RedirectToAction(nameof(Receipt), new { id = other.Id })
                    : RedirectToAction(nameof(Review), new { id = other.Id });
            }

            throw;
        }

        return RedirectToAction(nameof(Review), new { id = application.Id });
    }

    // ------------------------------------------------------------------
    // 5. Confirm — re-price the saved draft, hand off to T4's Checkout
    // ------------------------------------------------------------------

    /// <summary>
    /// Takes ONLY the draft id. The price is recomputed HERE, on the
    /// server, from the fee catalog — never taken from anything the
    /// browser sent. Problems NEVER bounce back to Questions: a blocked
    /// price or a still-blank plate returns THIS Review screen with
    /// the honest block / a big error banner.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(Guid id, CancellationToken cancellationToken)
    {
        var application = await _db.PermitApplications
            .Include(a => a.Vehicle)
            .Include(a => a.PermitType)
            .SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (application is null)
        {
            return NotFound();
        }

        if (application.ReceiptNumber is not null)
        {
            return RedirectToAction(nameof(Receipt), new { id = application.Id });
        }

        var fee = _catalog.Find(application.Campus);
        if (fee is null)
        {
            return NotFound();
        }

        var draft = DraftFromApplication(application);
        var review = BuildReview(draft, fee, application.Id);
        if (review.Blocked)
        {
            // No readable price: the honest block screen, no checkout.
            return View("Review", review);
        }

        if (string.IsNullOrWhiteSpace(application.Vehicle?.Plate))
        {
            // Residual path only (the Understand gate stops new flow
            // drafts long before here) — Review + banner, never a
            // silent bounce back to Questions.
            return View("Review", BuildReview(draft, fee, application.Id,
                error: "We still need your car's plate number before you " +
                       "can pay. Start over and type it on the " +
                       "\"Did I get this right?\" screen."));
        }

        // Persist the freshly recomputed snapshot, then hand off.
        application.PriceCents = review.PriceCents!.Value;
        application.StartDate = review.StartDate;
        application.EndDate = review.EndDate;
        await _db.SaveChangesAsync(cancellationToken);

        // Hand off to T4: paid path goes to PayPal sandbox checkout;
        // a $0 permit confirms free on that same page. T2 never calls
        // PayPal itself.
        return Redirect($"/Checkout/{application.Id}");
    }

    // ------------------------------------------------------------------
    // Draft persistence — the Understand step saves; ids travel after
    // ------------------------------------------------------------------

    /// <summary>
    /// Saves (or updates) the draft application for the Understood
    /// screen's values: her vehicle row plus a PermitApplication with
    /// PaymentStatus Pending. The stored PriceCents is the server's own
    /// catalog computation at save time (0 placeholder only while the
    /// price is blocked/unreadable) — Confirm recomputes it from the
    /// catalog before any money moves, so a stored value is never
    /// trusted for charging.
    /// </summary>
    private async Task<PermitApplication> SaveDraftAsync(
        PermitDraftViewModel draft, CampusFee fee, Student student,
        CancellationToken cancellationToken)
    {
        var permitType = await _db.PermitTypes
            .SingleAsync(t => t.Code == draft.PermitType && t.Active, cancellationToken);
        var pricing = BuildReview(draft, fee, draft.ApplicationId ?? Guid.Empty);
        var plate = draft.Plate!.Trim().ToUpperInvariant();

        PermitApplication? application = null;
        if (draft.ApplicationId is { } draftId)
        {
            application = await _db.PermitApplications
                .Include(a => a.Vehicle)
                .SingleOrDefaultAsync(
                    a => a.Id == draftId && a.StudentId == student.Id,
                    cancellationToken);
            if (application is not null && application.ReceiptNumber is not null)
            {
                // Already a live permit — never mutate it; a fresh
                // draft is created below (or the twin lookup finds it).
                application = null;
            }
        }

        if (application is null)
        {
            // The same permit already started under another draft?
            // Reuse it — never sell (or draft) it twice.
            var twin = await _db.PermitApplications
                .Include(a => a.Vehicle)
                .Where(a => a.StudentId == student.Id &&
                            a.PermitTypeId == permitType.Id &&
                            a.StartDate == pricing.StartDate &&
                            a.EndDate == pricing.EndDate)
                .FirstOrDefaultAsync(a => a.Vehicle!.Plate == plate, cancellationToken);
            if (twin is not null)
            {
                return twin;
            }
        }

        // Her car: the draft's own vehicle when updating, else reuse
        // the row if this plate is already hers, else a new row.
        var vehicle = application?.Vehicle;
        if (vehicle is null)
        {
            vehicle = await _db.Vehicles.FirstOrDefaultAsync(
                v => v.StudentId == student.Id && v.Plate == plate, cancellationToken);
        }

        if (vehicle is null)
        {
            vehicle = new Vehicle
            {
                Id = Guid.NewGuid(),
                StudentId = student.Id,
            };
            _db.Vehicles.Add(vehicle);
        }

        vehicle.Plate = plate;
        vehicle.State = ParseState(draft.CarState);
        vehicle.Color = ParseColor(draft.CarColor);
        vehicle.Make = NullIfBlank(draft.CarMake);
        vehicle.Model = NullIfBlank(draft.CarModel);

        if (application is null)
        {
            application = new PermitApplication
            {
                Id = Guid.NewGuid(),
                StudentId = student.Id,
                SubmittedAtUtc = DateTime.UtcNow,
            };
            _db.PermitApplications.Add(application);
        }

        application.Campus = fee.Campus;
        application.VehicleId = vehicle.Id;
        application.PermitTypeId = permitType.Id;
        application.Term = draft.Term;
        application.Source = draft.Source;
        application.StartDate = pricing.StartDate;
        application.EndDate = pricing.EndDate;
        application.Status = ApplicationStatus.Pending;
        application.PriceCents = pricing.PriceCents ?? 0;
        if (application.PaymentStatus is not (PaymentStatus.Paid or PaymentStatus.NotRequired))
        {
            application.PaymentStatus = PaymentStatus.Pending;
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The composite unique index caught a race with a twin
            // draft — detach our attempt and return the one that won.
            _db.Entry(application).State = EntityState.Detached;
            if (vehicle.Id != Guid.Empty && _db.Entry(vehicle).State == EntityState.Added)
            {
                _db.Entry(vehicle).State = EntityState.Detached;
            }

            var winner = await _db.PermitApplications
                .Include(a => a.Vehicle)
                .Where(a => a.StudentId == student.Id &&
                            a.PermitTypeId == permitType.Id &&
                            a.StartDate == pricing.StartDate &&
                            a.EndDate == pricing.EndDate)
                .FirstAsync(a => a.Vehicle!.Plate == plate, cancellationToken);
            return winner;
        }

        return application;
    }

    /// <summary>Rebuilds the flow draft from a saved application + its vehicle.</summary>
    private static PermitDraftViewModel DraftFromApplication(PermitApplication application)
    {
        var vehicle = application.Vehicle;
        return new PermitDraftViewModel
        {
            ApplicationId = application.Id,
            College = application.Campus,
            Plate = vehicle?.Plate,
            CarMake = vehicle?.Make,
            CarModel = vehicle?.Model,
            CarColor = vehicle?.Color.ToString(),
            CarState = vehicle?.State.ToString(),
            PermitType = application.PermitType?.Code ?? PermitTypeCode.Term,
            Term = application.Term,
            Source = application.Source,
        };
    }

    // ------------------------------------------------------------------
    // 6. My permits + the big receipt
    // ------------------------------------------------------------------

    [HttpGet]
    public async Task<IActionResult> My(CancellationToken cancellationToken)
    {
        var student = await CurrentStudentAsync(_db, cancellationToken);
        var applications = await _db.PermitApplications
            .Include(a => a.PermitType)
            .Where(a => a.StudentId == student.Id)
            .OrderByDescending(a => a.SubmittedAtUtc)
            .ToListAsync(cancellationToken);

        var items = applications.Select(a => new MyPermitItem(
            a.Id,
            a.Campus,
            a.PermitType?.Name ?? "Parking permit",
            a.StartDate,
            a.EndDate,
            a.PriceCents == 0 ? "$0 — FREE" : Money(a.PriceCents),
            a.PaymentStatus,
            a.ReceiptNumber)).ToList();

        return View(new MyPermitsViewModel
        {
            StudentName = student.DisplayName,
            Permits = items,
        });
    }

    [HttpGet]
    public async Task<IActionResult> Receipt(Guid id, CancellationToken cancellationToken)
    {
        var application = await _db.PermitApplications
            .Include(a => a.Vehicle)
            .Include(a => a.PermitType)
            .SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (application is null)
        {
            return NotFound();
        }

        var vehicle = application.Vehicle;
        var carParts = new[]
        {
            vehicle?.Color.ToString(),
            vehicle?.Make,
            vehicle?.Model,
        }.Where(p => !string.IsNullOrWhiteSpace(p));
        var carLine = string.Join(" ", carParts);
        if (vehicle is not null)
        {
            carLine = $"{carLine} — plate {vehicle.Plate}".Trim(' ', '—');
        }

        var isFree = application.PaymentStatus == PaymentStatus.NotRequired;
        var isLive = application.PaymentStatus is
            PaymentStatus.Paid or PaymentStatus.NotRequired;

        return View(new ReceiptViewModel
        {
            Id = application.Id,
            ReceiptNumber = application.ReceiptNumber,
            College = application.Campus,
            CarLine = carLine.Length > 0 ? carLine : "Your car",
            TypeName = application.PermitType?.Name ?? "Parking permit",
            StartDate = application.StartDate,
            EndDate = application.EndDate,
            TotalDisplay = application.PriceCents == 0
                ? "$0 — FREE"
                : Money(application.PriceCents),
            IsLive = isLive,
            IsFree = isFree,
            PayPalOrderId = application.PayPalOrderId,
            PayPalCaptureId = application.PayPalCaptureId,
            VerifyUrl = application.VerifyToken is null
                ? null
                : $"/verify?t={application.VerifyToken}",
        });
    }

    /// <summary>
    /// The printable permit PDF (PermitPdfService). Served ONLY for
    /// a live permit — Paid, or NotRequired for a free permit — the
    /// same gate as the reference app's Receipt(id): any other state
    /// goes back to the receipt screen (which says plainly that the
    /// permit is not live and offers the finish-paying path), never
    /// a PDF. Unknown id → 404.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> PermitPdf(Guid id, CancellationToken cancellationToken)
    {
        var application = await _db.PermitApplications
            .Include(a => a.Student)
            .Include(a => a.Vehicle)
            .Include(a => a.PermitType)
            .SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (application is null)
        {
            return NotFound();
        }

        if (application.PaymentStatus is not (PaymentStatus.Paid or PaymentStatus.NotRequired))
        {
            return RedirectToAction(nameof(Receipt), new { id = application.Id });
        }

        var pdf = _permitPdf.BuildPermitPdf(application);
        var fileName = $"Solvia-Permit-{application.ReceiptNumber ?? application.Id.ToString()}.pdf";
        return File(pdf, "application/pdf", fileName);
    }

    // ------------------------------------------------------------------
    // Pricing + dates — the server's single source of truth
    // ------------------------------------------------------------------

    private ReviewViewModel BuildReview(
        PermitDraftViewModel draft, CampusFee fee, Guid applicationId,
        string? error = null)
    {
        int? price;
        DateOnly start;
        DateOnly end;

        if (draft.PermitType == PermitTypeCode.Daily)
        {
            price = DayPassPriceCents(fee);
            var today = DateOnly.FromDateTime(DateTime.Now);
            start = today;
            end = today;
        }
        else
        {
            price = _catalog.SemesterPriceCents(fee, draft.Term);
            var dates = _termDates.GetTermDates(fee.Campus, draft.Term, YearFor(draft.Term));
            if (dates is not null)
            {
                start = dates.TermStart;
                end = dates.PermitEndDate;
            }
            else
            {
                // The catalog knows this campus, so the seam should too;
                // fall back to a plain 120-day window from today.
                start = DateOnly.FromDateTime(DateTime.Now);
                end = start.AddDays(120);
            }
        }

        // A blocked DAY PASS offers the same college's term permit as
        // the one-tap way forward — priced from the same catalog row,
        // never invented.
        int? termAlternative = null;
        if (price is null && draft.PermitType == PermitTypeCode.Daily)
        {
            termAlternative = _catalog.SemesterPriceCents(fee, draft.Term);
        }

        return new ReviewViewModel
        {
            ApplicationId = applicationId,
            Draft = draft,
            PriceCents = price,
            TotalDisplay = price is null
                ? "Price not published"
                : price == 0
                    ? "$0 — FREE"
                    : Money(price.Value),
            StartDate = start,
            EndDate = end,
            Error = error,
            TermAlternativePriceCents = termAlternative,
            TermAlternativeDisplay = termAlternative is null
                ? null
                : termAlternative == 0
                    ? "$0 — FREE"
                    : Money(termAlternative.Value),
        };
    }

    /// <summary>
    /// The campus's published day-pass price, in cents — the first
    /// dollar amount in its own published day-pass text ("$1.00
    /// (online)", "$5/day General; …"). Null when the campus does not
    /// publish one; 0 when it publishes FREE. Never invented.
    /// </summary>
    private static int? DayPassPriceCents(CampusFee fee)
    {
        var text = fee.DayPassText.Trim();
        if (text.Length == 0 ||
            text.StartsWith("not listed", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (text.StartsWith("FREE", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var match = DayPassMoney.Match(text);
        if (!match.Success)
        {
            return null;
        }

        var value = decimal.Parse(
            match.Groups[1].Value.Replace(",", ""),
            NumberStyles.Number,
            CultureInfo.InvariantCulture);
        return (int)Math.Round(value * 100m, MidpointRounding.AwayFromZero);
    }

    /// <summary>The academic year a term belongs to, from today's date.</summary>
    private static int YearFor(TermKind term)
    {
        var now = DateTime.Now;
        return term switch
        {
            // A Spring picked in the fall belongs to next year.
            TermKind.Spring when now.Month >= 8 => now.Year + 1,
            _ => now.Year,
        };
    }

    private static VehicleColor ParseColor(string? color) =>
        Enum.TryParse<VehicleColor>(color, ignoreCase: true, out var parsed)
            ? parsed
            : VehicleColor.Other;

    private static UsState ParseState(string? state) =>
        Enum.TryParse<UsState>(state, ignoreCase: true, out var parsed)
            ? parsed
            : UsState.CA;

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Money(int cents) =>
        "$" + (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
}
