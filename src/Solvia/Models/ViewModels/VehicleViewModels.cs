using Solvia.Models.Enums;
using System.ComponentModel.DataAnnotations;

namespace Solvia.Models.ViewModels;

/// <summary>
/// The garage form (R2-owned, REDO-PLAN §3; FlowViewModels.cs is
/// R4's file, so the vehicle view models live here). One shape serves
/// Create and Edit, and one shared partial renders it, so the two
/// screens can never drift apart. There is deliberately NO owner
/// field: the owner is stamped on the server from the demo person,
/// never trusted from a form.
///
/// Make/Model ride as free strings with two escape hatches
/// (<see cref="VehicleCatalog.OtherMakeValue"/> /
/// <see cref="VehicleCatalog.OtherModelValue"/>): when a dropdown's
/// "Other — type it" option is picked, the matching *Other text box
/// carries what the person typed, and the controller resolves the
/// pair into the single stored value. Off-catalog values are stored
/// exactly as typed — the catalog is a helper, not a gate.
/// </summary>
public sealed class VehicleEditViewModel
{
    /// <summary>The vehicle being edited; empty on the Create form.</summary>
    public Guid Id { get; set; }

    /// <summary>License plate, as typed; normalized on save.</summary>
    [Required, StringLength(20)] public string Plate { get; set; } = "";

    /// <summary>Plate state (defaults to CA, like the model).</summary>
    [EnumDataType(typeof(UsState))] public UsState State { get; set; } = UsState.CA;

    /// <summary>Make: a catalog make, a stored off-catalog make, or the "type it" sentinel.</summary>
    [StringLength(50)] public string? Make { get; set; }

    /// <summary>The typed make, used only when <see cref="Make"/> is the sentinel.</summary>
    [StringLength(50)] public string? MakeOther { get; set; }

    /// <summary>Model: a catalog model for the make, a stored off-catalog model, or the "type it" sentinel.</summary>
    [StringLength(50)] public string? Model { get; set; }

    /// <summary>The typed model, used only when <see cref="Model"/> is the sentinel.</summary>
    [StringLength(50)] public string? ModelOther { get; set; }

    [EnumDataType(typeof(VehicleColor))] public VehicleColor Color { get; set; } = VehicleColor.Other;

    /// <summary>
    /// A plain-language problem to show at the top of the form (the
    /// blank-plate refusal). Null when the form is fine.
    /// </summary>
    public string? Error { get; set; }
}

/// <summary>One row of the garage list.</summary>
public sealed class VehicleListItemViewModel
{
    public Guid Id { get; set; }

    public string Plate { get; set; } = "";

    public UsState State { get; set; }

    public VehicleColor Color { get; set; }

    public string? Make { get; set; }

    public string? Model { get; set; }

    /// <summary>"Plate · State · Make Model" — the same label the Apply door's vehicle dropdown uses (REDO-PLAN item 2).</summary>
    public string Label =>
        string.Join(" · ", new[]
        {
            Plate,
            State.ToString(),
            string.Join(" ", new[] { Make, Model }.Where(s => !string.IsNullOrWhiteSpace(s))),
        }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

/// <summary>The garage list page: the current demo person's own vehicles.</summary>
public sealed class VehicleIndexViewModel
{
    public string StudentName { get; set; } = "";

    public IReadOnlyList<VehicleListItemViewModel> Vehicles { get; set; } = [];
}
