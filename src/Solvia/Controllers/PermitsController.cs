using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Solvia.Data;
using Solvia.Models;
using Solvia.Models.Enums;
using Solvia.Models.ViewModels;
using Solvia.Services;

namespace Solvia.Controllers;

/// <summary>
/// The two doors (REDO-PLAN §2c/§3 R4). One application model, one
/// creation brain (ApplicationService.CreateAsync), two ways in:
///
/// FORM DOOR (the reference app's shape) — GET/POST /Permits/Apply:
/// Campus, Vehicle, Permit Type, Term, and Start Date on one form;
/// one "Submit Application" POST. Every failure re-renders the SAME
/// form with the lists repopulated and the problem on-screen.
/// Free → issued at the POST and on to My Permits; paid → straight
/// to /Checkout/{id}. There is NO Review step on this door.
///
/// AI DOOR (Solvia's) — /Permits/Ask → Questions → Understood →
/// Review → Confirm: pick a college, answer 3 questions only
/// (school / car / term), and the configured AI reader via Ollama
/// reads the car sentence. Unavailable or unusable answers fall back to rules.
/// The Understood screen is THE GATE — editable fields, the flow
/// does not advance without a plate — and submitting it persists
/// the draft through the same CreateAsync. Review is the trust
/// moment (exact total, from the catalog, before money moves);
/// Confirm posts the draft's id only and hands off to Checkout.
///
/// The demo person is the seeded fictional person from the demo
/// switcher (DemoControllerBase) — the owner is server-resolved on
/// every action, never trusted from a form.
/// </summary>
public sealed class PermitsController : DemoControllerBase
{
    /// <summary>Duvara's general invalid-selection error (item 16), in plain words.</summary>
    private const string InvalidSelection =
        "That selection is not valid — please pick again from the lists.";

    private readonly SolviaDbContext _db;
    private readonly IFeeCatalog _catalog;
    private readonly IAiExtractor _extractor;
    private readonly ITermDatesService _termDates;
    private readonly PricingService _pricing;
    private readonly UserCategoryResolver _categories;
    private readonly ApplicationService _applications;
    private readonly AiRequestLimiter _aiRequests;

    public PermitsController(
        SolviaDbContext db,
        IFeeCatalog catalog,
        IAiExtractor extractor,
        ITermDatesService termDates,
        PricingService pricing,
        UserCategoryResolver categories,
        ApplicationService applications,
        AiRequestLimiter? aiRequests = null)
    {
        _db = db;
        _catalog = catalog;
        _extractor = extractor;
        _termDates = termDates;
        _pricing = pricing;
        _categories = categories;
        _applications = applications;
        _aiRequests = aiRequests ?? new AiRequestLimiter();
    }

    // ==================================================================
    // FORM DOOR — Duvara's Apply
    // ==================================================================

