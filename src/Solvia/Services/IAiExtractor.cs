using Solvia.Models.Enums;

namespace Solvia.Services;

/// <summary>Which path produced an <see cref="ExtractionResult"/>.</summary>
public enum ExtractionSource
{
    /// <summary>The local AI model (Phi-4-mini via Ollama) answered and
    /// its answer passed validation against the fee catalog.</summary>
    Ollama,

    /// <summary>The deterministic rule-based extractor answered —
    /// because Ollama was unreachable, timed out, or its answer failed
    /// validation. The demo never dies because the model is down.</summary>
    Fallback,
}

/// <summary>
/// The validated permit draft extracted from a student's plain-words
/// answers (BUILD-PLAN §2 contract, owned by T3). This is the ONLY thing
/// the AI is allowed to produce:
///
/// - <see cref="College"/> is the catalog's canonical campus name,
///   resolved against <see cref="IFeeCatalog"/> — the extractor can
///   never return a campus the catalog does not know.
/// - There is deliberately NO price field on this type, and none can be
///   added by the model: the AI never sets the price. The price always
///   comes from <see cref="IFeeCatalog"/> later, from the campus's real
///   published fee row.
/// - Missing information stays null; the flow asks only for what is
///   missing (school / car / term — 3 questions max).
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

    /// <summary>Plate as typed, normalized to upper case without spaces.</summary>
    public string? Plate { get; init; }

    /// <summary>Two-letter plate state (e.g. "CA"), or null.</summary>
    public string? CarState { get; init; }

    /// <summary>Honest label of the path that produced this result.</summary>
    public required ExtractionSource Source { get; init; }
}

/// <summary>
/// Turns a student's plain answers into a validated
/// <see cref="ExtractionResult"/>. The primary implementation asks the
/// local AI model (Microsoft Phi-4-mini via Ollama — 100% on the
/// student's own computer, so student data never leaves the machine);
/// ANY model failure falls back to deterministic rule-based extraction,
/// so extraction always answers.
/// </summary>
public interface IAiExtractor
{
    /// <summary>
    /// Extracts the permit draft from one student message. Never throws
    /// for a model/Ollama failure — those land in the fallback.
    /// </summary>
    Task<ExtractionResult> ExtractAsync(
        string studentText,
        CancellationToken cancellationToken = default);
}
