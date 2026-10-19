using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Solvia.Config;
using Solvia.Data;
using Solvia.Models;
using Solvia.Models.Enums;
using Solvia.Models.ViewModels;
using Solvia.Services;

namespace Solvia.Controllers;

/// <summary>
/// College-side pages. T6 built the two seam pages here (the fee table
/// and the term dates); T5 adds the roster — "who paid / who hasn't" —
/// on top of the same controller. The roster reads PermitApplications
/// only: a student with no application at the campus shows NO PERMIT,
/// never invented data.
/// </summary>
public sealed class CollegeController : Controller
{
    private readonly IFeeCatalog _catalog;
    private readonly ITermDatesService _termDates;
    private readonly FeeTableOptions _feeTable;
    private readonly CalendarOptions _calendar;
    private readonly SolviaDbContext _db;

    public CollegeController(
        IFeeCatalog catalog,
        ITermDatesService termDates,
        IOptions<FeeTableOptions> feeTable,
        IOptions<CalendarOptions> calendar,
        SolviaDbContext db)
    {
        _catalog = catalog;
        _termDates = termDates;
        _feeTable = feeTable.Value;
        _calendar = calendar.Value;
        _db = db;
    }

    /// <summary>
    /// The roster for one campus: every seeded student with their
    /// permit state in big plain words — PAID (permit number + dates),
    /// FREE — ISSUED, PENDING (started, not paid), or NO PERMIT.
    /// </summary>
    public async Task<IActionResult> Roster(
        string campus = "Fresno City College",
        CancellationToken cancellationToken = default)
    {
        var fee = _catalog.Find(campus);
        if (fee is null)
        {
            return NotFound($"Campus '{campus}' is not in the fee catalog.");
        }

        var students = await _db.Students
            .OrderBy(s => s.DisplayName)
            .ToListAsync(cancellationToken);
        var applications = await _db.PermitApplications
            .Where(a => a.Campus == fee.Campus)
            .ToListAsync(cancellationToken);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var rows = students.Select(student =>
        {
            var mine = applications
                .Where(a => a.StudentId == student.Id)
                .ToList();
            var (state, permit) = PermitValidity.Classify(mine, today);
            return new RosterRow(
                StudentName: student.DisplayName,
                State: state,
                StateLabel: StateLabel(state),
                PermitNumber: permit?.ReceiptNumber,
                StartDate: permit?.StartDate,
                EndDate: permit?.EndDate);
        }).ToList();

        return View(new RosterViewModel
        {
            Campus = fee.Campus,
            Rows = rows,
            Campuses = _catalog.All().Select(f => f.Campus).ToList(),
        });
    }

    private static string StateLabel(RosterPermitState state) => state switch
    {
        RosterPermitState.Paid => "PAID",
        RosterPermitState.FreeIssued => "FREE — ISSUED",
        RosterPermitState.Pending => "PENDING",
        _ => "NO PERMIT",
    };

    /// <summary>Fee table page: all 10 campuses through the configured renderer.</summary>
    public IActionResult Fees()
    {
        var renderer = string.Equals(_feeTable.Renderer, "aggrid",
            StringComparison.OrdinalIgnoreCase) ? "aggrid" : "native";

        var rows = _catalog.All().Select(ToRow).ToList();
        return View(new FeeTableViewModel { Renderer = renderer, Rows = rows });
    }

    /// <summary>Term-dates page: the calendar seam's data in plain big text.</summary>
    public IActionResult Dates(string campus = "Fresno City College", string term = "Fall", int year = 2026)
    {
        var dates = _termDates.GetTermDates(campus, ParseTerm(term), year);
        if (dates is null)
        {
            return NotFound($"Campus '{campus}' is not in the fee catalog.");
        }

        // The calendar seam (T6 pattern, settled in T5): "native" is
        // what ships — a licensed Bryntum Calendar drops in later by
        // adding wwwroot/js/calendar.bryntum.js and flipping this flag.
        // docs/SPONSORS.md records why Bryntum itself is NOT embedded.
        ViewData["CalendarRenderer"] = string.Equals(
            _calendar.Renderer, "bryntum", StringComparison.OrdinalIgnoreCase)
            ? "bryntum"
            : "native";

        return View(dates);
    }

    /// <summary>The same dates as JSON — the payload the calendar widget consumes.</summary>
    public IActionResult DatesJson(string campus = "Fresno City College", string term = "Fall", int year = 2026)
    {
        var dates = _termDates.GetTermDates(campus, ParseTerm(term), year);
        return dates is null ? NotFound() : Json(dates);
    }

    private static TermKind ParseTerm(string term) =>
        Enum.TryParse<TermKind>(term, ignoreCase: true, out var kind) ? kind : TermKind.Fall;

    private FeeTableRow ToRow(CampusFee fee)
    {
        var cents = _catalog.SemesterPriceCents(fee, TermKind.Fall);
        var semesterPrice = fee.IsFree
            ? "$0 FREE"
            : cents is null
                ? "Not published"
                : (cents.Value / 100m).ToString("C", CultureInfo.GetCultureInfo("en-US"));

        var dayPass = fee.DayPassText.Trim();
        if (dayPass.Length == 0 ||
            dayPass.StartsWith("not listed", StringComparison.OrdinalIgnoreCase))
        {
            dayPass = "Not published";
        }

        return new FeeTableRow(
            Campus: fee.Campus,
            SemesterPrice: semesterPrice,
            SemesterPriceCents: cents,
            DayPass: dayPass,
            SourceNote: fee.SourceNote);
    }
}