    [HttpGet]
    public async Task<IActionResult> Apply(CancellationToken cancellationToken)
    {
        var student = await CurrentStudentAsync(_db, cancellationToken);
        var model = await BuildApplyModelAsync(student, cancellationToken);
        model.StartDate = Today();
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Apply(
        ApplyViewModel form, CancellationToken cancellationToken)
    {
        var student = await CurrentStudentAsync(_db, cancellationToken);
        var model = await BuildApplyModelAsync(student, cancellationToken);
        // Keep the applicant's picks; only the lists/identity refresh.
        model.Campus = form.Campus;
        model.VehicleId = form.VehicleId;
        model.PermitTypeId = form.PermitTypeId;
        model.Term = form.Term;
        model.StartDate = form.StartDate;

        var errors = model.Errors;

        // Campus (item 16: a bad pick is the general error).
        CampusFee? fee = null;
        if (string.IsNullOrWhiteSpace(form.Campus))
        {
            errors.Add("Please pick your college.");
        }
        else
        {
            fee = _catalog.Find(form.Campus);
            if (fee is null)
            {
                errors.Add(InvalidSelection);
            }
            else
            {
                model.Campus = fee.Campus;
            }
        }

        // Vehicle (items 2/16): no pick → "select a vehicle"; a pick
        // that is not this person's own → the general error — the
        // POST re-loads the vehicle, ids are never trusted.
        Vehicle? vehicle = null;
        if (form.VehicleId == Guid.Empty)
        {
            errors.Add("Please select a vehicle.");
        }
        else
        {
            vehicle = await _db.Vehicles.SingleOrDefaultAsync(
                v => v.Id == form.VehicleId && v.StudentId == student.Id,
                cancellationToken);
            if (vehicle is null)
            {
                errors.Add(InvalidSelection);
            }
        }

        // Permit type (items 3/16): no pick → "select a permit
        // type"; an inactive/unknown pick → the general error.
        PermitType? type = null;
        if (form.PermitTypeId == 0)
        {
            errors.Add("Please select a permit type.");
        }
        else
        {
            type = await _db.PermitTypes.SingleOrDefaultAsync(
                t => t.Id == form.PermitTypeId && t.Active, cancellationToken);
            if (type is null)
            {
                errors.Add(InvalidSelection);
            }
        }

        if (form.StartDate == default)
        {
            errors.Add("Please pick a start date.");
        }

        // Term window (§2c): a Term start must sit inside the chosen
        // term's window, and the error NAMES the window.
        if (fee is not null && type is not null &&
            type.Code == PermitTypeCode.Term && form.StartDate != default)
        {
            var dates = _termDates.GetTermDates(
                fee.Campus, form.Term, form.StartDate.Year);
            if (dates is not null &&
                (form.StartDate < dates.TermStart || form.StartDate > dates.TermEnd))
            {
                errors.Add(
                    $"{form.Term} runs {ShortDate(dates.TermStart)} – " +
                    $"{ShortDate(dates.TermEnd)} — pick a date inside it " +
                    "or change the term.");
            }
        }

        if (errors.Count > 0)
        {
            return View(model);
        }

        var result = await _applications.CreateAsync(new ApplicationInput(
            StudentId: student.Id,
            VehicleId: vehicle!.Id,
            Campus: fee!.Campus,
            TypeCode: type!.Code,
            Term: form.Term,
            StartDate: form.StartDate,
            Source: "form",
            PersistWhenPriceBlocked: false), cancellationToken);

        switch (result.Status)
        {
            case CreateApplicationStatus.Created:
            {
                var application = result.Application!;
                if (application.PriceCents > 0)
                {
                    // Paid outcome (item 18): straight to Checkout.
                    return Redirect($"/Checkout/{application.Id}");
                }

                // Free outcome (item 17): issued inside CreateAsync;
                // the Checkout page is never rendered for it.
                TempData["StatusMessage"] =
                    $"Permit {application.ReceiptNumber} is live — no payment " +
                    "was needed. It is under My permits now.";
                return Redirect("/Permits/My");
            }
            case CreateApplicationStatus.Duplicate:
                errors.Add(ApplicationService.DuplicateMessage);
                return View(model);
            case CreateApplicationStatus.PriceNotPublished:
                errors.Add(result.ErrorMessage!);
                return View(model);
            default:
                errors.Add(InvalidSelection);
                return View(model);
        }
    }

    /// <summary>The Apply form's identity + lists, fresh from the server.</summary>
    private async Task<ApplyViewModel> BuildApplyModelAsync(
        Student student, CancellationToken cancellationToken)
    {
        var vehicles = await _db.Vehicles
            .Where(v => v.StudentId == student.Id)
            .OrderBy(v => v.Plate)
            .ToListAsync(cancellationToken);
        var types = await _db.PermitTypes
            .Where(t => t.Active)
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken);

        return new ApplyViewModel
        {
            StudentName = student.DisplayName,
            CategoryLabel = _categories.Resolve(student).ToString(),
            Campuses = _catalog.All().Select(c => c.Campus).ToList(),
            Vehicles = vehicles.Select(v => new ApplyVehicleOption
            {
                Id = v.Id,
                Label = VehicleLabel(v),
            }).ToList(),
            Types = types.Select(t => new ApplyTypeOption
            {
                Id = t.Id,
                Name = t.Name,
                Code = t.Code,
            }).ToList(),
            NoVehicles = vehicles.Count == 0,
            NoTypes = types.Count == 0,
        };
    }

