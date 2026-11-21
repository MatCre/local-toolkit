using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Toolkit.AI.CallLog;

namespace Toolkit.AI.Tests;

/// <summary>A stand-in provider whose behaviour each test scripts. Counts how often it was called.</summary>
internal sealed class FakeChatClient(string reply = "ok", string model = "fake-model") : IChatClient
{
    public int Calls { get; private set; }

    /// <summary>Thrown instead of answering, if set.</summary>
    public Exception? Throw { get; init; }

    /// <summary>Waits this long before answering (honours cancellation), if set.</summary>
    public TimeSpan? Delay { get; init; }

    public UsageDetails Usage { get; init; } = new() { InputTokenCount = 10, OutputTokenCount = 20 };

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        await BehaveAsync(cancellationToken);
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)) { ModelId = model, Usage = Usage };
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
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

    public Task RecordAsync(AiCallRecord record, CancellationToken cancellationToken = default)
    {
        Records.Add(record);
        return Task.CompletedTask;
    }
}
