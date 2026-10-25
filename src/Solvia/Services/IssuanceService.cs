using Solvia.Models;

namespace Solvia.Services;

/// <summary>
/// The ONE mint path (REDO-PLAN §2d, item 34). A permit is issued
/// exactly when its application becomes live, and only through
/// <see cref="IssueIfAbsentAsync"/> — used for free/exempt creation,
/// verified individual or shared-purchase captures, and verified PayPal
/// webhooks. Shared purchases issue each dated child through this same
/// implementation. The return, receipt and PDF pages do not mint permits.
/// </summary>
public interface IIssuanceService
{
    /// <summary>
    /// Mints whatever the application is missing — receipt/permit
    /// number, verify token, and the <see cref="PermitApplication.ReceiptIssuedAtUtc"/>
    /// stamp — and rotates its concurrency token for the save that
    /// follows. Anything already present is left exactly as it is
    /// (mint-IF-ABSENT), so a second call — capture and webhook
    /// racing, a door re-submitting — is a no-op, never a re-issue.
    ///
    /// CONTRACT: this method does NOT save. The caller saves in the
    /// same unit of work, so the mint and the state change that
    /// triggered it commit together or not at all. Returns true
    /// when something was minted.
    /// </summary>
    Task<bool> IssueIfAbsentAsync(
        PermitApplication application, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IIssuanceService"/>
public sealed class IssuanceService : IIssuanceService
{
    private readonly PermitNumberService _numbers;

    public IssuanceService(PermitNumberService numbers)
    {
        _numbers = numbers;
    }

    public async Task<bool> IssueIfAbsentAsync(
        PermitApplication application, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(application);

        var minted = false;
        if (application.ReceiptNumber is null)
        {
            application.ReceiptNumber =
                await _numbers.IssueReceiptNumberAsync(cancellationToken);
            minted = true;
        }

        if (application.VerifyToken is null)
        {
            application.VerifyToken = _numbers.NewVerifyToken();
            minted = true;
        }

        if (minted)
        {
            application.ReceiptIssuedAtUtc ??= DateTime.UtcNow;
            // The caller's save carries a fresh token (item 51), so a
            // stale writer loses instead of overwriting this mint.
            application.ConcurrencyToken = Guid.NewGuid();
        }

        return minted;
    }
}