    /// <summary>"Plate · State · Make Model" (item 2's option label).</summary>
    private static string VehicleLabel(Vehicle vehicle) =>
        string.Join(" · ", new[]
        {
            vehicle.Plate,
            vehicle.State.ToString(),
            string.Join(" ", new[] { vehicle.Make, vehicle.Model }
                .Where(s => !string.IsNullOrWhiteSpace(s))),
        }.Where(s => !string.IsNullOrWhiteSpace(s)));

    // ==================================================================
    // AI DOOR — pick a college
    // ==================================================================

    [HttpGet]
    public async Task<IActionResult> Ask(CancellationToken cancellationToken)
    {
        var student = await CurrentStudentAsync(_db, cancellationToken);
        var campuses = _catalog.All().Select(fee =>
        {
            var cents = _catalog.SemesterPriceCents(fee, TermKind.Fall);
            var label = fee.IsFree
                ? "$0 — FREE"
                : cents is not null
                    ? $"{Money(cents.Value)} per term"
                    : "Price not published — ask your college";
            return new CampusPickItem
            {
                Name = fee.Campus,
                PriceLabel = label,
                HasPrice = fee.HasPublishedSemesterPrice,
            };
        }).ToList();

        return View(new AskViewModel
        {
            StudentName = student.DisplayName,
            Campuses = campuses,
        });
    }

    // ==================================================================
    // AI DOOR — 3 questions only
    // ==================================================================

    [HttpGet]
    public IActionResult Questions(string? college, PermitTypeCode permitType = PermitTypeCode.Term,
        TermKind term = TermKind.Fall, DateOnly? startDate = null)
    {
        var fee = college is null ? null : _catalog.Find(college);
        if (fee is null)
        {
            return RedirectToAction(nameof(Ask));
        }

        return View(new QuestionsViewModel
        {
            College = fee.Campus,
            PermitType = Enum.IsDefined(permitType) ? permitType : PermitTypeCode.Term,
            Term = Enum.IsDefined(term) ? term : TermKind.Fall,
            StartDate = startDate,
            CarText = TempData["CarText"] as string,
            Notice = TempData["Notice"] as string,
        });
    }

    /// <summary>The Fix-it path back: re-render the questions, answers kept.</summary>
    [HttpPost]
    [ActionName("Questions")]
    [ValidateAntiForgeryToken]
    public IActionResult QuestionsBack(PermitDraftViewModel draft)
    {
        var fee = _catalog.Find(draft.College);
        if (fee is null)
        {
            return RedirectToAction(nameof(Ask));
        }

        return View("Questions", new QuestionsViewModel
        {
            ApplicationId = draft.ApplicationId,
            College = fee.Campus,
            CarText = draft.CarText,
            PermitType = draft.PermitType,
            Term = draft.Term,
            StartDate = draft.StartDate,
        });
    }

