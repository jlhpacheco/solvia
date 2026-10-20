using System.Globalization;
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
    [HttpGet]
    public async Task<IActionResult> Index(string? campus, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var model = new PlannerViewModel
        {
            Campus = catalog.Find(campus ?? "Fresno City College")?.Campus ?? "Fresno City College",
            StartDate = today,
            Weeks = 3,
            Weekdays = [DayOfWeek.Tuesday, DayOfWeek.Thursday],
            Term = today.Month <= 5 ? TermKind.Spring : today.Month <= 7 ? TermKind.Summer : TermKind.Fall
        };
        await Populate(model, cancellationToken);
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Read(PlannerViewModel model, CancellationToken cancellationToken)
    {
        await Populate(model, cancellationToken);
        if (string.IsNullOrWhiteSpace(model.ScheduleText) || model.ScheduleText.Length > 500)
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
        model.ReaderSource = draft.Source;
        model.ReaderNote = draft.SourceNote + " " + draft.Notice;
        ModelState.Clear(); // Parsed choices must replace the previous form values for review.
        return View("Index", model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Compare(PlannerViewModel model, CancellationToken cancellationToken)
    {
        var category = await Populate(model, cancellationToken);
        if (ModelState.IsValid)
        {
            model.Result = planner.Compare(model.ToRequest(), category);
            foreach (var error in model.Result.Errors) ModelState.AddModelError("", error);
        }
        return View("Index", model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Continue(PlannerViewModel model, string? choice, CancellationToken cancellationToken)
    {
        var category = await Populate(model, cancellationToken);
        if (ModelState.IsValid)
        {
            model.Result = planner.Compare(model.ToRequest(), category);
            foreach (var error in model.Result.Errors) ModelState.AddModelError("", error);
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

    private async Task<UserCategory> Populate(PlannerViewModel model, CancellationToken cancellationToken)
    {
        var person = await CurrentStudentAsync(db, cancellationToken);
        model.PersonName = person.DisplayName;
        var category = categories.Resolve(person);
        model.CategoryLabel = category.ToString();
        model.Campuses = catalog.All().Select(f => f.Campus).ToList();
        model.Weekdays ??= [];
        // A source label is never accepted from a client; only Read sets it.
        model.ReaderNote = null;
        model.ReaderSource = null;
        model.Result = null;
        return category;
    }
}
