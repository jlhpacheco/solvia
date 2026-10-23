using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Solvia.Data;
using Solvia.Models.Enums;
using Solvia.Models.ViewModels;
using Solvia.Services;

namespace Solvia.Controllers;

/// <summary>Reviewable schedule drafting and read-only price comparison. No payment tools.</summary>
public sealed class PlannerController(SolviaDbContext db, IFeeCatalog catalog,
    VisitPlannerService planner, IVisitScheduleReader reader, UserCategoryResolver categories,
    AiRequestLimiter limiter) : DemoControllerBase
{
    private const string DraftKey = "Solvia.PlannerDraft";

    [HttpGet]
    public async Task<IActionResult> Index(string? campus, CancellationToken cancellationToken, bool showComparison = false)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var person = await CurrentStudentAsync(db, cancellationToken);
        var model = ReadDraft(person.Id) ?? new PlannerViewModel
        {
            Campus = catalog.Find(campus ?? "Fresno City College")?.Campus ?? "Fresno City College",
            StartDate = today,
            Weeks = 3,
            Weekdays = [DayOfWeek.Tuesday, DayOfWeek.Thursday],
            Term = today.Month <= 5 ? TermKind.Spring : today.Month <= 7 ? TermKind.Summer : TermKind.Fall
        };
        if (campus is not null && catalog.Find(campus) is { } selected)
            model.Campus = selected.Campus;
        var category = await Populate(model, cancellationToken);
        NormalizeSingleVisit(model);
        if (showComparison)
        {
            model.Result = planner.Compare(model.ToRequest(), category);
            foreach (var error in model.Result.Errors) ModelState.AddModelError("", error);
        }
        if (TempData["PlannerComparisonNotice"] is string notice)
            ModelState.AddModelError("", notice);
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Read(PlannerViewModel model, string? draftOwner, CancellationToken cancellationToken)
    {
        await Populate(model, cancellationToken);
        if (!IsCurrentOwner(model, draftOwner)) return StalePerson();
        // A nonblank read is a new interpretation, not confirmation of an old
        // single-visit mode. Errors/uncertainty must not reuse that old mode.
        if (!string.IsNullOrWhiteSpace(model.ScheduleText))
        {
            model.SingleVisit = false;
            ModelState.Remove(nameof(model.SingleVisit));
        }
        SaveDraft(model);
        if (string.IsNullOrWhiteSpace(model.ScheduleText))
        {
            NormalizeSingleVisit(model);
            model.ReaderSource = "manual";
            model.ReaderNote = "No description is needed. Enter your schedule directly below.";
            SaveDraft(model);
            if (ModelState.IsValid) return RedirectToAction(nameof(Index));
            return View("Index", model);
        }
        if (model.ScheduleText.Length > 500)
        {
            ModelState.AddModelError(nameof(model.ScheduleText), "Describe the weekdays and number of weeks in 500 characters or fewer.");
            return View("Index", model);
        }
        var client = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!limiter.TryAcquire(client, out var lease, out var retry))
        {
            Response.StatusCode = 429;
            Response.Headers.RetryAfter = retry.ToString(CultureInfo.InvariantCulture);
            ModelState.AddModelError("", "Too many schedule-reading requests right now. Wait briefly and try again. Your schedule is still here.");
            return View("Index", model);
        }
        using var admission = lease;
        var draft = await reader.ReadAsync(model.ScheduleText, cancellationToken);
        model.Weeks = draft.Weeks;
        model.Weekdays = draft.Weekdays.ToList();
        model.SingleVisit = draft.SingleVisit && draft.StartDate is not null;
        ModelState.Remove(nameof(model.SingleVisit));
        if (draft.StartDate is { } statedDate)
        {
            // A date explicitly grounded in the new sentence replaces the
            // old form value. Without one, preserve the person's date and
            // any unrelated date-validation error.
            model.StartDate = statedDate;
            ModelState.Remove(nameof(model.StartDate));
        }
        model.ReaderSource = draft.Source;
        model.ReaderNote = draft.SourceNote + " " + draft.Notice;
        // Replace only the reader-owned fields, preserving unrelated errors
        // and attempted values (for example an invalid planning date).
        ModelState.Remove(nameof(model.Weeks));
        ModelState.Remove(nameof(model.Weekdays));
        NormalizeSingleVisit(model);
        SaveDraft(model);
        if (ModelState.IsValid) return RedirectToAction(nameof(Index));
        return View("Index", model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Compare(PlannerViewModel model, string? draftOwner, CancellationToken cancellationToken)
    {
        var category = await Populate(model, cancellationToken);
        if (!IsCurrentOwner(model, draftOwner)) return StalePerson();
        NormalizeSingleVisit(model);
        SaveDraft(model);
        if (ModelState.IsValid)
        {
            model.Result = planner.Compare(model.ToRequest(), category);
            foreach (var error in model.Result.Errors) ModelState.AddModelError("", error);
            if (ModelState.IsValid) return RedirectToAction(nameof(Index), new { showComparison = true });
        }
        return View("Index", model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Continue(PlannerViewModel model, string? choice, string? comparedPlan, string? draftOwner, CancellationToken cancellationToken)
    {
        var category = await Populate(model, cancellationToken);
        if (!IsCurrentOwner(model, draftOwner)) return StalePerson();
        NormalizeSingleVisit(model);
        SaveDraft(model);
        if (ModelState.IsValid)
        {
            model.Result = planner.Compare(model.ToRequest(), category);
            foreach (var error in model.Result.Errors) ModelState.AddModelError("", error);
            if (model.Result.IsValid && !string.Equals(comparedPlan, model.ComparisonKey, StringComparison.Ordinal))
            {
                TempData["PlannerComparisonNotice"] = "Your plan changed. The comparison below has been refreshed. Check it before continuing.";
                return RedirectToAction(nameof(Index), new { showComparison = true });
            }
            var permitted = model.Result.IsValid && model.Result.FirstVisit is not null &&
                ((choice == "Daily" && model.Result.DayCents is not null) ||
                 (choice == "Term" && model.Result.TermAvailable && model.Result.TermCents is not null));
            if (permitted)
                return RedirectToAction("Questions", "Permits", new
                {
                    college = model.Result.Campus, permitType = choice, term = model.Term.ToString(),
                    startDate = model.Result.FirstVisit!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                });
            ModelState.AddModelError("", "Choose an available permit option after checking your schedule.");
        }
        return View("Index", model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetVisitType(PlannerViewModel model, string? draftOwner,
        CancellationToken cancellationToken)
    {
        await Populate(model, cancellationToken);
        if (!IsCurrentOwner(model, draftOwner)) return StalePerson();
        NormalizeSingleVisit(model);
        if (model.StartDate == default)
            ModelState.AddModelError(nameof(model.StartDate), "Choose a valid planning date.");
        SaveDraft(model);
        return ModelState.IsValid ? RedirectToAction(nameof(Index)) : View("Index", model);
    }

    private void NormalizeSingleVisit(PlannerViewModel model)
    {
        if (!model.SingleVisit) return;
        model.Weeks = 1;
        model.Weekdays = [model.StartDate.DayOfWeek];
        // Hidden recurrence fields are not inputs in single mode. Preserve
        // date, owner and all unrelated validation instead of clearing it.
        foreach (var key in ModelState.Keys.Where(key => new[] { nameof(model.Weeks), nameof(model.Weekdays) }
                     .Any(field => key.Equals(field, StringComparison.OrdinalIgnoreCase) ||
                         key.StartsWith(field + "[", StringComparison.OrdinalIgnoreCase) ||
                         key.StartsWith(field + ".", StringComparison.OrdinalIgnoreCase))).ToArray())
            ModelState.Remove(key);
    }

    private async Task<UserCategory> Populate(PlannerViewModel model, CancellationToken cancellationToken)
    {
        var person = await CurrentStudentAsync(db, cancellationToken);
        model.PersonName = person.DisplayName;
        model.PersonId = person.Id;
        var category = categories.Resolve(person);
        model.CategoryLabel = category.ToString();
        model.Campuses = catalog.All().Select(f => f.Campus).ToList();
        model.Weekdays ??= [];
        // Retain server-recorded provenance only for the same sentence. Client
        // source fields are never bound, and changed prose cannot inherit it.
        var retained = ReadDraft(person.Id);
        var sameText = retained is not null && string.Equals(retained.ScheduleText, model.ScheduleText, StringComparison.Ordinal);
        model.ReaderNote = sameText ? retained!.ReaderNote : null;
        model.ReaderSource = sameText ? retained!.ReaderSource : null;
        model.Result = null;
        return category;
    }

    // Cookie TempData is authenticated/encrypted by ASP.NET Core. Store only
    // this small draft, never prices, AI provenance, payment IDs or authority.
    // Peek keeps it available when the applicant follows Change the plan or
    // revisits the planner. It cannot leak into a different demo person's form.
    private PlannerViewModel? ReadDraft(string owner)
    {
        if (TempData.Peek(DraftKey) is not string encoded) return null;
        try
        {
            using var compressed = new MemoryStream(Convert.FromBase64String(encoded));
            using var inflater = new BrotliStream(compressed, CompressionMode.Decompress);
            using var json = new MemoryStream();
            var buffer = new byte[1024];
            int read;
            while ((read = inflater.Read(buffer)) > 0)
            {
                if (json.Length + read > 8192) return null;
                json.Write(buffer, 0, read);
            }
            var draft = JsonSerializer.Deserialize<PlannerDraft>(json.ToArray());
            if (draft is null || draft.Owner != owner || draft.SavedAt < DateTimeOffset.UtcNow.AddHours(-2)) return null;
            return new PlannerViewModel
            {
                Campus = draft.Campus, StartDate = draft.StartDate, Weeks = draft.Weeks,
                Weekdays = draft.Weekdays ?? [], Term = draft.Term, ScheduleText = draft.ScheduleText,
                ReaderSource = draft.ReaderSource, ReaderNote = draft.ReaderNote,
                // Old protected snapshots had no mode. Infer only their
                // already-single exact date; an explicit new false is kept.
                SingleVisit = draft.SingleVisit ?? (draft.StartDate != default && draft.Weeks == 1 &&
                    draft.Weekdays is { Count: 1 } && draft.Weekdays[0] == draft.StartDate.DayOfWeek)
            };
        }
        catch (JsonException) { return null; }
        catch (FormatException) { return null; }
        catch (InvalidDataException) { return null; }
    }

    private void SaveDraft(PlannerViewModel model)
    {
        if (model.StartDate == default || model.ScheduleText?.Length > 500 || model.Weekdays.Count > 7 || string.IsNullOrEmpty(model.Campus) || model.Campus.Length > 150) return;
        using var compressed = new MemoryStream();
        using (var writer = new BrotliStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            JsonSerializer.Serialize(writer, new PlannerDraft(model.PersonId, DateTimeOffset.UtcNow,
                model.Campus, model.StartDate, model.Weeks, model.Weekdays, model.Term, model.ScheduleText,
                model.ReaderSource, model.ReaderNote, model.SingleVisit));
        TempData[DraftKey] = Convert.ToBase64String(compressed.ToArray());
    }

    private sealed record PlannerDraft(string Owner, DateTimeOffset SavedAt, string Campus,
        DateOnly StartDate, int? Weeks, List<DayOfWeek> Weekdays, TermKind Term, string? ScheduleText,
        string? ReaderSource, string? ReaderNote, bool? SingleVisit = null);

    private static bool IsCurrentOwner(PlannerViewModel model, string? owner) =>
        string.Equals(model.PersonId, owner, StringComparison.Ordinal);

    private IActionResult StalePerson()
    {
        TempData["PlannerNotice"] = "The demo person changed. Start a new plan for the person shown below; the previous person's entries were not copied.";
        return RedirectToAction(nameof(Index));
    }
}
