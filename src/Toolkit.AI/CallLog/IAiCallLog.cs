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

/// <summary>Where the router records each attempt, and where the budget cap reads spending back.</summary>
public interface IAiCallLog
{
    Task RecordAsync(AiCallRecord record, CancellationToken cancellationToken = default);

    /// <summary>Total estimated cost (USD) of calls made at or after <paramref name="since"/>.</summary>
    Task<decimal> GetCostSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default);
}

/// <summary>Used when ToolkitAI:CallLog:Enabled is false. Records nothing and reports no spending.</summary>
public sealed class NullAiCallLog : IAiCallLog
{
    public static readonly NullAiCallLog Instance = new();

    public Task RecordAsync(AiCallRecord record, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<decimal> GetCostSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default) => Task.FromResult(0m);
}
