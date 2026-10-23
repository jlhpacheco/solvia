using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Solvia.Models.Enums;
using Solvia.Services;

namespace Solvia.Models.ViewModels;

public sealed class PlannerViewModel
{
    public string Campus { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public bool SingleVisit { get; set; }
    [Range(1, 26, ErrorMessage = "Choose 1 to 26 weeks.")]
    public int? Weeks { get; set; }
    public List<DayOfWeek> Weekdays { get; set; } = [];
    public TermKind Term { get; set; } = TermKind.Fall;
    [StringLength(500, ErrorMessage = "Keep your schedule to 500 characters or fewer.")]
    public string? ScheduleText { get; set; }
    [BindNever] public string? ReaderNote { get; set; }
    [BindNever] public string? ReaderSource { get; set; }
    [BindNever] public IReadOnlyList<string> Campuses { get; set; } = [];
    [BindNever] public string PersonName { get; set; } = "";
    [BindNever] public string PersonId { get; set; } = "";
    [BindNever] public string CategoryLabel { get; set; } = "";
    [BindNever] public PlanResult? Result { get; set; }

    public PlanRequest ToRequest() => SingleVisit
        ? new(Campus, StartDate, 1, [StartDate.DayOfWeek], Term)
        : new(Campus, StartDate, Weeks ?? 0, Weekdays, Term);

    // Detect edits made after a comparison, including with JavaScript disabled.
    // This is a freshness check, not a price/authorization token; Continue
    // independently resolves the person, availability and prices on the server.
    [BindNever] public string ComparisonKey => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            PersonId, Campus, StartDate, SingleVisit, Term,
            Weeks = SingleVisit ? 1 : Weeks,
            Weekdays = SingleVisit ? new[] { StartDate.DayOfWeek } : Weekdays.OrderBy(day => day).ToArray()
        }))));
}
