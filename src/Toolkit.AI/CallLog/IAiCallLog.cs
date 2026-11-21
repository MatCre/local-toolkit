namespace Toolkit.AI.CallLog;

/// <summary>One attempt to get an answer from one provider. A fallback produces several records per call.</summary>
public sealed record AiCallRecord(
    DateTimeOffset Timestamp,
    string Provider,
    string? Model,
    long? InputTokens,
    long? OutputTokens,
    TimeSpan Duration,
    decimal? EstimatedCostUsd,
    bool Succeeded,
    string? Error);

/// <summary>Where the router records each attempt. Session 3's budget cap reads this back to sum the month's cost.</summary>
public interface IAiCallLog
{
    Task RecordAsync(AiCallRecord record, CancellationToken cancellationToken = default);
}

/// <summary>Used when ToolkitAI:CallLog:Enabled is false.</summary>
public sealed class NullAiCallLog : IAiCallLog
{
    public static readonly NullAiCallLog Instance = new();

    public Task RecordAsync(AiCallRecord record, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
