namespace Solvia.Models;

/// <summary>
/// The garage's make/model catalog (R2-owned, REDO-PLAN §3): the
/// dropdown source for the vehicle form, in the reference app's
/// shape — pick a Make and the Model list cascades to that make's
/// models. This is a convenience list, never a whitelist: the form
/// also offers an "Other — type it" escape for both fields, and any
/// off-catalog value already stored on a vehicle is re-offered in
/// the dropdown so it is never silently dropped.
/// </summary>
public static class VehicleCatalog
{
    /// <summary>The make the catalog uses when a car is none of the listed makes.</summary>
    public const string OtherMake = "Other";

    /// <summary>
    /// Posted value of the Make dropdown's "Other — type it" option.
    /// The controller resolves it to the text typed in the MakeOther
    /// box (or to <see cref="OtherMake"/> when the box is left blank).
    /// </summary>
    public const string OtherMakeValue = "__solvia_other_make__";

    /// <summary>
    /// Posted value of the Model dropdown's "Other — type it" option.
    /// The controller resolves it to the text typed in the ModelOther
    /// box (blank means no model recorded).
    /// </summary>
    public const string OtherModelValue = "__solvia_other_model__";

    private static readonly IReadOnlyDictionary<string, string[]> ModelsByMakeMap =
        new Dictionary<string, string[]>
        {
            ["Honda"] = ["Accord", "Civic", "CR-V", "HR-V", "Odyssey", "Pilot", "Ridgeline"],
            ["Toyota"] = ["Camry", "Corolla", "Highlander", "Prius", "RAV4", "Sienna", "Tacoma"],
            ["Ford"] = ["Bronco", "Edge", "Escape", "Explorer", "F-150", "Mustang", "Ranger"],
            ["Chevrolet"] = ["Colorado", "Equinox", "Malibu", "Silverado 1500", "Suburban", "Tahoe", "Traverse"],
            ["Nissan"] = ["Altima", "Frontier", "Maxima", "Pathfinder", "Rogue", "Sentra"],
            ["Tesla"] = ["Cybertruck", "Model 3", "Model S", "Model X", "Model Y"],
            ["BMW"] = ["3 Series", "5 Series", "X1", "X3", "X5"],
            ["Volkswagen"] = ["Atlas", "Golf", "Jetta", "Passat", "Tiguan"],
            ["Hyundai"] = ["Elantra", "Kona", "Palisade", "Santa Fe", "Sonata", "Tucson"],
            ["Kia"] = ["Forte", "K5", "Sorento", "Soul", "Sportage", "Telluride"],
            ["Subaru"] = ["Ascent", "Crosstrek", "Forester", "Impreza", "Outback"],
            ["Jeep"] = ["Cherokee", "Compass", "Gladiator", "Grand Cherokee", "Wrangler"],
            ["Dodge"] = ["Challenger", "Charger", "Durango", "Hornet"],
            ["Mazda"] = ["CX-5", "CX-50", "CX-9", "Mazda3", "Mazda6", "MX-5 Miata"],
            [OtherMake] = [],
        };

    /// <summary>All makes offered in the dropdown, in display order.</summary>
    public static IReadOnlyList<string> Makes { get; } =
        ModelsByMakeMap.Keys.ToList();

    /// <summary>Make → its models, for the cascading dropdown script.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ModelsByMake { get; } =
        ModelsByMakeMap.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value);

    /// <summary>The catalog models for a make; empty for Other / unknown makes.</summary>
    public static IReadOnlyList<string> ModelsFor(string? make) =>
        make is not null && ModelsByMakeMap.TryGetValue(make, out var models)
            ? models
            : [];

    /// <summary>True when the make is one the catalog lists (including Other).</summary>
    public static bool IsCatalogMake(string? make) =>
        make is not null && ModelsByMakeMap.ContainsKey(make);
}
