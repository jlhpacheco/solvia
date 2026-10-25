using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Solvia.Data;

namespace Solvia.Services;

/// <summary>
/// Draws the two identifiers a live permit carries (REDO-PLAN
/// item 34): the receipt number — which IS the permit number shown
/// on campus and in the PDF — and the unguessable verify token
/// behind the public /verify?t= page.
///
/// Numbers are SOL-YYYY-NNNNNN and uniqueness-checked against the
/// database and tracked unsaved permits with retry (≤10 draws): an
/// already reserved candidate is discarded before the next draw.
/// The database unique index also protects concurrent writers. This service
/// only DRAWS identifiers; stamping them onto an application is
/// <see cref="IssuanceService"/>'s one job.
/// </summary>
public sealed class PermitNumberService
{
    private const int MaxAttempts = 10;

    private readonly SolviaDbContext _db;
    private readonly Func<int> _draw;

    public PermitNumberService(SolviaDbContext db)
        : this(db, () => RandomNumberGenerator.GetInt32(0, 1_000_000))
    {
    }

    // Deterministic collision tests exercise the same ten-draw algorithm.
    // Production construction always uses the cryptographic generator above.
    internal PermitNumberService(SolviaDbContext db, Func<int> draw)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(draw);
        _db = db;
        _draw = draw;
    }

    /// <summary>Draws a receipt/permit number that no application holds yet.</summary>
    public async Task<string> IssueReceiptNumberAsync(
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            // e.g. "SOL-2026-483920" (15 chars — fits the 32-char column).
            var candidate =
                $"SOL-{DateTime.UtcNow.Year}-{_draw():D6}";
            // A shared capture stages several receipts before one SaveChanges.
            // Querying only persisted rows would miss earlier batch children.
            if (_db.PermitApplications.Local.Any(a => a.ReceiptNumber == candidate))
                continue;
            var taken = await _db.PermitApplications
                .AnyAsync(a => a.ReceiptNumber == candidate, cancellationToken);
            if (!taken)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            $"Could not issue a unique permit number after {MaxAttempts} attempts.");
    }

    /// <summary>
    /// A fresh URL-safe verify token (32 chars from 24 random bytes —
    /// fits the 64-char column, unguessable in practice).
    /// </summary>
    public string NewVerifyToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
