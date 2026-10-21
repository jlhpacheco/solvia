using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
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
    private const string FlowPrefix = "PermitFlow:";
    private static readonly JsonSerializerOptions FlowJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private sealed record FlowState(string OwnerId, PermitDraftViewModel Draft, bool HasVehicleDraft, DateTimeOffset ExpiresAtUtc);
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
    public async Task<IActionResult> Ask(CancellationToken cancellationToken, Guid? flowId = null)
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
            FlowId = ReadFlow(flowId, student.Id)?.Draft.FlowId,
            StudentName = student.DisplayName,
            Campuses = campuses,
        });
    }

    // ==================================================================
    // AI DOOR — 3 questions only
    // ==================================================================

    [HttpGet]
    public async Task<IActionResult> Questions(string? college, PermitTypeCode permitType = PermitTypeCode.Term,
        TermKind term = TermKind.Fall, DateOnly? startDate = null, Guid? flowId = null)
    {
        var fee = college is null ? null : _catalog.Find(college);
        if (fee is null)
        {
            return RedirectToAction(nameof(Ask));
        }

        var student = await CurrentStudentAsync(_db, HttpContext.RequestAborted);
        var state = ReadFlow(flowId, student.Id);
        if (state is null)
        {
            var draft = new PermitDraftViewModel
            {
                FlowId = Guid.NewGuid(), College = fee.Campus,
                PermitType = Enum.IsDefined(permitType) ? permitType : PermitTypeCode.Term,
                Term = Enum.IsDefined(term) ? term : TermKind.Fall, StartDate = startDate,
                CarText = TempData["CarText"] as string,
            };
            SaveFlow(student.Id, draft, false);
            return RedirectToAction(nameof(Questions), new { college = fee.Campus, flowId = draft.FlowId });
        }
        if (!string.Equals(state.Draft.College, fee.Campus, StringComparison.OrdinalIgnoreCase))
        {
            // A saved application belongs to its original campus. Carry the
            // vehicle answers into a NEW flow, never move its order/draft id.
            state.Draft.FlowId = Guid.NewGuid();
            state.Draft.ApplicationId = null;
            state.Draft.College = fee.Campus;
            SaveFlow(student.Id, state.Draft, state.HasVehicleDraft);
            return RedirectToAction(nameof(Questions), new { college = fee.Campus, flowId = state.Draft.FlowId });
        }
        return View(ToQuestions(state.Draft, TempData["Notice"] as string));
    }

    private static QuestionsViewModel ToQuestions(PermitDraftViewModel draft, string? notice = null) =>
        new()
        {
            FlowId = draft.FlowId, ApplicationId = draft.ApplicationId,
            DraftOwner = draft.DraftOwner,
            College = draft.College, CarText = draft.CarText,
            PermitType = draft.PermitType, Term = draft.Term,
            StartDate = draft.StartDate, Notice = notice,
        };

    /// <summary>The Fix-it path back: re-render the questions, answers kept.</summary>
    [HttpPost]
    [ActionName("Questions")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> QuestionsBack(PermitDraftViewModel draft)
    {
        var fee = _catalog.Find(draft.College);
        if (fee is null)
        {
            return RedirectToAction(nameof(Ask));
        }

        var student = await CurrentStudentAsync(_db, HttpContext.RequestAborted);
        var retained = ReadFlow(draft.FlowId, student.Id);
        if (retained is null) return ExpiredFlow();
        draft.College = fee.Campus;
        draft.Source = retained.Draft.Source;
        draft.SourceNote = SourceNote(draft.Source);
        if (!SaveFlow(student.Id, draft, true))
            return View("Questions", ToQuestions(draft, "This draft is too large to keep safely. Shorten the vehicle description, then review it again. Your entered sentence is still below."));
        return RedirectToAction(nameof(Questions), new { college = fee.Campus, flowId = draft.FlowId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChooseCollege(QuestionsViewModel form, CancellationToken cancellationToken,
        [Bind(Prefix = "revision")] PermitDraftViewModel? revision = null)
    {
        var student = await CurrentStudentAsync(_db, cancellationToken);
        var state = ReadFlow(form.FlowId, student.Id);
        if (state is null) return ExpiredFlow();
        if (form.CarText?.Length > PermitDraftViewModel.MaxCarTextLength)
            return QuestionsForLongDescription(new PermitDraftViewModel
            { FlowId = form.FlowId, College = state.Draft.College, CarText = form.CarText,
                PermitType = form.PermitType, Term = form.Term, StartDate = form.StartDate, DraftOwner = student.Id });
        var unchanged = string.Equals(state.Draft.CarText, form.CarText, StringComparison.Ordinal);
        if (state.HasVehicleDraft && unchanged) MergeRevision(state.Draft, revision, form.CarText);
        state.Draft.CarText = form.CarText;
        state.Draft.PermitType = form.PermitType;
        state.Draft.Term = form.Term;
        state.Draft.StartDate = form.StartDate;
        if (!SaveFlow(student.Id, state.Draft, state.HasVehicleDraft && unchanged))
            return View("Questions", ToQuestions(state.Draft, "Shorten the vehicle description before changing colleges. Your entered sentence is still below."));
        return RedirectToAction(nameof(Ask), new { flowId = form.FlowId });
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
        CancellationToken cancellationToken, DateOnly? startDate = null, Guid? flowId = null,
        [Bind(Prefix = "revision")] PermitDraftViewModel? revision = null)
    {
        var fee = _catalog.Find(college);
        if (fee is null)
        {
            return RedirectToAction(nameof(Ask));
        }

        var student = await CurrentStudentAsync(_db, cancellationToken);
        var retained = ReadFlow(flowId, student.Id);
        if (retained is null) return ExpiredFlow();
        if (!string.Equals(retained.Draft.College, fee.Campus, StringComparison.OrdinalIgnoreCase))
            return RedirectToAction(nameof(Questions), new { college = fee.Campus, flowId });
        // An unchanged sentence must not overwrite the person's corrections
        // with another extraction. Permit/date choices remain independently editable.
        if (retained.HasVehicleDraft && string.Equals(carText, retained.Draft.CarText, StringComparison.Ordinal))
        {
            MergeRevision(retained.Draft, revision, carText);
            retained.Draft.College = fee.Campus;
            retained.Draft.PermitType = permitType;
            retained.Draft.Term = term;
            retained.Draft.StartDate = startDate;
            if (!SaveFlow(student.Id, retained.Draft, true))
                return View("Questions", ToQuestions(retained.Draft, "Shorten the vehicle description before continuing. Your entered sentence is still below."));
            return RedirectToAction(nameof(Understood), new { flowId = retained.Draft.FlowId });
        }

        if (carText?.Length > PermitDraftViewModel.MaxCarTextLength)
        {
            return QuestionsForLongDescription(new PermitDraftViewModel
            {
                FlowId = retained.Draft.FlowId,
                DraftOwner = student.Id,
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
                FlowId = retained.Draft.FlowId,
                DraftOwner = student.Id,
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
            FlowId = retained.Draft.FlowId,
            ApplicationId = retained.Draft.ApplicationId,
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

        if (!SaveFlow(student.Id, draft, true))
            return View("Questions", ToQuestions(draft, "This description is too large to retain safely. Shorten it and try again; your entered sentence is still below."));
        return RedirectToAction(nameof(Understood), new { flowId = draft.FlowId });
    }

    [HttpGet]
    public async Task<IActionResult> Understood(Guid flowId, CancellationToken cancellationToken)
    {
        var student = await CurrentStudentAsync(_db, cancellationToken);
        var state = ReadFlow(flowId, student.Id);
        if (state is null || !state.HasVehicleDraft) return ExpiredFlow();
        state.Draft.SourceNote = SourceNote(state.Draft.Source);
        state.Draft.PlateMissing = string.IsNullOrWhiteSpace(state.Draft.Plate);
        return View("Understood", state.Draft);
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
        var student = await CurrentStudentAsync(_db, cancellationToken);
        var retained = ReadFlow(draft.FlowId, student.Id);
        if (retained is null) return ExpiredFlow();
        draft.Source = retained.Draft.Source;
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

        PermitApplication? existing = null;
        if (draft.ApplicationId is { } draftId)
        {
            existing = await _db.PermitApplications.SingleOrDefaultAsync(
                a => a.Id == draftId && a.StudentId == student.Id, cancellationToken);
            if (existing is null) return NotFound();
            if (!string.Equals(existing.Campus, fee.Campus, StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError("", "Choose a different college from the questions screen to start a separate application. Your existing permit is unchanged.");
                return View("Understood", draft);
            }
            if (existing.ReceiptNumber is not null)
            {
                // Already a live permit — never revised, never re-sold.
                return Redirect($"/Permits/Receipt/{existing.Id}");
            }
            if (!string.IsNullOrWhiteSpace(existing.PayPalOrderId))
            {
                TempData["StatusMessage"] = "Payment has already started for this permit. Finish or review that checkout before starting a different application.";
                return Redirect($"/Checkout/{existing.Id}");
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
        catch (VehiclePurchaseLockedException ex)
        {
            ModelState.AddModelError("", ex.Message);
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

        if (result.Application is { } saved)
        {
            draft.ApplicationId = saved.Id;
            draft.StartDate = saved.StartDate;
            SaveFlow(student.Id, draft, true);
        }

        return result.Status switch
        {
            CreateApplicationStatus.PaymentStarted => Redirect($"/Checkout/{result.Application!.Id}"),
            CreateApplicationStatus.Created or
            CreateApplicationStatus.Duplicate or
            CreateApplicationStatus.AlreadyLive => result.Application!.ReceiptNumber is not null
                ? Redirect($"/Permits/Receipt/{result.Application!.Id}")
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
            CreateApplicationStatus.PaymentStarted => Redirect($"/Checkout/{result.Application!.Id}"),
            CreateApplicationStatus.Duplicate or
            CreateApplicationStatus.AlreadyLive =>
                result.Application!.ReceiptNumber is not null
                    ? Redirect($"/Permits/Receipt/{result.Application!.Id}")
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

        if (!string.IsNullOrWhiteSpace(application.PayPalOrderId))
            return Redirect($"/Checkout/{application.Id}");

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

        if (review.PriceCents == 0 && application.PriceCents != 0)
        {
            // An acknowledged newly-free price uses the existing issuance
            // authority; it must not leave an unpaid $0 row at Checkout.
            var free = await _applications.ReviseDraftAsync(application.Id,
                new ApplicationInput(application.StudentId, application.VehicleId,
                    application.Campus, application.PermitType!.Code, application.Term,
                    application.StartDate, application.Source, true), cancellationToken);
            if (free.Application?.ReceiptNumber is not null)
                return Redirect($"/Permits/Receipt/{free.Application.Id}");
            if (free.Status == CreateApplicationStatus.PaymentStarted)
                return Redirect($"/Checkout/{free.Application!.Id}");
            return View("Review", BuildReview(application,
                "This application changed. Check its current details before continuing."));
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

    private static void MergeRevision(PermitDraftViewModel target, PermitDraftViewModel? revision, string? carText)
    {
        if (revision is null || !string.Equals(revision.CarText, carText, StringComparison.Ordinal)) return;
        // Only editable facts may return from temporary tab storage. Owner,
        // source, application identity and money remain server-owned.
        target.Plate = revision.Plate;
        target.CarMake = revision.CarMake;
        target.CarModel = revision.CarModel;
        target.CarColor = revision.CarColor;
        target.CarState = revision.CarState;
    }

    // Cookie TempData is encrypted/authenticated by ASP.NET Core. Keep at
    // most two small browser draft snapshots; no vehicle prose goes in a URL
    // or permanent browser storage. Flow ids alone never authorize a payment.
    private FlowState? ReadFlow(Guid? flowId, string ownerId)
    {
        if (flowId is null || flowId == Guid.Empty) return null;
        if (TempData.Peek(FlowPrefix + flowId.Value.ToString("N")) is not string encoded) return null;
        try
        {
            using var input = new MemoryStream(Convert.FromBase64String(encoded));
            using var compression = new BrotliStream(input, CompressionMode.Decompress);
            using var text = new StreamReader(compression, Encoding.UTF8);
            var state = JsonSerializer.Deserialize<FlowState>(text.ReadToEnd(), FlowJson);
            return state is not null && state.OwnerId == ownerId && state.Draft.FlowId == flowId && state.ExpiresAtUtc > DateTimeOffset.UtcNow
                ? state : null;
        }
        catch (Exception error) when (error is JsonException or FormatException or InvalidDataException) { return null; }
    }

    private bool SaveFlow(string ownerId, PermitDraftViewModel draft, bool hasVehicleDraft)
    {
        if (draft.FlowId == Guid.Empty) throw new InvalidOperationException("A flow id is required.");
        if (draft.CarText?.Length > PermitDraftViewModel.MaxCarTextLength ||
            draft.CarMake?.Length > PermitDraftViewModel.MaxVehicleDetailLength ||
            draft.CarModel?.Length > PermitDraftViewModel.MaxVehicleDetailLength ||
            draft.Plate?.Length > 20 || draft.CarColor?.Length > 30 || draft.CarState?.Length > 10)
            return false; // Invalid large values remain on the rendered form, never in cookies.
        var key = FlowPrefix + draft.FlowId.ToString("N");
        draft.DraftOwner = ownerId;
        var json = JsonSerializer.Serialize(new FlowState(ownerId, draft, hasVehicleDraft, DateTimeOffset.UtcNow.AddHours(2)), FlowJson);
        using var output = new MemoryStream();
        using (var compression = new BrotliStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            compression.Write(Encoding.UTF8.GetBytes(json));
        var encoded = Convert.ToBase64String(output.ToArray());
        if (encoded.Length > 3200) return false;
        var otherKeys = TempData.Keys.Where(k => k.StartsWith(FlowPrefix, StringComparison.Ordinal) && k != key).ToList();
        foreach (var stale in otherKeys.Take(Math.Max(0, otherKeys.Count - 1))) TempData.Remove(stale);
        // Cookie/header budget includes the independent planner snapshot.
        foreach (var stale in otherKeys.Where(k => TempData.ContainsKey(k)))
        {
            var othersSize = TempData.Keys.Where(k => k.StartsWith(FlowPrefix, StringComparison.Ordinal) && k != key)
                .Sum(k => (TempData.Peek(k) as string)?.Length ?? 0);
            if (othersSize + encoded.Length <= 3200) break;
            TempData.Remove(stale);
        }
        TempData[key] = encoded;
        return true;
    }

    private IActionResult ExpiredFlow()
    {
        TempData["StatusMessage"] = "This draft belongs to another demo person or is no longer available. Choose your college to start a new draft; no payment was made.";
        return RedirectToAction(nameof(Ask));
    }

    private PermitDraftViewModel RestoreApplicationDraft(PermitApplication application)
    {
        var draft = DraftFromApplication(application);
        foreach (var key in TempData.Keys.Where(k => k.StartsWith(FlowPrefix, StringComparison.Ordinal)).ToList())
        {
            if (!Guid.TryParseExact(key[FlowPrefix.Length..], "N", out var flowId)) continue;
            var state = ReadFlow(flowId, application.StudentId);
            if (state?.Draft.ApplicationId != application.Id) continue;
            draft.FlowId = flowId;
            draft.DraftOwner = application.StudentId;
            draft.CarText = state.Draft.CarText;
            return draft;
        }
        // The original sentence may expire with the browser session. Saved
        // vehicle fields still support editing an owned unpaid application.
        draft.FlowId = Guid.NewGuid();
        draft.CarText = string.Join(", ", new[] { draft.CarColor, draft.CarMake, draft.CarModel,
            "license plate " + draft.Plate, draft.CarState }.Where(s => !string.IsNullOrWhiteSpace(s)));
        SaveFlow(application.StudentId, draft, true);
        return draft;
    }

    private IActionResult QuestionsForLongDescription(PermitDraftViewModel draft) =>
        View("Questions", new QuestionsViewModel
        {
            FlowId = draft.FlowId,
            DraftOwner = draft.DraftOwner,
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
        var publishedPrice = fee is null || type is null
            ? null
            : _pricing.ResolvePriceCents(fee, type.Code, application.Term, application.Category);
        var orderQuoteLocked = !string.IsNullOrWhiteSpace(application.PayPalOrderId);
        var price = orderQuoteLocked ? application.PriceCents : publishedPrice;

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
            Draft = RestoreApplicationDraft(application),
            College = application.Campus,
            CategoryLabel = application.Category.ToString(),
            TypeName = type?.Name ?? "Parking permit",
            Term = application.Term,
            CarLine = carLine.Length > 0 ? carLine : "Your car",
            PlateLine = vehicle is null ? "" : $"{vehicle.Plate} ({vehicle.State})",
            StartDate = application.StartDate,
            EndDate = application.EndDate,
            PriceCents = price,
            PayPalOrderId = application.PayPalOrderId,
            PayPalCaptureId = application.PayPalCaptureId,
            OrderQuoteLocked = orderQuoteLocked,
            PriceChangeRequiresReview = !orderQuoteLocked && price is not null && price != application.PriceCents,
            ReviewedSnapshot = CheckoutReviewSnapshot.Compute(application),
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
            DraftOwner = application.StudentId,
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
