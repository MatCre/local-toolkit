using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Toolkit.AI.CallLog;

namespace Toolkit.AI.Tests;

/// <summary>A stand-in provider whose behaviour each test scripts. Counts how often it was called.</summary>
internal sealed class FakeChatClient(string reply = "ok", string model = "fake-model") : IChatClient
{
    public int Calls { get; private set; }

    /// <summary>The options the most recent call arrived with, to check what outer layers passed down.</summary>
    public ChatOptions? LastOptions { get; private set; }

    /// <summary>The text of the last message of the most recent call, exactly as it arrived.</summary>
    public string? LastPrompt { get; private set; }

    /// <summary>Thrown instead of answering, if set.</summary>
    public Exception? Throw { get; init; }

    /// <summary>Waits this long before answering (honours cancellation), if set.</summary>
    public TimeSpan? Delay { get; init; }

    public UsageDetails Usage { get; init; } = new() { InputTokenCount = 10, OutputTokenCount = 20 };

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        LastOptions = options;
        LastPrompt = messages.LastOrDefault()?.Text;
        await BehaveAsync(cancellationToken);
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)) { ModelId = model, Usage = Usage };
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        LastOptions = options;
        await BehaveAsync(cancellationToken);
        foreach (var word in reply.Split(' '))
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, word + " ") { ModelId = model };
        }

        yield return new ChatResponseUpdate { Contents = [new UsageContent(Usage)], ModelId = model };
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(ChatClientMetadata) ? new ChatClientMetadata("fake", defaultModelId: model) : null;

    public void Dispose()
    {
    }

    private async Task BehaveAsync(CancellationToken cancellationToken)
    {
        Calls++;
        if (Delay is { } delay)
        {
            await Task.Delay(delay, cancellationToken);
        }

        if (Throw is not null)
        {
            throw Throw;
        }
    }
}

/// <summary>Keeps call records in memory so tests can inspect them.</summary>
internal sealed class InMemoryCallLog : IAiCallLog
{
    public List<AiCallRecord> Records { get; } = [];

    /// <summary>Set to make every read fail, as a locked or corrupt database would.</summary>
    public bool FailReads { get; init; }

    public Task RecordAsync(AiCallRecord record, CancellationToken cancellationToken = default)
    {
        Records.Add(record);
        return Task.CompletedTask;
    }

    public Task<decimal> GetCostSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default) =>
        FailReads
            ? throw new IOException("database is locked")
            : Task.FromResult(Records.Where(r => r.Timestamp >= since).Sum(r => r.EstimatedCostUsd ?? 0));

    /// <summary>Adds a past call that cost <paramref name="usd"/>, to put the month's spending where a test needs it.</summary>
    public void Spent(decimal usd, DateTimeOffset at) =>
        Records.Add(new AiCallRecord(at, "cloud", "paid-model", 1000, 1000, TimeSpan.FromSeconds(1), usd, true, null));
}
