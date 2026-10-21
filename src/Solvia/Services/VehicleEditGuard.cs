using Microsoft.EntityFrameworkCore;
using Solvia.Data;
using Solvia.Models;
using Solvia.Models.Enums;

namespace Solvia.Services;

/// <summary>Preserves the vehicle facts used by payment and issued permit history.</summary>
public static class VehicleEditGuard
{
    // Call only after refreshing the vehicle inside the same write transaction
    // as the eventual save. Checkout binds its order under that SQLite lock too.
    public static async Task ValidateAsync(SolviaDbContext db, Vehicle vehicle,
        string plate, UsState state, string? make, string? model, VehicleColor color,
        CancellationToken cancellationToken = default)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Vehicle edits require a transaction.");
        var identityChanged = vehicle.Plate != plate || vehicle.State != state;
        var changed = identityChanged || vehicle.Make != make || vehicle.Model != model || vehicle.Color != color;
        if (!changed) return;

        var references = db.PermitApplications.Where(application => application.VehicleId == vehicle.Id);
        // Preserve the existing stricter plate/state error for any reference.
        if (identityChanged && await references.AnyAsync(cancellationToken))
            throw new DraftVehicleStateLockedException();
        if (await references.AnyAsync(application =>
                (application.PayPalOrderId != null && application.PayPalOrderId != "") ||
                application.PaymentStatus == PaymentStatus.Paid || application.ReceiptNumber != null,
                cancellationToken))
            throw new VehiclePurchaseLockedException();

        // Only unbound drafts can still update descriptive facts.
    }
}

public sealed class VehiclePurchaseLockedException : InvalidOperationException
{
    public const string PublicMessage =
        "This vehicle is linked to a payment or issued permit. Its saved details cannot be changed. Add a new vehicle for different details; your existing permit and payment remain unchanged.";
    public VehiclePurchaseLockedException() : base(PublicMessage) { }
}
