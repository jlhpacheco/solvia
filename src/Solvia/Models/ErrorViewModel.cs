namespace Solvia.Models;

/// <summary>Model for the shared Error view (shell plumbing, R0).</summary>
public class ErrorViewModel
{
    public string? RequestId { get; set; }

    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
}
