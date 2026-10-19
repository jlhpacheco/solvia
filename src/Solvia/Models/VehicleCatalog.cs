namespace Solvia.Models;

/// <summary>
/// The garage form's make/model catalog — Duvara-style dropdowns
/// instead of free-typing: a small built-in list of common makes,
/// each with its common models, offered as cascading selects.
/// Off-catalog values still work: the form's "Other — type it"
/// fallback keeps an extracted or unusual model enterable, and
/// existing off-catalog values are re-offered, never dropped.
/// </summary>
public static class VehicleCatalog
{
    /// <summary>Makes offered by the garage form, in display order.</summary>
    public static readonly IReadOnlyList<string> Makes =
    [
        "Honda", "Toyota", "Ford", "Chevrolet", "Nissan", "Tesla",
        "BMW", "Volkswagen", "Hyundai", "Kia", "Subaru", "Jeep",
        "Dodge", "Mazda", "Other",
    ];

    /// <summary>Common models per make (the cascade's second level).</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> ModelsByMake =
        new Dictionary<string, IReadOnlyList<string>>
        {
            ["Honda"] = ["Civic", "Accord", "CR-V", "HR-V", "Pilot", "Odyssey"],
            ["Toyota"] = ["Camry", "Corolla", "RAV4", "Highlander", "Tacoma", "Prius"],
            ["Ford"] = ["F-150", "Escape", "Explorer", "Mustang", "Bronco", "Edge"],
            ["Chevrolet"] = ["Silverado", "Equinox", "Traverse", "Tahoe", "Malibu", "Colorado"],
            ["Nissan"] = ["Altima", "Sentra", "Rogue", "Frontier", "Leaf", "Pathfinder"],
            ["Tesla"] = ["Model 3", "Model Y", "Model S", "Model X", "Cybertruck"],
            ["BMW"] = ["3 Series", "5 Series", "X3", "X5", "X1"],
            ["Volkswagen"] = ["Jetta", "Golf", "Tiguan", "Atlas", "Taos"],
            ["Hyundai"] = ["Elantra", "Sonata", "Kona", "Tucson", "Santa Fe", "Palisade"],
            ["Kia"] = ["Forte", "K5", "Soul", "Sportage", "Sorento", "Telluride"],
            ["Subaru"] = ["Impreza", "Crosstrek", "Forester", "Outback", "Legacy"],
            ["Jeep"] = ["Wrangler", "Compass", "Cherokee", "Grand Cherokee", "Gladiator"],
            ["Dodge"] = ["Charger", "Challenger", "Durango"],
            ["Mazda"] = ["Mazda3", "Mazda6", "CX-30", "CX-5", "CX-50", "MX-5 Miata"],
            ["Other"] = [],
        };
}
