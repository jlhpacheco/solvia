using Microsoft.EntityFrameworkCore;
using Solvia.Data;
using Solvia.Models;
using Solvia.Models.Enums;

namespace Solvia.Services;

/// <summary>
/// The normalized creation input (REDO-PLAN §2c): the ONLY shape an
/// application is ever created from. Both doors gather these same
/// facts — the form door from dropdowns and a date input, the AI
/// door from the extraction plus the Understood gate — and differ
/// only in how they gathered them. Campus is a catalog name; price,
/// category, and dates are NEVER inputs — they are resolved inside.
/// </summary>
/// <param name="StudentId">Server-resolved demo person (owner).</param>
/// <param name="VehicleId">The vehicle row the permit covers (form
/// door: the chosen dropdown row; AI door: resolved/created first via
/// <see cref="ApplicationService.ResolveVehicleForDraftAsync"/>).</param>
/// <param name="Campus">Campus name; canonicalized against the catalog.</param>
/// <param name="TypeCode">Daily or Term.</param>
/// <param name="Term">The picked term (read for Term permits and for
/// the day-pass alternative; stored on the row either way).</param>
/// <param name="StartDate">First valid day. Form door: typed. AI
/// door: the term window's start (Term) or today (Daily).</param>
/// <param name="Source">Honest origin label: "form", "ollama", or "fallback".</param>
/// <param name="PersistWhenPriceBlocked">The AI door passes true:
/// when the catalog does not publish the price, the draft is still
/// saved (price snapshot parked at 0, payment Pending) so its Review
/// screen can show the honest block and offer the other type. The
/// form door passes false: a blocked price creates NOTHING and the
/// applicant gets the error on the same form (item 11/16).</param>
public sealed record ApplicationInput(
    string StudentId,
    Guid VehicleId,
    string Campus,
    PermitTypeCode TypeCode,
    TermKind Term,
    DateOnly StartDate,
    string Source,
    bool PersistWhenPriceBlocked = false);

/// <summary>How a creation/revision attempt ended.</summary>
public enum CreateApplicationStatus
{
    /// <summary>The row exists now with the resolved snapshot (issued already, if $0).</summary>
    Created,

    /// <summary>The composite unique key already exists — the twin rides along.</summary>
    Duplicate,

    /// <summary>The campus publishes no such price; nothing was created.</summary>
    PriceNotPublished,

    /// <summary>The campus is not in the fee catalog at all.</summary>
    CampusNotFound,

    /// <summary>The permit type is missing or inactive in the catalog.</summary>
    TypeUnavailable,

    /// <summary>(Revision only) No application with that id exists.</summary>
    NotFound,

    /// <summary>(Revision only) The application is already a live permit — never revised.</summary>
    AlreadyLive,

    /// <summary>(Revision only) An existing PayPal order locks the purchase snapshot.</summary>
    PaymentStarted,
}

/// <summary>
/// The outcome of a creation/revision. On <see cref="CreateApplicationStatus.Created"/>
/// and <see cref="CreateApplicationStatus.Duplicate"/>, <see cref="Application"/>
/// carries the row (the new one, or the pre-existing twin).
/// </summary>
public sealed record CreateApplicationResult(
    CreateApplicationStatus Status,
    PermitApplication? Application,
    string? ErrorMessage)
{
    public bool IsCreated => Status == CreateApplicationStatus.Created;
}

/// <summary>The AI draft must preserve the garage's referenced plate/state rule.</summary>
public sealed class DraftVehicleStateLockedException : InvalidOperationException
{
    public DraftVehicleStateLockedException()
        : base("This plate is linked to permit applications. Add a new vehicle to use a different plate or state.")
    {
    }
}

