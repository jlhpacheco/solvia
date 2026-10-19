using Solvia.Models.Enums;

namespace Solvia.Models;

/// <summary>
/// A person's car. Owner-scoped: <see cref="StudentId"/> is set on
/// the server, and every lookup must filter by it — a person only
/// ever sees their own vehicles.
/// </summary>
public sealed class Vehicle
{
    public Guid Id { get; set; }

    /// <summary>Owner of this vehicle; set on the server.</summary>
    public string StudentId { get; set; } = "";

    public Student? Student { get; set; }

    /// <summary>License plate — the lookup field on campus.</summary>
    public string Plate { get; set; } = "";

    /// <summary>Plate state; defaults to CA.</summary>
    public UsState State { get; set; } = UsState.CA;

    public VehicleColor Color { get; set; }

    public string? Make { get; set; }

    public string? Model { get; set; }
}
