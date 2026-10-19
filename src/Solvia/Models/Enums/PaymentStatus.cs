namespace Solvia.Models.Enums;

/// <summary>
/// Payment state of a permit application (docs/SCHEMA.md). An application
/// is a live permit only when this is Paid — or NotRequired for a free
/// permit. Paid is set ONLY from a PayPal capture result / webhook
/// confirmation, never from a checkout return page.
/// </summary>
public enum PaymentStatus
{
    NotRequired,
    Pending,
    Paid,
    Failed,
    Refunded,
    Expired,
}
