using Solvia.Models.Enums;

namespace Solvia.Services;

/// <summary>Which path produced an <see cref="ExtractionResult"/>.</summary>
public enum ExtractionSource
{
    /// <summary>
    /// The local AI model (Microsoft Phi-4-mini via Ollama) answered,
    /// and its answer passed validation against the fee catalog.
    /// Serialized on the wire as "ollama".
    /// </summary>
    Ollama,

    /// <summary>
    /// The deterministic rule-based extractor answered — because
    /// the selected provider was unavailable, timed out, or returned an answer that
    /// failed validation. Serialized on the wire as "fallback".
    /// </summary>
    Fallback,

    /// <summary>The hosted Meta model answered and passed the shared validation.</summary>
    Meta,
}

public static class ExtractionSourceExtensions
{
    public static string ToWireName(this ExtractionSource source) => source switch
    {
        ExtractionSource.Ollama => "ollama",
        ExtractionSource.Meta => "meta",
        _ => "fallback"
    };
}

/// <summary>
/// A permit draft read out of a student's own words — the ONLY thing
/// the extraction layer is allowed to produce (REDO-PLAN §3 R3).
///
/// Two construction guarantees:
/// - <see cref="College"/> is always the fee catalog's canonical
///   campus name, resolved against <see cref="IFeeCatalog"/>. Neither
///   the model nor the rules can name a campus the catalog does not
///   know: an unresolvable college voids the model answer outright.
/// - There is NO price member on this type — not hidden, not
///   nullable, absent. The AI can never set a price; pricing reads
///   <see cref="IFeeCatalog"/> later, from the campus's real
///   published fee row.
///
/// Anything the student did not state stays null; the flow asks only
/// for what is missing (school / car / term — three questions max).
/// </summary>
public sealed record ExtractionResult
{
    /// <summary>Canonical campus name from the fee catalog, or null.</summary>
    public string? College { get; init; }

    /// <summary>Daily or Term permit, or null when not stated.</summary>
    public PermitTypeCode? PermitType { get; init; }

    /// <summary>Fall / Spring / Summer, or null when not stated.</summary>
    public TermKind? Term { get; init; }

    public string? CarMake { get; init; }

    public string? CarModel { get; init; }

    /// <summary>Canonical color name (e.g. "Blue") when recognized.</summary>
    public string? CarColor { get; init; }

    /// <summary>Plate as typed, normalized to upper case, separators stripped.</summary>
    public string? Plate { get; init; }

    /// <summary>Two-letter plate state (e.g. "CA"), or null.</summary>
    public string? CarState { get; init; }

    /// <summary>Honest label of the path that produced this result.</summary>
    public required ExtractionSource Source { get; init; }
}

/// <summary>
/// Reads a permit draft out of one student message. The primary
/// implementation is selected explicitly: Ollama (local to the application
/// host by default) or the hosted Meta Model API. The configured provider
/// receives extraction inputs. Model failures use the deterministic fallback.
/// </summary>
public interface IAiExtractor
{
    /// <summary>
    /// Extracts the draft from the student's message. Never throws
    /// for a model/provider failure — those land in the fallback.
    /// </summary>
    Task<ExtractionResult> ExtractAsync(
        string studentText,
        CancellationToken cancellationToken = default);
}
