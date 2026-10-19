using Microsoft.EntityFrameworkCore;
using Solvia.Data;
using Solvia.Models.Enums;

namespace Solvia.Services;

/// <summary>One Pending price the repair put back to the catalog value.</summary>
public sealed record PriceRepairChange(
    Guid ApplicationId,
    string StudentId,
    string Campus,
    int OldPriceCents,
    int NewPriceCents);

/// <summary>
/// One Pending row the repair deliberately left alone, and why:
/// its campus publishes no price for what was applied for, so there
/// is no honest value to write.
/// </summary>
public sealed record PriceRepairSkip(
    Guid ApplicationId,
    string StudentId,
    string Campus,
    string Reason);

/// <summary>The full outcome of one repair run (REDO-PLAN item 55).</summary>
public sealed record PriceRepairReport(
    IReadOnlyList<PriceRepairChange> Changes,
    IReadOnlyList<PriceRepairSkip> Skipped,
    int UnchangedCount);

/// <summary>
/// The price-repair utility (REDO-PLAN item 55): the fee-truth
/// story's safety net. If a Pending application's snapshotted price
/// ever disagrees with the college's published fee — a bad write, a
/// stale draft, a hand-edited row — this recomputes it from the fee
/// catalog through the SAME <see cref="PricingService"/> rule
/// creation used (campus + type + the row's stored term + the row's
/// SNAPSHOT category) and writes the catalog value back.
///
/// The boundaries are the point:
/// - PENDING applications only. A Paid or NotRequired snapshot is
///   historical truth — the amount the student actually settled —
///   and is NEVER rewritten.
/// - An unpublished fee leaves the row untouched and is reported as
///   skipped: repair never invents a price to "fix" a row with.
/// - Every row it saves gets a fresh concurrency token first
///   (item 51), like every other writer.
/// </summary>
public sealed class PriceRepairService
{
    private readonly SolviaDbContext _db;
    private readonly IFeeCatalog _catalog;
    private readonly PricingService _pricing;

    public PriceRepairService(
        SolviaDbContext db, IFeeCatalog catalog, PricingService pricing)
    {
        _db = db;
        _catalog = catalog;
        _pricing = pricing;
    }

    /// <summary>
    /// Recomputes every Pending application's price from the fee
    /// catalog (optionally only one campus's rows) and returns the
    /// change list: what moved, what was skipped, and how many rows
    /// already matched the catalog.
    /// </summary>
    public async Task<PriceRepairReport> RepairPendingAsync(
        string? campus = null, CancellationToken cancellationToken = default)
    {
        var query = _db.PermitApplications
            .Include(a => a.PermitType)
            .Where(a => a.PaymentStatus == PaymentStatus.Pending);
        if (!string.IsNullOrWhiteSpace(campus))
        {
            var wanted = campus.Trim();
            query = query.Where(a => a.Campus == wanted);
        }

        var pending = await query
            .OrderBy(a => a.SubmittedAtUtc)
            .ToListAsync(cancellationToken);

        var changes = new List<PriceRepairChange>();
        var skipped = new List<PriceRepairSkip>();
        var unchanged = 0;

        foreach (var application in pending)
        {
            if (!string.IsNullOrWhiteSpace(application.PayPalOrderId))
            {
                skipped.Add(new PriceRepairSkip(application.Id, application.StudentId,
                    application.Campus, "A PayPal order already holds this price. Payment must be reconciled before repricing."));
                continue;
            }
            var fee = _catalog.Find(application.Campus);
            if (fee is null)
            {
                skipped.Add(new PriceRepairSkip(
                    application.Id, application.StudentId, application.Campus,
                    "Campus is not in the fee catalog — no published price to repair to."));
                continue;
            }

            if (application.PermitType is null)
            {
                skipped.Add(new PriceRepairSkip(
                    application.Id, application.StudentId, application.Campus,
                    "The application's permit type is missing — left untouched."));
                continue;
            }

            var resolved = _pricing.ResolvePriceCents(
                fee, application.PermitType.Code,
                application.Term, application.Category);
            if (resolved is null)
            {
                skipped.Add(new PriceRepairSkip(
                    application.Id, application.StudentId, application.Campus,
                    $"{application.Campus} does not publish this permit's price — left untouched."));
                continue;
            }

            if (resolved.Value == application.PriceCents)
            {
                unchanged++;
                continue;
            }

            changes.Add(new PriceRepairChange(
                application.Id, application.StudentId, application.Campus,
                application.PriceCents, resolved.Value));
            application.PriceCents = resolved.Value;
            application.ConcurrencyToken = Guid.NewGuid();
        }

        if (changes.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return new PriceRepairReport(changes, skipped, unchanged);
    }
}
