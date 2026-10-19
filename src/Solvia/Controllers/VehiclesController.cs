using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Solvia.Data;
using Solvia.Models.Enums;
using Solvia.Models.ViewModels;

namespace Solvia.Controllers;

/// <summary>
/// The student's own cars (BUILD-PLAN T2). Owner-scoped: every query
/// filters by the server-set demo StudentId, so a student only ever
/// sees — and can only fix — her own vehicles.
/// </summary>
public sealed class VehiclesController : DemoControllerBase
{
    private readonly SolviaDbContext _db;

    public VehiclesController(SolviaDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var student = await CurrentStudentAsync(_db, cancellationToken);
        var vehicles = await _db.Vehicles
            .Where(v => v.StudentId == student.Id)
            .OrderBy(v => v.Plate)
            .ToListAsync(cancellationToken);
        return View(vehicles);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var student = await CurrentStudentAsync(_db, cancellationToken);
        var vehicle = await _db.Vehicles.SingleOrDefaultAsync(
            v => v.Id == id && v.StudentId == student.Id, cancellationToken);
        if (vehicle is null)
        {
            return NotFound();
        }

        return View(new VehicleEditViewModel
        {
            Id = vehicle.Id,
            Plate = vehicle.Plate,
            Make = vehicle.Make,
            Model = vehicle.Model,
            Color = vehicle.Color,
            State = vehicle.State,
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        VehicleEditViewModel form, CancellationToken cancellationToken)
    {
        var student = await CurrentStudentAsync(_db, cancellationToken);
        var vehicle = await _db.Vehicles.SingleOrDefaultAsync(
            v => v.Id == form.Id && v.StudentId == student.Id, cancellationToken);
        if (vehicle is null)
        {
            return NotFound();
        }

        vehicle.Plate = (form.Plate ?? "").Trim().ToUpperInvariant().Replace(" ", "");
        vehicle.Make = string.IsNullOrWhiteSpace(form.Make) ? null : form.Make.Trim();
        vehicle.Model = string.IsNullOrWhiteSpace(form.Model) ? null : form.Model.Trim();
        vehicle.Color = form.Color;
        vehicle.State = form.State;
        await _db.SaveChangesAsync(cancellationToken);

        return RedirectToAction(nameof(Index));
    }
}