/// <summary>
/// The ONE application-creation brain (REDO-PLAN §2c) — both doors
/// create through here, so there is one category rule, one price
/// rule, one end-date rule, one duplicate rule, and one issuance
/// rule:
///
/// - Category is resolved from the person (UserCategoryResolver) and
///   SNAPSHOTTED onto the row; pricing reads the snapshot.
/// - Price comes from PricingService against the campus's catalog
///   row — never from the PermitType row, never from the browser.
///   Unpublished = blocked (see <see cref="ApplicationInput.PersistWhenPriceBlocked"/>).
/// - End date is computed, never typed: Daily → the start day
///   itself; Term → the term window's PermitEndDate from
///   ITermDatesService for (campus, term, start year).
/// - $0 (a FREE campus, or the Employee exemption) → PaymentStatus
///   NotRequired and issuance happens INSIDE this flow, before the
///   row is saved — the caller just redirects.
/// - The composite unique index (person, vehicle, type, start,
///   end) is pre-checked and its DbUpdateException is caught, so a
///   duplicate surfaces as an outcome, never a 500.
/// </summary>
public sealed class ApplicationService
{
    private readonly SolviaDbContext _db;
    private readonly IFeeCatalog _catalog;
    private readonly PricingService _pricing;
    private readonly UserCategoryResolver _categories;
    private readonly ITermDatesService _termDates;
    private readonly IIssuanceService _issuance;

    public ApplicationService(
        SolviaDbContext db,
        IFeeCatalog catalog,
        PricingService pricing,
        UserCategoryResolver categories,
        ITermDatesService termDates,
        IIssuanceService issuance)
    {
        _db = db;
        _catalog = catalog;
        _pricing = pricing;
        _categories = categories;
        _termDates = termDates;
        _issuance = issuance;
    }

    // ------------------------------------------------------------------
    // Creation (both doors)
    // ------------------------------------------------------------------

    public async Task<CreateApplicationResult> CreateAsync(
        ApplicationInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var fee = _catalog.Find(input.Campus);
        if (fee is null)
        {
            return new CreateApplicationResult(
                CreateApplicationStatus.CampusNotFound, null,
                $"'{input.Campus}' is not one of Solvia's colleges.");
        }

        var type = await ActiveTypeAsync(input.TypeCode, cancellationToken);
        if (type is null)
        {
            return new CreateApplicationResult(
                CreateApplicationStatus.TypeUnavailable, null,
                "That permit type is not available right now.");
        }

        var student = await _db.Students
            .SingleAsync(s => s.Id == input.StudentId, cancellationToken);
        var category = _categories.Resolve(student);

        var end = ComputeEndDate(fee.Campus, input, type);

        // The duplicate rule fires before anything is written: the
        // same person + vehicle + type over the same window exists.
        var twin = await FindTwinAsync(
            input.StudentId, input.VehicleId, type.Id,
            input.StartDate, end, excludeId: null, cancellationToken);
        if (twin is not null)
        {
            return new CreateApplicationResult(
                CreateApplicationStatus.Duplicate, twin, DuplicateMessage);
        }

        var price = _pricing.ResolvePriceCents(
            fee, input.TypeCode, input.Term, category);
        if (price is null && !input.PersistWhenPriceBlocked)
        {
            return new CreateApplicationResult(
                CreateApplicationStatus.PriceNotPublished, null,
                UnpublishedMessage(fee.Campus, input));
        }

        var application = new PermitApplication
        {
            Id = Guid.NewGuid(),
            StudentId = input.StudentId,
            Campus = fee.Campus,
            Category = category,
            VehicleId = input.VehicleId,
            PermitTypeId = type.Id,
            Term = input.Term,
            Source = input.Source,
            StartDate = input.StartDate,
            EndDate = end,
            Status = ApplicationStatus.Pending,
            // A blocked draft parks at 0 while Pending — never a
            // chargeable value: its door recomputes before checkout,
            // and checkout never renders for a blocked draft.
            PriceCents = price ?? 0,
            PaymentStatus = price == 0
                ? PaymentStatus.NotRequired
                : PaymentStatus.Pending,
            SubmittedAtUtc = DateTime.UtcNow,
            ConcurrencyToken = Guid.NewGuid(),
        };

        if (price == 0)
        {
            // Free at creation (FREE campus or Employee): issue
            // inside the same flow — number + token ride the insert.
            await _issuance.IssueIfAbsentAsync(application, cancellationToken);
        }

        _db.PermitApplications.Add(application);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The unique index caught a race the pre-check missed.
            Detach(application);
            var winner = await FindTwinAsync(
                input.StudentId, input.VehicleId, type.Id,
                input.StartDate, end, excludeId: null, cancellationToken);
            if (winner is not null)
            {
                return new CreateApplicationResult(
                    CreateApplicationStatus.Duplicate, winner, DuplicateMessage);
            }

            throw;
        }

