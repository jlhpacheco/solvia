using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Solvia.Data;
using Solvia.Models;
using Solvia.Models.ViewModels;

namespace Solvia.Controllers;

/// <summary>
/// The vehicle garage (R2, REDO-PLAN §3): the demo person's own
/// cars, in the reference app's CRUD shape — Index / Create / Edit,
/// one shared form, fields in the reference's order (Plate → State →
/// Make → Model → Color).
///
/// The garage is the ONLY place vehicle records are managed
/// (checklist item 57: the Apply form references a vehicle by id
/// only). Every action is owner-scoped to the current demo person:
/// the owner is stamped on the server at Create, every lookup
/// filters by it, and another person's vehicle simply does not
/// exist here (404) — it is never shown and never edited.
/// </summary>
public sealed class VehiclesController : DemoControllerBase
{
    private readonly SolviaDbContext _db;

    public VehiclesController(SolviaDbContext db)
    {
        _db = db;
    }

    // ------------------------------------------------------------ Index

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var student = await CurrentStudentAsync(_db, cancellationToken);
        var vehicles = await _db.Vehicles
            .Where(v => v.StudentId == student.Id)
            .OrderBy(v => v.Plate)
            .ToListAsync(cancellationToken);

        return View(new VehicleIndexViewModel
        {
            StudentName = student.DisplayName,
            Vehicles = vehicles.Select(v => new VehicleListItemViewModel
            {
                Id = v.Id,
                Plate = v.Plate,
                State = v.State,
                Color = v.Color,
                Make = v.Make,
                Model = v.Model,
            }).ToList(),
        });
    }

    // ------------------------------------------------------------ Create

    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        // Resolving the person here also proves the demo cookie maps
        // to a seeded person before the form is offered.
        _ = await CurrentStudentAsync(_db, cancellationToken);
        return View(new VehicleEditViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        VehicleEditViewModel form, CancellationToken cancellationToken)
    {
        var student = await CurrentStudentAsync(_db, cancellationToken);

        if (!ModelState.IsValid) return View(form);
        var plate = NormalizePlate(form.Plate);
        if (plate is null)
        {
            // A blank plate saves nothing — the same form comes back
            // with everything else the person typed still in it.
            form.Error = "Please type the plate — a vehicle cannot be saved without one.";
            return View(form);
        }

        var vehicle = new Vehicle
        {
            Id = Guid.NewGuid(),
            // The owner is the demo person from the server-side
            // switcher — never a value from the form.
            StudentId = student.Id,
            Plate = plate,
            State = form.State,
            Color = form.Color,
            Make = ResolveMake(form),
            Model = ResolveModel(form),
        };
        _db.Vehicles.Add(vehicle);
        await _db.SaveChangesAsync(cancellationToken);

        TempData["StatusMessage"] = $"Vehicle {vehicle.Plate} added to your garage.";
        return RedirectToAction(nameof(Index));
    }

    // ------------------------------------------------------------ Edit

    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var student = await CurrentStudentAsync(_db, cancellationToken);
        var vehicle = await FindOwnVehicleAsync(id, student.Id, cancellationToken);
        if (vehicle is null)
        {
            return NotFound();
        }

        return View(ToViewModel(vehicle));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        Guid id, VehicleEditViewModel form, CancellationToken cancellationToken)
    {
        var student = await CurrentStudentAsync(_db, cancellationToken);
        var vehicle = await FindOwnVehicleAsync(id, student.Id, cancellationToken);
        if (vehicle is null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid) { form.Id = id; return View(form); }
        var plate = NormalizePlate(form.Plate);
        if (plate is null)
        {
            form.Id = id;
            form.Error = "Please type the plate — a vehicle cannot be saved without one.";
            return View(form);
        }

        if ((vehicle.Plate != plate || vehicle.State != form.State) &&
            await _db.PermitApplications.AnyAsync(a => a.VehicleId == id, cancellationToken))
        {
            form.Id = id;
            form.Error = "This plate is linked to permit applications. Add a new vehicle to use a different plate or state.";
            return View(form);
        }
        vehicle.Plate = plate;
        vehicle.State = form.State;
        vehicle.Color = form.Color;
        vehicle.Make = ResolveMake(form);
        vehicle.Model = ResolveModel(form);
        await _db.SaveChangesAsync(cancellationToken);

        TempData["StatusMessage"] = $"Vehicle {vehicle.Plate} saved.";
        return RedirectToAction(nameof(Index));
    }

    // ------------------------------------------------------------ helpers

    /// <summary>Owner-scoped lookup: another person's vehicle reads as not found.</summary>
    private Task<Vehicle?> FindOwnVehicleAsync(
        Guid id, string studentId, CancellationToken cancellationToken) =>
        _db.Vehicles.SingleOrDefaultAsync(
            v => v.Id == id && v.StudentId == studentId, cancellationToken);

    private static VehicleEditViewModel ToViewModel(Vehicle vehicle) => new()
    {
        Id = vehicle.Id,
        Plate = vehicle.Plate,
        State = vehicle.State,
        Color = vehicle.Color,
        Make = vehicle.Make,
        Model = vehicle.Model,
    };

    /// <summary>
    /// Plate normalization, one rule everywhere: trim, drop inner
    /// spaces, uppercase — " 8xyz 123 " and "8XYZ123" are the same
    /// plate. Null means there is no plate at all (blank or spaces
    /// only), which the doors refuse.
    /// </summary>
    private static string? NormalizePlate(string? plate)
    {
        if (string.IsNullOrWhiteSpace(plate))
        {
            return null;
        }

        var normalized = plate.Replace(" ", "").Trim().ToUpperInvariant();
        return normalized.Length == 0 ? null : normalized;
    }

    /// <summary>
    /// The stored make: what the dropdown carried — a catalog or
    /// re-offered value as-is, or the typed text behind the
    /// "Other — type it" sentinel (blank typed text means the person
    /// meant plain "Other").
    /// </summary>
    private static string? ResolveMake(VehicleEditViewModel form)
    {
        if (form.Make == VehicleCatalog.OtherMakeValue)
        {
            return NullIfBlank(form.MakeOther) ?? VehicleCatalog.OtherMake;
        }

        return NullIfBlank(form.Make);
    }

    /// <summary>
    /// The stored model, same rule as the make; a blank typed model
    /// records no model at all.
    /// </summary>
    private static string? ResolveModel(VehicleEditViewModel form)
    {
        if (form.Model == VehicleCatalog.OtherModelValue)
        {
            return NullIfBlank(form.ModelOther);
        }

        return NullIfBlank(form.Model);
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
