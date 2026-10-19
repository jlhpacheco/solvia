namespace Solvia.Models;

/// <summary>
/// One row per inbound webhook delivery (REDO-PLAN item 33; the
/// entity is pre-built by R1, the handler lands with R7). The unique
/// (Provider, EventId) pair is what makes webhook processing
/// idempotent: a redelivered event collides with its own log row
/// instead of acting twice.
/// </summary>
public sealed class WebhookEventLog
{
    public Guid Id { get; set; }

    /// <summary>Who sent the event, e.g. "PayPal".</summary>
    public string Provider { get; set; } = "";

    /// <summary>The provider's own event id (unique per provider).</summary>
    public string EventId { get; set; } = "";

    /// <summary>The provider's event type, e.g. "PAYMENT.CAPTURE.COMPLETED".</summary>
    public string EventType { get; set; } = "";

    /// <summary>When the delivery reached us.</summary>
    public DateTime ReceivedAtUtc { get; set; }

    /// <summary>When handling finished; null while unhandled.</summary>
    public DateTime? ProcessedAtUtc { get; set; }

    /// <summary>
    /// How the delivery ended, in the handler's words ("Acted",
    /// "Duplicate", "Ignored", "Failed"). Null until handled.
    /// </summary>
    public string? Outcome { get; set; }
}