        return new CreateApplicationResult(
            CreateApplicationStatus.Created, application, null);
    }

    // ------------------------------------------------------------------
    // Revision (the AI door's Understood gate, re-submitted)
    // ------------------------------------------------------------------

    /// <summary>
    /// Re-runs the same resolution pipeline over an existing draft
    /// (the AI door's Fix-it loop: the student corrected the car,
    /// the type, or the term on Understood and submitted again).
    /// Campus and the category snapshot never move — both were set
    /// at creation. An already-live or PayPal-order-bound application is never revised.
    /// A revision that lands on $0 issues in the same flow.
    /// </summary>
    public async Task<CreateApplicationResult> ReviseDraftAsync(
        Guid applicationId, ApplicationInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var application = await _db.PermitApplications
            .SingleOrDefaultAsync(a => a.Id == applicationId, cancellationToken);
        if (application is null)
        {
            return new CreateApplicationResult(
                CreateApplicationStatus.NotFound, null,
                "That draft is gone — start again.");
        }

        if (application.ReceiptNumber is not null ||
            application.PaymentStatus == PaymentStatus.Paid)
        {
            return new CreateApplicationResult(
                CreateApplicationStatus.AlreadyLive, application,
                "That permit is already live — it cannot be changed here.");
        }

        // The saved order identifies this exact purchase. Even a same-price
        // vehicle/date edit would change what its eventual capture authorizes.
        // Resume that order instead of reusing it for a revised application.
        if (!string.IsNullOrWhiteSpace(application.PayPalOrderId))
            return PaymentStarted(application);

        var fee = _catalog.Find(application.Campus);
        if (fee is null)
        {
            return new CreateApplicationResult(
                CreateApplicationStatus.CampusNotFound, null,
                $"'{application.Campus}' is not one of Solvia's colleges.");
        }

        var type = await ActiveTypeAsync(input.TypeCode, cancellationToken);
        if (type is null)
        {
            return new CreateApplicationResult(
                CreateApplicationStatus.TypeUnavailable, null,
                "That permit type is not available right now.");
        }

        var end = ComputeEndDate(fee.Campus, input, type);

        var twin = await FindTwinAsync(
            application.StudentId, input.VehicleId, type.Id,
            input.StartDate, end, excludeId: application.Id, cancellationToken);
        if (twin is not null)
        {
            return new CreateApplicationResult(
                CreateApplicationStatus.Duplicate, twin, DuplicateMessage);
        }

        // Price reads the SNAPSHOT category (item 14): a revision
        // never re-resolves who the buyer is.
        var price = _pricing.ResolvePriceCents(
            fee, input.TypeCode, input.Term, application.Category);

        application.VehicleId = input.VehicleId;
        application.PermitTypeId = type.Id;
        application.Term = input.Term;
        application.Source = input.Source;
        application.StartDate = input.StartDate;
        application.EndDate = end;
        application.PriceCents = price ?? 0;
        if (price == 0)
        {
            application.PaymentStatus = PaymentStatus.NotRequired;
            await _issuance.IssueIfAbsentAsync(application, cancellationToken);
        }
        else if (price is not null &&
                 application.PaymentStatus == PaymentStatus.NotRequired)
        {
            // Was parked free/blocked, now priced: back to awaiting
            // payment. (An issued row never reaches this branch —
            // the AlreadyLive guard above stops it.)
            application.PaymentStatus = PaymentStatus.Pending;
        }

        application.ConcurrencyToken = Guid.NewGuid();
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Checkout can bind an order after this context loaded the draft.
            // Its token wins; discard our attempted edits and return the saved
            // purchase for resumption, rather than leaving a stale tracked row.
            _db.Entry(application).State = EntityState.Detached;
            var current = await _db.PermitApplications.AsNoTracking()
                .SingleOrDefaultAsync(a => a.Id == applicationId, cancellationToken);
            if (current?.ReceiptNumber is not null || current?.PaymentStatus == PaymentStatus.Paid)
                return new CreateApplicationResult(CreateApplicationStatus.AlreadyLive, current,
                    "That permit is already live — it cannot be changed here.");
            if (current is not null && !string.IsNullOrWhiteSpace(current.PayPalOrderId))
                return PaymentStarted(current);
            throw;
        }
        catch (DbUpdateException)
        {
            _db.Entry(application).State = EntityState.Detached;
            var winner = await FindTwinAsync(
                application.StudentId, input.VehicleId, type.Id,
                input.StartDate, end, excludeId: application.Id, cancellationToken);
            if (winner is not null)
            {
                return new CreateApplicationResult(
                    CreateApplicationStatus.Duplicate, winner, DuplicateMessage);
            }

            throw;
        }

        return new CreateApplicationResult(
            CreateApplicationStatus.Created, application, null);
    }

    // ------------------------------------------------------------------
    // The AI door's vehicle resolution
    // ------------------------------------------------------------------

    /// <summary>
    /// Resolves the Understood screen's car facts to a vehicle row
    /// (the frozen build's SaveDraft behavior): the person's own row
    /// with the same plate is reused and updated with the corrected
    /// facts; otherwise a new row is created with the owner stamped
    /// on the server. The garage stays the only place vehicles are
    /// MANAGED — this is the flow resolving its own sentence.
    /// </summary>
    public async Task<Vehicle> ResolveVehicleForDraftAsync(
        string studentId,
        string plate,
        string? make,
        string? model,
        string? colorName,
        string? stateCode,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizePlate(plate);
        // SQLite does not enforce HasMaxLength. Validate every supplied
        // vehicle detail before loading or mutating a tracked vehicle.
        var normalizedMake = BoundedVehicleDetail(make, nameof(make));
        var normalizedModel = BoundedVehicleDetail(model, nameof(model));
        var state = ParseState(stateCode);
        var color = ParseColor(colorName);
        var vehicle = await _db.Vehicles.FirstOrDefaultAsync(
            v => v.StudentId == studentId && v.Plate == normalized,
            cancellationToken);
        if (vehicle is not null && vehicle.State != state &&
            await _db.PermitApplications.AnyAsync(application => application.VehicleId == vehicle.Id, cancellationToken))
        {
            // Match the garage: any permit reference freezes plate/state.
            // Make, model and color remain editable when the state is kept.
            throw new DraftVehicleStateLockedException();
        }
        if (vehicle is null)
        {
            vehicle = new Vehicle
            {
                Id = Guid.NewGuid(),
                StudentId = studentId,
            };
            _db.Vehicles.Add(vehicle);
        }

        vehicle.Plate = normalized;
        vehicle.State = state;
        vehicle.Color = color;
        vehicle.Make = normalizedMake;
        vehicle.Model = normalizedModel;
        await _db.SaveChangesAsync(cancellationToken);
        return vehicle;
    }

    // ------------------------------------------------------------------
    // Shared internals — one rule per concern
    // ------------------------------------------------------------------

    /// <summary>Duvara's duplicate meaning, in Solvia's words (item 15).</summary>
    public const string DuplicateMessage =
        "You have already submitted an application for this permit, " +
        "this car, and these dates — duplicates are not allowed. " +
        "Find the one you already sent under My permits.";

    public const string PaymentStartedMessage =
        "PayPal checkout has already started for this application. Resume its existing checkout; " +
        "the vehicle, permit and price cannot be changed.";

    private static CreateApplicationResult PaymentStarted(PermitApplication application) =>
        new(CreateApplicationStatus.PaymentStarted, application, PaymentStartedMessage);

    private async Task<PermitType?> ActiveTypeAsync(
        PermitTypeCode code, CancellationToken cancellationToken) =>
        await _db.PermitTypes.SingleOrDefaultAsync(
            t => t.Code == code && t.Active, cancellationToken);

    /// <summary>
    /// The end-date rule (§2c): Daily ends the day it starts; Term
    /// ends at the term window's PermitEndDate for (campus, term,
    /// the start's year). The type row's DurationDays is only a
    /// defensive fallback if the dates seam ever misses a campus
    /// the catalog knows.
    /// </summary>
    private DateOnly ComputeEndDate(
        string campusName, ApplicationInput input, PermitType type)
    {
        if (input.TypeCode == PermitTypeCode.Daily)
        {
            return input.StartDate;
        }

        var dates = _termDates.GetTermDates(campusName, input.Term, input.StartDate.Year);
        return dates?.PermitEndDate ?? input.StartDate.AddDays(type.DurationDays);
    }

    private Task<PermitApplication?> FindTwinAsync(
        string studentId, Guid vehicleId, int permitTypeId,
        DateOnly start, DateOnly end, Guid? excludeId,
        CancellationToken cancellationToken) =>
        _db.PermitApplications.FirstOrDefaultAsync(
            a => a.StudentId == studentId &&
                 a.VehicleId == vehicleId &&
                 a.PermitTypeId == permitTypeId &&
                 a.StartDate == start &&
                 a.EndDate == end &&
                 (excludeId == null || a.Id != excludeId.Value),
            cancellationToken);

    /// <summary>
    /// The blocked-price sentence (items 11/16): it NAMES the campus
    /// and the exact price Solvia could not find — the honest form
    /// of the reference app's pricing-configuration error.
    /// </summary>
    private static string UnpublishedMessage(string campus, ApplicationInput input) =>
        input.TypeCode == PermitTypeCode.Daily
            ? $"{campus} does not publish a day-pass price, so a daily " +
              "permit cannot be priced. Pick a term permit, or another college."
            : $"{campus} does not publish a {input.Term} semester price, " +
              "so this permit cannot be priced. Pick another term, or another college.";

    private void Detach(PermitApplication application)
    {
        var entry = _db.Entry(application);
        if (entry.State != EntityState.Detached)
        {
            entry.State = EntityState.Detached;
        }
    }

    /// <summary>
    /// Plate normalization, the garage's one rule: trim, drop inner
    /// spaces, uppercase. Callers gate the blank case first (the
    /// Understood gate), so this never sees an empty plate.
    /// </summary>
    private static string NormalizePlate(string plate) =>
        plate.Replace(" ", "").Trim().ToUpperInvariant();

    private static VehicleColor ParseColor(string? colorName) =>
        ParseNamedEnum(colorName, VehicleColor.Other, nameof(colorName), "Choose a color from the list.");

    private static UsState ParseState(string? stateCode) =>
        ParseNamedEnum(stateCode, UsState.CA, nameof(stateCode), "Choose a plate state from the list.");

    private static TEnum ParseNamedEnum<TEnum>(string? value, TEnum defaultValue, string parameter, string error)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;

        // Enum.TryParse alone also accepts numeric strings, including values
        // with no declared name. Accept only a single actual enum name.
        var name = Enum.GetNames<TEnum>().FirstOrDefault(candidate =>
            string.Equals(candidate, value.Trim(), StringComparison.OrdinalIgnoreCase));
        return name is not null ? Enum.Parse<TEnum>(name) : throw new ArgumentException(error, parameter);
    }

    private static string? BoundedVehicleDetail(string? value, string parameter)
    {
        var detail = NullIfBlank(value);
        if (detail?.Length > 50)
            throw new ArgumentException("Vehicle make and model must be 50 characters or fewer.", parameter);
        return detail;
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
