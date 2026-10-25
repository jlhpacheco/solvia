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
/// College-side pages (REDO-PLAN R8). The roster — "who paid / who
/// hasn't" for one campus — carries Duvara's three admin tiles and
/// the "By student" section (items 53–54) and hosts the price-repair
/// action (item 55). The fee table and term dates render through
/// their seams (§2e), and the find-your-college search names the
/// provider that served it. The roster reads PermitApplications
/// only: a student with no application at the campus shows NO
/// PERMIT, never invented data.
/// </summary>
public sealed class CollegeController : Controller
{
    private readonly IFeeCatalog _catalog;
    private readonly ITermDatesService _termDates;
    private readonly ICollegeSearch _search;
    private readonly PriceRepairService _priceRepair;
    private readonly FeeTableOptions _feeTable;
    private readonly CalendarOptions _calendar;
    private readonly SolviaDbContext _db;

    public CollegeController(
        IFeeCatalog catalog,
        ITermDatesService termDates,
        ICollegeSearch search,
        PriceRepairService priceRepair,
        IOptions<FeeTableOptions> feeTable,
        IOptions<CalendarOptions> calendar,
        SolviaDbContext db)
    {
        _catalog = catalog;
        _termDates = termDates;
        _search = search;
        _priceRepair = priceRepair;
        _feeTable = feeTable.Value;
        _calendar = calendar.Value;
        _db = db;
    }

    // ------------------------------------------------------------------
    // Roster (items 53–54)
    // ------------------------------------------------------------------

