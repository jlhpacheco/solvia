using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Solvia.Data;

namespace Solvia.Services;

/// <summary>
/// Issues the two identifiers a live permit carries (BUILD-PLAN T4):
/// the receipt number — which IS the permit number the student shows on
/// campus — and the unguessable verify token behind the public
/// /verify?t= page (T5).
///
/// Receipt numbers are uniqueness-checked against the database with
/// retry, mirroring the reference app: a candidate that already exists
/// is discarded and a new one drawn, so two permits can never share a
/// number even before the unique index gets a say.
/// </summary>
public sealed class PermitNumberService
{
    private const int MaxAttempts = 10;

    private readonly SolviaDbContext _db;

    public PermitNumberService(SolviaDbContext db)
    {
        _db = db;
    }

    /// <summary>Draws a receipt/permit number that no application holds yet.</summary>
    public async Task<string> IssueReceiptNumberAsync(
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            // e.g. "SOL-2026-483920" (15 chars — fits the 32-char column).
            var candidate =
                $"SOL-{DateTime.UtcNow.Year}-{RandomNumberGenerator.GetInt32(0, 1_000_000):D6}";
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
