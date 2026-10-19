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
///   Apply     — pick your college (the 25 campuses from IFeeCatalog,
///               each with its real published price — or the honest
///               "Price not published — ask your college").
///   Questions — 3 questions ONLY: your college (prefilled), your car
///               (one free-text box, read by IAiExtractor), your term
///               (big tap choices).
///   Understand— what the AI understood, in plain words, with a
///               "Fix it" path back.
///   Review    — college, car, term, dates, and the EXACT total from
///               the fee catalog. A college that does not publish the
///               price BLOCKS here: no invented price, no way to pay.
///   Confirm   — creates the vehicle + application (price recomputed
///               on the server from the catalog — the browser never
///               sets a price) and hands off to T4's /Checkout.
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

    public PermitsController(
        SolviaDbContext db,
        IFeeCatalog catalog,
        IAiExtractor extractor,
        ITermDatesService termDates,
        ICollegeSearch search)
    {
        _db = db;
        _catalog = catalog;
        _extractor = extractor;
        _termDates = termDates;
        _search = search;
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
        // she picked stay exactly as she set them.
        var result = await _extractor.ExtractAsync(carText ?? "", cancellationToken);

        var draft = new PermitDraftViewModel
        {
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Review(PermitDraftViewModel draft)
    {
        var fee = _catalog.Find(draft.College);
        if (fee is null)
        {
            return RedirectToAction(nameof(Apply));
        }

        draft.College = fee.Campus;
        return View(BuildReview(draft, fee));
    }

    // ------------------------------------------------------------------
    // 5. Confirm — save it, then hand off to T4's Checkout
    // ------------------------------------------------------------------

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(
        PermitDraftViewModel draft, CancellationToken cancellationToken)
    {
        var fee = _catalog.Find(draft.College);
        if (fee is null)
        {
            return RedirectToAction(nameof(Apply));
        }

        draft.College = fee.Campus;

        // The price is recomputed HERE, on the server, from the fee
        // catalog — never taken from anything the browser sent.
        var review = BuildReview(draft, fee);
        if (review.Blocked)
        {
            // No published price: the honest block screen, no checkout.
            return View("Review", review);
        }

        if (string.IsNullOrWhiteSpace(draft.Plate))
        {
            TempData["Notice"] = "I still need your plate number — please add it.";
            TempData["CarText"] = draft.CarText;
            return RedirectToAction(nameof(Questions), new { college = fee.Campus });
        }

        var student = await CurrentStudentAsync(_db, cancellationToken);
        var permitType = await _db.PermitTypes
            .SingleAsync(t => t.Code == draft.PermitType && t.Active, cancellationToken);
        var plate = draft.Plate.Trim().ToUpperInvariant();

        // Same permit already started? Reuse it — never sell it twice.
        var existing = await _db.PermitApplications
            .Include(a => a.Vehicle)
            .Where(a => a.StudentId == student.Id &&
                        a.PermitTypeId == permitType.Id &&
                        a.StartDate == review.StartDate &&
                        a.EndDate == review.EndDate)
            .FirstOrDefaultAsync(a => a.Vehicle!.Plate == plate, cancellationToken);
        if (existing is not null)
        {
            return existing.ReceiptNumber is not null
                ? RedirectToAction(nameof(Receipt), new { id = existing.Id })
                : Redirect($"/Checkout/{existing.Id}");
        }

        // Her car: reuse the row if this plate is already hers.
        var vehicle = await _db.Vehicles.FirstOrDefaultAsync(
            v => v.StudentId == student.Id && v.Plate == plate, cancellationToken);
        if (vehicle is null)
        {
            vehicle = new Vehicle
            {
                Id = Guid.NewGuid(),
                StudentId = student.Id,
                Plate = plate,
                State = ParseState(draft.CarState),
                Color = ParseColor(draft.CarColor),
                Make = NullIfBlank(draft.CarMake),
                Model = NullIfBlank(draft.CarModel),
            };
            _db.Vehicles.Add(vehicle);
        }

        var application = new PermitApplication
        {
            Id = Guid.NewGuid(),
            StudentId = student.Id,
            Campus = fee.Campus,
            VehicleId = vehicle.Id,
            PermitTypeId = permitType.Id,
            StartDate = review.StartDate,
            EndDate = review.EndDate,
            Status = ApplicationStatus.Pending,
            PriceCents = review.PriceCents!.Value,
            PaymentStatus = PaymentStatus.Pending,
            SubmittedAtUtc = DateTime.UtcNow,
        };
        _db.PermitApplications.Add(application);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The composite unique index caught a double-submit of the
            // same permit — send her to the one that exists.
            return RedirectToAction(nameof(My));
        }

        // Hand off to T4: paid path goes to PayPal sandbox checkout;
        // a $0 permit confirms free on that same page. T2 never calls
        // PayPal itself.
        return Redirect($"/Checkout/{application.Id}");
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

    // ------------------------------------------------------------------
    // Pricing + dates — the server's single source of truth
    // ------------------------------------------------------------------

    private ReviewViewModel BuildReview(PermitDraftViewModel draft, CampusFee fee)
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

        return new ReviewViewModel
        {
            Draft = draft,
            PriceCents = price,
            TotalDisplay = price is null
                ? "Price not published"
                : price == 0
                    ? "$0 — FREE"
                    : Money(price.Value),
            StartDate = start,
            EndDate = end,
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