    // ==================================================================
    // AI DOOR — what the AI understood (the gate screen)
    // ==================================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Understand(
        string college, string? carText,
        PermitTypeCode permitType, TermKind term,
        Guid? applicationId,
        CancellationToken cancellationToken, DateOnly? startDate = null)
    {
        var fee = _catalog.Find(college);
        if (fee is null)
        {
            return RedirectToAction(nameof(Ask));
        }

        if (carText?.Length > PermitDraftViewModel.MaxCarTextLength)
        {
            return QuestionsForLongDescription(new PermitDraftViewModel
            {
                ApplicationId = applicationId,
                College = fee.Campus,
                CarText = carText,
                PermitType = permitType,
                Term = term,
                StartDate = startDate,
            });
        }

        // The car, in the student's own words, through the AI
        // extractor. The extraction is a PRE-FILL only: the
        // Understood screen's fields are editable, and what is
        // submitted there is what gets saved — a value the reader
        // missed (the plate above all) is typed there, not lost.
        // The college stays the one picked on Ask; the taps stay
        // exactly as set. The extractor can never produce a price.
        cancellationToken.ThrowIfCancellationRequested();
        var client = HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!_aiRequests.TryAcquire(client, out var lease, out var retryAfterSeconds))
        {
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
            return View("Questions", new QuestionsViewModel
            {
                ApplicationId = applicationId,
                College = fee.Campus,
                CarText = carText,
                PermitType = permitType,
                Term = term,
                StartDate = startDate,
                Notice = AiRequestLimiter.RejectionMessage + " Your answers are still here.",
            });
        }

        using var admission = lease;
        var result = await _extractor.ExtractAsync(carText ?? "", cancellationToken);

        var draft = new PermitDraftViewModel
        {
            ApplicationId = applicationId,
            College = fee.Campus,
            CarText = carText,
            Plate = result.Plate,
            PlateMissing = string.IsNullOrWhiteSpace(result.Plate),
            CarMake = result.CarMake,
            CarModel = result.CarModel,
            CarColor = result.CarColor,
            CarState = result.CarState,
            PermitType = permitType,
            Term = term,
            StartDate = startDate,
            Source = result.Source.ToWireName(),
        };
        draft.SourceNote = SourceNote(draft.Source);

        return View("Understood", draft);
    }

    // ==================================================================
    // AI DOOR — Review: the draft persists here, the trust moment
    // ==================================================================

    /// <summary>
    /// The Understood screen's continue button lands here with the
    /// (possibly hand-corrected) draft. THE GATE: a blank plate goes
    /// straight back to the Understood screen with its big prompt —
    /// the flow never advances without a plate, and it never
    /// restarts silently at Questions. With a plate, the draft is
    /// persisted through ApplicationService (create or revise) and
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
            return RedirectToAction(nameof(Ask));
        }

        draft.College = fee.Campus;
        draft.SourceNote = SourceNote(draft.Source);

        if (draft.CarText?.Length > PermitDraftViewModel.MaxCarTextLength)
        {
            return QuestionsForLongDescription(draft);
        }

        ValidateDraftVehicle(draft);
        var normalizedPlate = FallbackExtractor.NormalizePlate(draft.Plate);
        if (normalizedPlate is null)
        {
            draft.PlateMissing = true;
            ModelState.AddModelError(nameof(draft.Plate), "Enter a license plate with 4 to 10 letters or digits.");
        }
        else
        {
            draft.Plate = normalizedPlate;
            draft.PlateMissing = false;
        }
        if (!ModelState.IsValid)
        {
            return View("Understood", draft);
        }

        var student = await CurrentStudentAsync(_db, cancellationToken);

        PermitApplication? existing = null;
        if (draft.ApplicationId is { } draftId)
        {
            existing = await _db.PermitApplications.SingleOrDefaultAsync(
                a => a.Id == draftId && a.StudentId == student.Id, cancellationToken);
            if (existing?.ReceiptNumber is not null)
            {
                // Already a live permit — never revised, never re-sold.
                return Redirect($"/Permits/Receipt/{existing.Id}");
            }
        }

        Vehicle vehicle;
        try
        {
            vehicle = await _applications.ResolveVehicleForDraftAsync(
                student.Id, draft.Plate!, draft.CarMake, draft.CarModel,
                draft.CarColor, draft.CarState, cancellationToken);
        }
        catch (DraftVehicleStateLockedException ex)
        {
            ModelState.AddModelError(nameof(draft.CarState), ex.Message);
            return View("Understood", draft);
        }

        var input = new ApplicationInput(
            StudentId: student.Id,
            VehicleId: vehicle.Id,
            Campus: fee.Campus,
            TypeCode: draft.PermitType,
            Term: draft.Term,
            StartDate: draft.StartDate ?? ComputeAiStart(fee.Campus, draft.PermitType, draft.Term),
            Source: draft.Source,
            PersistWhenPriceBlocked: true);

        var result = existing is not null
            ? await _applications.ReviseDraftAsync(existing.Id, input, cancellationToken)
            : await _applications.CreateAsync(input, cancellationToken);

        return result.Status switch
        {
            CreateApplicationStatus.Created or
            CreateApplicationStatus.Duplicate or
            CreateApplicationStatus.AlreadyLive => result.Application!.ReceiptNumber is not null
                ? Redirect($"/Permits/Receipt/{result.Application.Id}")
                : RedirectToAction(nameof(Review), new { id = result.Application!.Id }),
            // Defensive only (the draft persists even when blocked):
            // back to the gate screen rather than a dead end.
            _ => BackToUnderstoodWithError(draft),
        };
    }

    /// <summary>The Review screen itself, rendered from the saved draft.</summary>
    [HttpGet]
    public async Task<IActionResult> Review(Guid id, CancellationToken cancellationToken)
    {
        var application = await LoadOwnApplicationAsync(id, cancellationToken);
        if (application is null)
        {
            return NotFound();
        }

        if (application.ReceiptNumber is not null)
        {
            return Redirect($"/Permits/Receipt/{application.Id}");
        }

        return View(BuildReview(application));
    }

    /// <summary>
    /// The blocked day-pass screen's one-tap alternative: switch the
    /// saved draft to the term permit and land back on its Review —
    /// same draft, same car, no re-typing.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SwitchType(
        Guid id, PermitTypeCode permitType, CancellationToken cancellationToken)
    {
        var application = await LoadOwnApplicationAsync(id, cancellationToken);
        if (application is null)
        {
            return NotFound();
        }

        if (application.ReceiptNumber is not null)
        {
            return Redirect($"/Permits/Receipt/{application.Id}");
        }

        var input = new ApplicationInput(
            StudentId: application.StudentId,
            VehicleId: application.VehicleId,
            Campus: application.Campus,
            TypeCode: permitType,
            Term: application.Term,
            StartDate: ComputeAiStart(application.Campus, permitType, application.Term),
            Source: application.Source,
            PersistWhenPriceBlocked: true);

        var result = await _applications.ReviseDraftAsync(
            application.Id, input, cancellationToken);
        return result.Status switch
        {
            CreateApplicationStatus.Duplicate or
            CreateApplicationStatus.AlreadyLive =>
                result.Application!.ReceiptNumber is not null
                    ? Redirect($"/Permits/Receipt/{result.Application.Id}")
                    : RedirectToAction(nameof(Review), new { id = result.Application!.Id }),
            _ => RedirectToAction(nameof(Review), new { id = application.Id }),
        };
    }

    // ==================================================================
    // AI DOOR — Confirm: id only, hand off to Checkout
    // ==================================================================

    /// <summary>
    /// Takes ONLY the draft id. The price is re-read HERE from the
    /// fee catalog — never from anything the browser sent — and a
    /// problem NEVER bounces back to Questions: a blocked price or a
    /// still-blank plate returns THIS Review screen with the honest
    /// block / a big error banner (the plate case is residual only —
    /// the Understood gate stops new drafts long before here).
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(Guid id, CancellationToken cancellationToken)
    {
        var application = await LoadOwnApplicationAsync(id, cancellationToken);
        if (application is null)
        {
            return NotFound();
        }

        if (application.ReceiptNumber is not null)
        {
            return Redirect($"/Permits/Receipt/{application.Id}");
        }

        var review = BuildReview(application);
        if (review.Blocked)
        {
            return View("Review", review);
        }

        if (string.IsNullOrWhiteSpace(application.Vehicle?.Plate))
        {
            return View("Review", BuildReview(application,
                error: "We still need your car's plate number before you " +
                       "can pay. Use “Check my answers” below and type it " +
                       "on the “Did I get this right?” screen."));
        }

        // Persist the freshly recomputed snapshot if it moved (the
        // catalog is a static file, so this is a guard, not a path).
        if (review.PriceCents is { } price && price != application.PriceCents)
        {
            application.PriceCents = price;
            application.ConcurrencyToken = Guid.NewGuid();
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Item 51: a stale write loses loudly, on this same
                // screen — never a silent overwrite.
                return View("Review", BuildReview(application,
                    error: "This application changed while you were " +
                           "looking at it. Check the details, then try again."));
            }
        }

        if (application.PaymentStatus == PaymentStatus.NotRequired)
        {
            // Defensive: a $0 draft is issued at save time, so this
            // is unreachable in the normal flow.
            return Redirect($"/Permits/Receipt/{application.Id}");
        }

        return Redirect($"/Checkout/{application.Id}");
    }

    // ==================================================================
    // AI-door helpers
    // ==================================================================

    private IActionResult QuestionsForLongDescription(PermitDraftViewModel draft) =>
        View("Questions", new QuestionsViewModel
        {
            ApplicationId = draft.ApplicationId,
            College = draft.College,
            CarText = draft.CarText,
            PermitType = draft.PermitType,
            Term = draft.Term,
            StartDate = draft.StartDate,
            Notice = PermitDraftViewModel.CarTextLengthMessage +
                     " Your college and permit choices are kept; shorten the description and try again.",
        });

    private void ValidateDraftVehicle(PermitDraftViewModel draft)
    {
        if (!Enum.IsDefined(draft.PermitType) || !Enum.IsDefined(draft.Term))
            ModelState.AddModelError("", "Choose a permit type and term from the lists.");
        if (draft.StartDate is { } start && draft.PermitType == PermitTypeCode.Term && Enum.IsDefined(draft.Term))
        {
            var dates = _termDates.GetTermDates(draft.College, draft.Term, start.Year);
            if (dates is null || start < dates.TermStart || start > dates.TermEnd || start > dates.PermitEndDate)
                ModelState.AddModelError(nameof(draft.StartDate), "The planned start must fall inside the selected demo term window. Return to the planner to check the dates.");
        }
        // Validate here as well as during MVC binding: no direct caller or
        // manually posted draft may bypass the same persistence boundary.
        var errors = new List<ValidationResult>();
        Validator.TryValidateObject(draft, new ValidationContext(draft), errors, validateAllProperties: true);
        foreach (var error in errors)
        {
            foreach (var member in error.MemberNames)
            {
                var message = error.ErrorMessage ?? "Check this vehicle detail.";
                if (!ModelState.TryGetValue(member, out var entry) ||
                    entry.Errors.All(existing => existing.ErrorMessage != message))
                    ModelState.AddModelError(member, message);
            }
        }

        if (!IsNamedEnumValue<UsState>(draft.CarState))
            ModelState.AddModelError(nameof(draft.CarState), "Choose a plate state from the list.");
        if (!IsNamedEnumValue<VehicleColor>(draft.CarColor))
            ModelState.AddModelError(nameof(draft.CarColor), "Choose a color from the list.");
    }

    private static bool IsNamedEnumValue<TEnum>(string? value) where TEnum : struct, Enum =>
        string.IsNullOrWhiteSpace(value) || Enum.GetNames<TEnum>().Any(name =>
            string.Equals(name, value.Trim(), StringComparison.OrdinalIgnoreCase));

    private IActionResult BackToUnderstoodWithError(PermitDraftViewModel draft)
    {
        draft.PlateMissing = string.IsNullOrWhiteSpace(draft.Plate);
        return View("Understood", draft);
    }

    /// <summary>Loads one of the current person's applications (vehicle + type joined).</summary>
    private async Task<PermitApplication?> LoadOwnApplicationAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var student = await CurrentStudentAsync(_db, cancellationToken);
        return await _db.PermitApplications
            .Include(a => a.Vehicle)
            .Include(a => a.PermitType)
            .SingleOrDefaultAsync(
                a => a.Id == id && a.StudentId == student.Id, cancellationToken);
    }

    /// <summary>
    /// Builds the trust-moment model from the SAVED draft: stored
    /// dates (the snapshot), a FRESH catalog price (never the stored
    /// placeholder), and — for a blocked day pass — the same
    /// college's term price as the one-tap alternative.
    /// </summary>
    private ReviewViewModel BuildReview(PermitApplication application, string? error = null)
    {
        var fee = _catalog.Find(application.Campus);
        var type = application.PermitType;
        var price = fee is null || type is null
            ? null
            : _pricing.ResolvePriceCents(fee, type.Code, application.Term, application.Category);

        var isDayPassBlock = price is null && type?.Code == PermitTypeCode.Daily;
        int? termAlternative = null;
        if (isDayPassBlock && fee is not null)
        {
            termAlternative = _pricing.ResolvePriceCents(
                fee, PermitTypeCode.Term, application.Term, application.Category);
        }

        var vehicle = application.Vehicle;
        var carLine = string.Join(" ", new[]
        {
            vehicle?.Color.ToString(), vehicle?.Make, vehicle?.Model,
        }.Where(p => !string.IsNullOrWhiteSpace(p)));

        return new ReviewViewModel
        {
            ApplicationId = application.Id,
            Draft = DraftFromApplication(application),
            College = application.Campus,
            CategoryLabel = application.Category.ToString(),
            TypeName = type?.Name ?? "Parking permit",
            Term = application.Term,
            CarLine = carLine.Length > 0 ? carLine : "Your car",
            PlateLine = vehicle is null ? "" : $"{vehicle.Plate} ({vehicle.State})",
            StartDate = application.StartDate,
            EndDate = application.EndDate,
            PriceCents = price,
            IsDayPassBlock = isDayPassBlock,
            TotalDisplay = price is null
                ? "Price not published"
                : price == 0
                    ? "$0 — FREE"
                    : Money(price.Value),
            TermAlternativePriceCents = termAlternative,
            TermAlternativeDisplay = termAlternative is null
                ? null
                : termAlternative == 0
                    ? "$0 — FREE"
                    : Money(termAlternative.Value),
            Error = error,
        };
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
            StartDate = application.StartDate,
        };
    }

    /// <summary>
    /// The AI door never asks for a start date (§2c): a Term permit
    /// starts on its term window's first day; a day pass is for
    /// today.
    /// </summary>
    private DateOnly ComputeAiStart(string campus, PermitTypeCode type, TermKind term)
    {
        if (type == PermitTypeCode.Daily)
        {
            return Today();
        }

        var dates = _termDates.GetTermDates(campus, term, YearFor(term));
        return dates?.TermStart ?? Today();
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

    private static string SourceNote(string source) => source switch
    {
        "ollama" => "Read by Solvia's configured AI reader via Ollama. Check the draft before continuing.",
        "together" => "Read by Solvia's hosted AI reader via Together AI. Check the draft before continuing.",
        _ => "Read by the built-in reader because the configured AI was unavailable " +
             "or returned an unusable answer. Check the draft before continuing."
    };

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.Now);

    private static string ShortDate(DateOnly date) =>
        date.ToString("MMM d", CultureInfo.InvariantCulture);

    private static string Money(int cents) =>
        "$" + (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
}
