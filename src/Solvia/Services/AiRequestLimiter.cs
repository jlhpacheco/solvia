namespace Solvia.Services;

/// <summary>
/// Shared admission control for both public vehicle-reading routes. Counts
/// requests before inference and limits simultaneous work; no request waits
/// in a queue. This process-local demo guard resets on restart and does not
/// replace a provider spending limit or a distributed hosting gateway.
/// </summary>
public sealed class AiRequestLimiter
{
    public const int MaxRequestsPerClient = 20;
    public const int MaxConcurrentPerClient = 2;
    public const int MaxConcurrentRequests = 4;
    public const int MaxRequestsGlobally = 60;
    public const int MaxTrackedClients = 2048;
    public static readonly TimeSpan WindowLength = TimeSpan.FromMinutes(1);
    public const string RejectionMessage =
        "Too many vehicle-reading requests. Please wait before trying again.";

    private sealed class ClientWindow(long started)
    {
        public long Started = started;
        public int Count;
        public int Active;
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, ClientWindow> _clients = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;
    private long _globalStarted;
    private int _globalCount;
    private int _active;

    public AiRequestLimiter(TimeProvider? timeProvider = null)
    {
        _clock = timeProvider ?? TimeProvider.System;
        _globalStarted = _clock.GetTimestamp();
    }

    public bool TryAcquire(string clientKey, out IDisposable? lease, out int retryAfterSeconds)
    {
        lock (_gate)
        {
            lease = null;
            retryAfterSeconds = 1;
            var now = _clock.GetTimestamp();
            if (_clock.GetElapsedTime(_globalStarted, now) >= WindowLength)
            {
                _globalStarted = now;
                _globalCount = 0;
            }

            if (!_clients.TryGetValue(clientKey, out var client))
            {
                // Bound memory even when callers rotate IP addresses. Active
                // leases retain their counters until they are released.
                if (_clients.Count >= MaxTrackedClients)
                {
                    foreach (var expired in _clients.Where(item => item.Value.Active == 0 &&
                                 _clock.GetElapsedTime(item.Value.Started, now) >= WindowLength)
                                 .Select(item => item.Key).ToArray())
                        _clients.Remove(expired);
                    if (_clients.Count >= MaxTrackedClients)
                    {
                        retryAfterSeconds = (int)WindowLength.TotalSeconds;
                        return false;
                    }
                }
                client = new ClientWindow(now);
                _clients.Add(clientKey, client);
            }

            if (_clock.GetElapsedTime(client.Started, now) >= WindowLength)
            {
                client.Started = now;
                client.Count = 0;
            }

            if (client.Count >= MaxRequestsPerClient || _globalCount >= MaxRequestsGlobally)
            {
                var remaining = TimeSpan.Zero;
                if (client.Count >= MaxRequestsPerClient)
                    remaining = WindowLength - _clock.GetElapsedTime(client.Started, now);
                if (_globalCount >= MaxRequestsGlobally)
                {
                    var globalRemaining = WindowLength - _clock.GetElapsedTime(_globalStarted, now);
                    if (globalRemaining > remaining) remaining = globalRemaining;
                }
                retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
                return false;
            }

            if (client.Active >= MaxConcurrentPerClient || _active >= MaxConcurrentRequests)
                return false;

            client.Count++;
            client.Active++;
            _globalCount++;
            _active++;
            lease = new RequestLease(() =>
            {
                lock (_gate)
                {
                    client.Active--;
                    _active--;
                }
            });
            return true;
        }
    }

    private sealed class RequestLease(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
