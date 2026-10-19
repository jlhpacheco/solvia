using System.Collections.Concurrent;

namespace Solvia.Services;

/// <summary>
/// The lightweight public-verify rate limiter (REDO-PLAN item 46):
/// an in-memory fixed window of <see cref="MaxRequests"/> requests
/// per minute per client IP, shared by the /verify page and its
/// /api/verify JSON twin so neither door can be walked around the
/// other. Deliberately small — no store, no persistence: a restart
/// resets the windows, which is fine for a public permit check whose
/// real credential is the unguessable verify token.
///
/// Registered as a singleton via a one-line Program.cs change
/// request to R0 (pre-approved in REDO-PLAN §3 R7).
/// </summary>
public sealed class VerifyRateLimiter
{
    /// <summary>Requests allowed per IP inside one window.</summary>
    public const int MaxRequests = 60;

    /// <summary>Window length: one fixed minute, per IP.</summary>
    public static readonly TimeSpan WindowLength = TimeSpan.FromMinutes(1);

    private sealed class Window
    {
        public DateTime StartUtc;
        public int Count;
        public readonly object Gate = new();
    }

    private readonly ConcurrentDictionary<string, Window> _windows = new();

    /// <summary>Acquires one slot for <paramref name="key"/> at the current time.</summary>
    public bool TryAcquire(string key) => TryAcquire(key, DateTime.UtcNow);

    /// <summary>
    /// Acquires one slot for <paramref name="key"/> at
    /// <paramref name="utcNow"/> (the clock is a parameter so tests
    /// can walk a window boundary without sleeping). Returns false
    /// once the window's allowance is spent; the window restarts
    /// wholesale once it has fully elapsed.
    /// </summary>
    public bool TryAcquire(string key, DateTime utcNow)
    {
        var window = _windows.GetOrAdd(
            key, _ => new Window { StartUtc = utcNow });
        lock (window.Gate)
        {
            if (utcNow - window.StartUtc >= WindowLength)
            {
                window.StartUtc = utcNow;
                window.Count = 0;
            }

            if (window.Count >= MaxRequests)
            {
                return false;
            }

            window.Count++;
            return true;
        }
    }
}
