namespace Solvia.Models.Enums;

/// <summary>
/// Payment state of a permit application (REDO-PLAN item 49 — the
/// value set matches the reference app exactly, including
/// <see cref="PartiallyRefunded"/>). An application is a live permit
/// only when this is Paid — or NotRequired for a free permit. Paid is
/// set ONLY from a PayPal capture result / verified webhook
/// confirmation, never from a checkout return page. Stored as TEXT.
/// </summary>
public enum PaymentStatus
{
    NotRequired,
    Pending,
    Paid,
    Failed,
    Refunded,
    Expired,
    PartiallyRefunded,
}
