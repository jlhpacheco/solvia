using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Solvia.Models;

namespace Solvia.Services;

/// <summary>
/// Binds a payment action to the purchase facts actually rendered for review.
/// This is a version fingerprint, not authorization; ownership is checked separately.
/// Payment status/order/capture tokens are excluded so unchanged retries remain valid.
/// </summary>
public static class CheckoutReviewSnapshot
{
    public static string Compute(PermitApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        var vehicle = application.Vehicle ?? throw new InvalidOperationException("Load the reviewed vehicle before computing its purchase snapshot.");
        var type = application.PermitType ?? throw new InvalidOperationException("Load the reviewed permit type before computing its purchase snapshot.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Version = 1, application.Id, application.StudentId, application.Campus,
            application.Category, application.VehicleId, application.PermitTypeId,
            TypeCode = type.Code, TypeName = type.Name, application.Term,
            application.StartDate, application.EndDate, application.PriceCents,
            vehicle.Plate, vehicle.State, vehicle.Make, vehicle.Model, vehicle.Color
        });
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public static bool Matches(PermitApplication application, string reviewedSnapshot)
    {
        if (reviewedSnapshot.Length != 64) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Compute(application)), Encoding.ASCII.GetBytes(reviewedSnapshot));
    }
}

public sealed class CheckoutReviewChangedException : InvalidOperationException
{
    public const string ReviewMessage =
        "Your permit details or total changed after this page was opened. Review the current total before paying. No new payment order was created.";
    public CheckoutReviewChangedException() : base(ReviewMessage) { }
}
