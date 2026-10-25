namespace Solvia.Models.ViewModels;

/// <summary>Public purchase facts used by both the total review and resumed checkout.</summary>
public sealed record PayPalPaymentPanelViewModel(
    Guid ApplicationId,
    int PriceCents,
    string ReviewedSnapshot,
    string? PayPalOrderId = null,
    string? PayPalCaptureId = null,
    bool IsPrepaid = false);
