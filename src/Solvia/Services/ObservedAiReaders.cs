namespace Solvia.Services;

/// <summary>Observes the adapter's validated source without retaining any input or draft field.</summary>
public sealed class ObservedAiExtractor(IAiExtractor inner, AiOperationStatus status) : IAiExtractor
{
    public IAiExtractor Inner { get; } = inner;
    public async Task<ExtractionResult> ExtractAsync(string studentText, CancellationToken cancellationToken = default)
    {
        using var observation = status.Observe(AiReaderKind.Vehicle, out var complete);
        var result = await Inner.ExtractAsync(studentText, cancellationToken);
        complete(result.Source.ToWireName());
        return result;
    }
}

public sealed class ObservedVisitScheduleReader(IVisitScheduleReader inner, AiOperationStatus status) : IVisitScheduleReader
{
    public async Task<VisitScheduleDraft> ReadAsync(string text, CancellationToken cancellationToken = default)
    {
        using var observation = status.Observe(AiReaderKind.Schedule, out var complete);
        var result = await inner.ReadAsync(text, cancellationToken);
        complete(result.Source);
        return result;
    }
}
