using Microsoft.Extensions.Options;
using Solvia.Config;

namespace Solvia.Services;

public enum AiReaderKind { Vehicle, Schedule }

/// <summary>Public allowlist: no inputs, credentials, URLs, exceptions or user identifiers.</summary>
public sealed record AiReaderObservation(string Reader, string State, DateTimeOffset? LastAttemptAtUtc,
    DateTimeOffset? LastSuccessAtUtc);
public sealed record AiOperationSnapshot(string State, string Label, string Description,
    string Provider, string Model, DateTimeOffset ObservedAtUtc, DateTimeOffset? LastSuccessAtUtc,
    DateTimeOffset? LastAttemptAtUtc, IReadOnlyList<AiReaderObservation> Readers);

/// <summary>Two bounded observations for this process only. Reading status never invokes a model.</summary>
public sealed class AiOperationStatus
{
    public static readonly TimeSpan RecentSuccessWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ActiveWindow = TimeSpan.FromMinutes(2);
    private readonly object _gate = new();
    private readonly TimeProvider _clock;
    private readonly bool _hosted;
    private readonly bool _configured;
    private readonly string _model;
    private readonly Lane[] _lanes = [new(), new()];
    private long _completionSequence;
    private sealed class Lane
    {
        public int Active;
        public DateTimeOffset? Started;
        public DateTimeOffset? Completed;
        public DateTimeOffset? Success;
        public string Outcome = "unchecked";
        public long Sequence;
    }

    public AiOperationStatus(IOptions<AiOptions> selection, IOptions<TogetherOptions> together,
        IOptions<OllamaOptions> ollama, TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        _hosted = selection.Value.Provider.Equals("together", StringComparison.OrdinalIgnoreCase);
        _configured = !_hosted || !string.IsNullOrWhiteSpace(together.Value.ApiKey);
        // Public names only; never expose an arbitrary configuration string.
        _model = _hosted ? TogetherOptions.SupportedModel :
            ollama.Value.Model.Equals("phi4-mini", StringComparison.OrdinalIgnoreCase)
                ? "phi4-mini (Ollama)" : "Configured local Ollama model";
    }

    public IDisposable Observe(AiReaderKind kind, out Action<string> complete)
    {
        var lane = _lanes[(int)kind];
        lock (_gate) { lane.Active++; lane.Started = _clock.GetUtcNow(); }
        var finished = 0;
        void Finish(string outcome)
        {
            if (Interlocked.Exchange(ref finished, 1) != 0) return;
            lock (_gate)
            {
                lane.Active = Math.Max(0, lane.Active - 1);
                lane.Completed = _clock.GetUtcNow();
                var accepted = _configured && (kind != AiReaderKind.Schedule || _hosted) &&
                    outcome == (_hosted ? "together" : "ollama");
                lane.Outcome = accepted ? "success" : outcome == "interrupted" ? "interrupted" : "fallback";
                if (accepted) lane.Success = lane.Completed;
                lane.Sequence = ++_completionSequence;
            }
        }
        complete = Finish;
        return new Observation(() => Finish("interrupted"));
    }

    private sealed class Observation(Action finish) : IDisposable
    {
        public void Dispose() => finish();
    }

    public AiOperationSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            var latest = _lanes.OrderByDescending(lane => lane.Sequence).First();
            var lastSuccess = _lanes.Max(lane => lane.Success);
            var lastAttempt = _lanes.Max(lane => lane.Completed);
            var active = _lanes.Any(lane => lane.Active > 0 && lane.Started is { } started && now >= started && now - started < ActiveWindow);
            string state, label, description;
            if (!_configured)
                (state, label, description) = ("unconfigured", "AI not configured", "Built-in rules are available. No hosted AI response has been verified on this app instance.");
            else if (active)
                (state, label, description) = ("reading", "AI reading…", "A draft read is in progress. Wait for its result; a successful model response has not yet been claimed.");
            else if (latest.Outcome == "fallback")
                (state, label, description) = ("fallback", "AI: latest read used rules", "The latest draft read used built-in rules, not a validated model response. You can review and correct the draft.");
            else if (latest.Outcome == "interrupted")
                (state, label, description) = ("interrupted", "AI read interrupted", "The latest read did not complete. No new successful model response is claimed.");
            else if (lastSuccess is null)
                (state, label, description) = ("unchecked", "AI configured · not checked", "Configuration alone does not prove availability. No validated model response has been observed on this app instance.");
            else if (now < lastSuccess.Value || now - lastSuccess.Value >= RecentSuccessWindow)
                (state, label, description) = ("stale", "AI: earlier response", "The last validated model response is more than 10 minutes old. Current availability has not been checked.");
            else
                (state, label, description) = ("recent", "AI: response verified", "A real draft read returned a validated model response within the last 10 minutes. This is a past observation, not a promise of availability.");
            var readers = _lanes.Select((lane, index) => new AiReaderObservation(index == 0 ? "Vehicle" : "Visits",
                index == 1 && !_hosted ? "disabled" : !_configured ? "unconfigured" :
                lane.Active > 0 && lane.Started is { } started && now >= started && now - started < ActiveWindow ? "reading" : lane.Outcome,
                lane.Completed, lane.Success)).ToArray();
            return new(state, label, description, _hosted ? "Together AI" : "Ollama", _model, now,
                lastSuccess, lastAttempt, Array.AsReadOnly(readers));
        }
    }
}
