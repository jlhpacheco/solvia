using System.Globalization;
using Solvia.Models;
using Solvia.Models.Enums;

namespace Solvia.Services;

/// <summary>
/// Where one student stands on one campus's roster (BUILD-PLAN T5) —
/// the "who paid / who hasn't" answer in four plain states.
/// </summary>
public enum RosterPermitState
{
    /// <summary>Paid through PayPal — permit number and dates are shown.</summary>
    Paid,

    /// <summary>A $0 (FREE) campus: the permit was issued with no payment due.</summary>
    FreeIssued,

    /// <summary>An application exists at this campus, but the money never landed.</summary>
    Pending,

    /// <summary>No application at this campus at all. Never invented data.</summary>
    NoPermit,
}

/// <summary>
/// The outcome of a validity check: whether the permit is live, and the
/// plain-words reason shown on the public /verify page either way.
/// </summary>
public sealed record PermitValidityResult(bool IsActive, string Reason);

/// <summary>
/// The one place "is this permit live?" is decided (BUILD-PLAN §2
/// contract, owned by T5 — mirrors the guard rules in docs/SCHEMA.md).
/// A permit is active when today falls inside its StartDate–EndDate
/// window, the application is not Denied, and its PaymentStatus is Paid
/// (or NotRequired for a free permit). The college roster and the
/// public /verify page both read their truth from here.
/// </summary>
public static class PermitValidity
{
    /// <summary>True when <paramref name="application"/> is a live permit on <paramref name="today"/>.</summary>
    public static bool IsActive(PermitApplication application, DateOnly today) =>
        Check(application, today).IsActive;

    /// <summary>
    /// The full check: active or not, plus the plain-words reason. The
    /// reason order is deliberate — a denied or refunded permit says so
    /// even when its dates also happen to be off.
    /// </summary>
    public static PermitValidityResult Check(PermitApplication application, DateOnly today)
    {
        if (application.Status == ApplicationStatus.Denied)
        {
            return new PermitValidityResult(
                false, "This permit was denied, so it is not valid.");
        }

        if (application.PaymentStatus == PaymentStatus.Refunded)
        {
            return new PermitValidityResult(
                false, "This permit was refunded, so it is not valid.");
        }

        if (application.PaymentStatus is not (PaymentStatus.Paid or PaymentStatus.NotRequired))
        {
            return new PermitValidityResult(
                false, "This permit has not been paid for, so it is not valid.");
        }

        if (today < application.StartDate)
        {
            return new PermitValidityResult(
                false, $"This permit is not valid yet — it starts on {LongDate(application.StartDate)}.");
        }

        if (today > application.EndDate)
        {
            return new PermitValidityResult(
                false, $"This permit ended on {LongDate(application.EndDate)}.");
        }

        return new PermitValidityResult(true, "This permit is valid.");
    }

    /// <summary>
    /// Classifies one student's standing at one campus from the
    /// applications they hold there. Issued beats started beats
    /// nothing; among issued permits the live one wins, then the most
    /// recently submitted. The returned permit is the one whose number
    /// and dates the roster shows (null unless a permit was issued).
    /// </summary>
    public static (RosterPermitState State, PermitApplication? Permit) Classify(
        IReadOnlyCollection<PermitApplication> applicationsAtCampus,
        DateOnly today)
    {
        var issued = applicationsAtCampus
            .Where(a => a.Status != ApplicationStatus.Denied &&
                        a.PaymentStatus is PaymentStatus.Paid or PaymentStatus.NotRequired)
            .ToList();

        var best = issued
            .OrderByDescending(a => IsActive(a, today))
            .ThenByDescending(a => a.SubmittedAtUtc)
            .FirstOrDefault();

        if (best is not null)
        {
            return (best.PaymentStatus == PaymentStatus.Paid
                ? RosterPermitState.Paid
                : RosterPermitState.FreeIssued, best);
        }

        return applicationsAtCampus.Count > 0
            ? (RosterPermitState.Pending, null)
            : (RosterPermitState.NoPermit, null);
    }

    private static string LongDate(DateOnly date) =>
        date.ToString("MMMM d, yyyy", CultureInfo.GetCultureInfo("en-US"));
}
