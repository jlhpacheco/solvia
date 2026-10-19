namespace Solvia.Models.ViewModels;

/// <summary>
/// Home screen (R0 shell): the demo-person switcher + the big doors.
/// (R1 owns Models/** — this home view model is the R0 stub R1
/// absorbs; R2 owns VehicleViewModels.cs and R4 owns
/// FlowViewModels.cs as separate files, per REDO-PLAN §3.)
/// </summary>
public sealed class HomeIndexViewModel
{
    public required IReadOnlyList<Student> Students { get; init; }

    public required string CurrentStudentId { get; init; }

    public required string CurrentStudentName { get; init; }
}
