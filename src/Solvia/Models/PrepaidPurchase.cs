using Solvia.Models.Enums;

namespace Solvia.Models;

/// <summary>One payment for an immutable set of individually dated day permits.</summary>
public sealed class PrepaidPurchase
{
    public Guid Id { get; set; }
    public string StudentId { get; set; } = "";
    public string Campus { get; set; } = "";
    public UserCategory Category { get; set; }
    public Guid VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    public TermKind Term { get; set; }
    public int UnitPriceCents { get; set; }
    public int TotalCents { get; set; }
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;
    public string? PayPalOrderId { get; set; }
    public string? PayPalCaptureId { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
    public Guid ConcurrencyToken { get; set; }
    /// <summary>Stable owner/vehicle/campus/term/date identity; deliberately excludes prices.</summary>
    public string DraftKey { get; set; } = "";
    public List<PrepaidPurchaseItem> Items { get; set; } = [];
}

/// <summary>A dated permit belongs to at most one shared purchase.</summary>
public sealed class PrepaidPurchaseItem
{
    public Guid PurchaseId { get; set; }
    public PrepaidPurchase Purchase { get; set; } = null!;
    public Guid ApplicationId { get; set; }
    public PermitApplication Application { get; set; } = null!;
}