    /// <summary>
    /// The roster for one campus: every seeded student with their
    /// permit state in big plain words — PAID (permit number +
    /// dates), FREE — ISSUED, PENDING (started, not paid), or NO
    /// PERMIT — under the three admin tiles, with the "By student"
    /// section below.
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

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "America/Los_Angeles"));
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

        // Duvara's three admin tiles (item 53), computed from this
        // campus's rows only. "Issued" is a payment truth — the row
        // settled (Paid or NotRequired) — the same set Classify
        // treats as holding a permit.
        var issued = applications
            .Where(a => a.Status != ApplicationStatus.Denied &&
                        a.PaymentStatus is PaymentStatus.Paid or PaymentStatus.NotRequired)
            .ToList();

        // "By student" (item 54): issued permits per seeded student
        // at this campus, most first; ties keep name order.
        var byStudent = students
            .Select(student =>
            {
                var mine = issued
                    .Where(a => a.StudentId == student.Id)
                    .OrderBy(a => a.SubmittedAtUtc)
                    .ToList();
                return new StudentPermitSummary(
                    StudentName: student.DisplayName,
                    PermitCount: mine.Count,
                    ReceiptNumbers: mine
                        .Select(a => a.ReceiptNumber)
                        .OfType<string>()
                        .ToList());
            })
            .OrderByDescending(s => s.PermitCount)
            .ThenBy(s => s.StudentName)
            .ToList();

        return View(new RosterViewModel
        {
            Campus = fee.Campus,
            Rows = rows,
            Campuses = _catalog.All().Select(f => f.Campus).ToList(),
            IssuedCount = issued.Count,
            RevenueCents = issued
                .Where(a => a.PaymentStatus == PaymentStatus.Paid)
                .Sum(a => (long)a.PriceCents),
            ExpiredCount = issued.Count(a => a.EndDate < today),
            ByStudent = byStudent,
            RepairReport = ReadRepairReport(),
        });
    }

    private static string StateLabel(RosterPermitState state) => state switch
    {
        RosterPermitState.Paid => "PAID",
        RosterPermitState.FreeIssued => "FREE — ISSUED",
        RosterPermitState.Pending => "PENDING",
        _ => "NO PERMIT",
    };

    // ------------------------------------------------------------------
    // Price repair (item 55)
    // ------------------------------------------------------------------

    /// <summary>
    /// Runs the price repair over this campus's Pending applications
    /// and lands back on the roster with the outcome as a banner
    /// list: every price put back, every row skipped and why.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RepairPrices(
        string campus, CancellationToken cancellationToken = default)
    {
        var fee = _catalog.Find(campus);
        if (fee is null)
        {
            return NotFound($"Campus '{campus}' is not in the fee catalog.");
        }

        var report = await _priceRepair.RepairPendingAsync(fee.Campus, cancellationToken);

        var lines = new List<string>
        {
            $"Price check for {fee.Campus}: {report.Changes.Count} price(s) repaired, " +
            $"{report.Skipped.Count} skipped, {report.UnchangedCount} already matched the published fee.",
        };
        lines.AddRange(report.Changes.Select(change =>
            $"Repaired application {change.ApplicationId} ({change.StudentId}): " +
            $"{Money(change.OldPriceCents)} → {Money(change.NewPriceCents)} " +
            "(the college's published fee)."));
        lines.AddRange(report.Skipped.Select(skip =>
            $"Skipped application {skip.ApplicationId} ({skip.StudentId}): {skip.Reason}"));
        TempData[RepairReportKey] = string.Join("\n", lines);

        return RedirectToAction(nameof(Roster), new { campus = fee.Campus });
    }

    private const string RepairReportKey = "Solvia.RepairReport";

    private IReadOnlyList<string>? ReadRepairReport() =>
        TempData[RepairReportKey] is string text && text.Length > 0
            ? text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            : null;

    // ------------------------------------------------------------------
    // Fee table (§2e seam)
    // ------------------------------------------------------------------

    /// <summary>Fee table page: all 10 campuses through the configured renderer.</summary>
    public IActionResult Fees()
    {
        var renderer = string.Equals(_feeTable.Renderer, "aggrid",
            StringComparison.OrdinalIgnoreCase) ? "aggrid" : "native";

        var rows = _catalog.All().Select(ToRow).ToList();
        return View(new FeeTableViewModel { Renderer = renderer, Rows = rows });
    }

    private FeeTableRow ToRow(CampusFee fee)
    {
        var cents = _catalog.SemesterPriceCents(fee, TermKind.Fall);
        var semesterPrice = fee.IsFree
            ? "$0 FREE (term only)"
            : cents is null
                ? "Not published"
                : Money(cents.Value);

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

    // ------------------------------------------------------------------
    // Term dates (calendar seam)
    // ------------------------------------------------------------------

    /// <summary>Term-dates page: the calendar seam's data in plain big text.</summary>
    public IActionResult Dates(string campus = "Fresno City College", string term = "Fall", int year = 2026)
    {
        var dates = _termDates.GetTermDates(campus, ParseTerm(term), year);
        if (dates is null)
        {
            return NotFound($"Campus '{campus}' is not in the fee catalog.");
        }

        // The calendar seam (§2e): "native" is what ships — Bryntum
        // Calendar has NO free community edition (paid license or a
        // 45-day trial that would expire during judging), so the
        // native renderer ships and "bryntum" stays the drop-in
        // point for a licensed build. docs/SPONSORS.md records it.
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

    // ------------------------------------------------------------------
    // Find your college (search seam)
    // ------------------------------------------------------------------

    /// <summary>
    /// The find-your-college screen: ranked campuses from the
    /// configured search provider, with the provider that ACTUALLY
    /// served them labeled on-screen — if Elastic was configured but
    /// down, the built-in match answered and the page says so.
    /// </summary>
    public async Task<IActionResult> Search(
        string? q, CancellationToken cancellationToken = default)
    {
        var result = await _search.SearchAsync(q, cancellationToken);
        var rows = result.Campuses.Select(fee => new CollegeSearchRow(
            Campus: fee.Campus,
            SemesterPriceLabel: SemesterPriceLabel(fee))).ToList();

        return View(new CollegeSearchViewModel
        {
            Query = q,
            ServedBy = result.ServedBy,
            Rows = rows,
        });
    }

    private string SemesterPriceLabel(CampusFee fee)
    {
        var cents = _catalog.SemesterPriceCents(fee, TermKind.Fall);
        return fee.IsFree
            ? "$0 — FREE"
            : cents is not null
                ? $"{Money(cents.Value)} per term"
                : "Price not published — ask your college";
    }

    private static string Money(int cents) =>
        (cents / 100m).ToString("C", CultureInfo.GetCultureInfo("en-US"));
}
