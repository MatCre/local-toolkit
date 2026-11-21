using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Toolkit.AI.CallLog;

namespace Toolkit.AI.Routing;

/// <summary>
/// Sends each request to the first provider in the list and, if it fails, times out or is
/// rate limited, tries the next one. Every attempt, failed or not, goes to the call log.
/// </summary>
/// <remarks>
/// Callers see a single <see cref="IChatClient"/>. To find out who answered, read
/// <see cref="ProviderPropertyKey"/> from the response's AdditionalProperties.
/// </remarks>
public sealed class ToolkitRoutingChatClient : IChatClient
{
    /// <summary>AdditionalProperties key holding the name of the provider that answered.</summary>
    public const string ProviderPropertyKey = "toolkit.provider";

    private static readonly ChatClientMetadata Metadata = new("toolkit-router");

    private readonly IReadOnlyList<ChatProvider> _providers;
    private readonly IAiCallLog _callLog;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;

    public ToolkitRoutingChatClient(IReadOnlyList<ChatProvider> providers, IAiCallLog callLog, ILogger<ToolkitRoutingChatClient> logger, TimeProvider? time = null)
    {
        if (providers.Count == 0)
        {
            throw new ArgumentException("At least one provider is required.", nameof(providers));
        }

        _providers = providers;
        _callLog = callLog;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public IReadOnlyList<ChatProvider> Providers => _providers;

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        // A retry sends the same messages again, so read them into a list once.
        var messageList = messages as IList<ChatMessage> ?? messages.ToList();
        var failures = new List<Exception>();
        (IReadOnlyList<ChatProvider> candidates, options) = SelectProviders(options);

        foreach (var provider in candidates)
        {
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attempt.CancelAfter(provider.Timeout);
            var started = _time.GetTimestamp();

            try
            {
                var response = await provider.Client.GetResponseAsync(messageList, options, attempt.Token);

                var elapsed = _time.GetElapsedTime(started);
                (response.AdditionalProperties ??= new())[ProviderPropertyKey] = provider.Name;
                LogAnswered(provider, response.ModelId, elapsed);
                await RecordAsync(provider, response.ModelId, response.Usage, elapsed, error: null);
                return response;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                // The caller didn't cancel, so this is the provider's problem: an error,
                // a rate limit, or our own timeout firing. Note it and move on.
                var failure = Describe(provider, ex, attempt.IsCancellationRequested);
                await RecordFailureAsync(provider, failure, _time.GetElapsedTime(started));
                failures.Add(failure);
            }
        }

        throw new AllProvidersFailedException(failures);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messageList = messages as IList<ChatMessage> ?? messages.ToList();
        var failures = new List<Exception>();
        (IReadOnlyList<ChatProvider> candidates, options) = SelectProviders(options);

        foreach (var provider in candidates)
        {
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attempt.CancelAfter(provider.Timeout);
            var started = _time.GetTimestamp();

            // Fallback is only possible until the first chunk arrives: after that the caller
            // has already shown part of an answer, so switching provider would mix two replies.
            IAsyncEnumerator<ChatResponseUpdate>? stream = null;
            bool hasFirst;
            try
            {
                stream = provider.Client
                    .GetStreamingResponseAsync(messageList, options, attempt.Token)
                    .GetAsyncEnumerator(attempt.Token);
                hasFirst = await stream.MoveNextAsync();
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                if (stream is not null)
                {
                    await stream.DisposeAsync();
                }

                var failure = Describe(provider, ex, attempt.IsCancellationRequested);
                await RecordFailureAsync(provider, failure, _time.GetElapsedTime(started));
                failures.Add(failure);
                continue;
            }

            // Committed to this provider. The timeout covered the wait for the first token;
            // a long answer may take longer than that to finish, so switch it off.
            attempt.CancelAfter(Timeout.InfiniteTimeSpan);

            string? modelId = null;
            UsageDetails? usage = null;
            try
            {
                if (hasFirst)
                {
                    var first = stream.Current;
                    (first.AdditionalProperties ??= new())[ProviderPropertyKey] = provider.Name;
                    Track(first, ref modelId, ref usage);
                    yield return first;

                    while (await stream.MoveNextAsync())
                    {
                        Track(stream.Current, ref modelId, ref usage);
                        yield return stream.Current;
                    }
                }
            }
            finally
            {
                await stream.DisposeAsync();
                var elapsed = _time.GetElapsedTime(started);
                LogAnswered(provider, modelId, elapsed);
                await RecordAsync(provider, modelId, usage, elapsed, error: null);
            }

            yield break;
        }

        throw new AllProvidersFailedException(failures);
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceKey is null && serviceType.IsInstanceOfType(this))
        {
            return this;
        }

        if (serviceKey is null && serviceType == typeof(ChatClientMetadata))
        {
            return Metadata;
        }

        // Let callers (and tests) reach a provider's own client, e.g. GetService<OllamaApiClient>().
        foreach (var provider in _providers)
        {
            if (provider.Client.GetService(serviceType, serviceKey) is { } service)
            {
                return service;
            }
        }

        return null;
    }

    public void Dispose()
    {
        foreach (var provider in _providers)
        {
            provider.Client.Dispose();
        }
    }

    /// <summary>
    /// Applies routing hints from outer layers. With <see cref="RoutingHints.LocalOnly"/> set,
    /// only local providers are tried. All toolkit.* hints are removed from the options passed on,
    /// because some clients send unknown AdditionalProperties to the server as extra request fields.
    /// </summary>
    private (IReadOnlyList<ChatProvider> Providers, ChatOptions? Options) SelectProviders(ChatOptions? options)
    {
        if (options?.AdditionalProperties is not { } hints
            || !hints.Keys.Any(k => k.StartsWith(RoutingHints.Prefix, StringComparison.Ordinal)))
        {
            return (_providers, options);
        }

        var localOnly = hints.TryGetValue(RoutingHints.LocalOnly, out var value) && value is true;

        var forwarded = options.Clone();
        foreach (var key in hints.Keys.Where(k => k.StartsWith(RoutingHints.Prefix, StringComparison.Ordinal)).ToList())
        {
            forwarded.AdditionalProperties!.Remove(key);
        }

        if (!localOnly)
        {
            return (_providers, forwarded);
        }

        var local = _providers.Where(p => p.IsLocal).ToList();
        if (local.Count == 0)
        {
            throw new InvalidOperationException(
                "This request may only use local providers (is the monthly budget spent?), but none is configured in ToolkitAI:ProviderOrder.");
        }

        return (local, forwarded);
    }

    private static void Track(ChatResponseUpdate update, ref string? modelId, ref UsageDetails? usage)
    {
        modelId ??= update.ModelId;
        foreach (var content in update.Contents.OfType<UsageContent>())
        {
            usage = content.Details;
        }
    }

    private Exception Describe(ChatProvider provider, Exception ex, bool attemptCancelled)
    {
        // Our own timeout surfaces as an OperationCanceledException; name it for what it is.
        Exception failure = attemptCancelled && ex is OperationCanceledException
            ? new TimeoutException($"{provider.Name} did not answer within {provider.Timeout.TotalSeconds:0.#} s.", ex)
            : ex;

        _logger.LogWarning(
            "AI provider {Provider} failed ({Error}); trying the next provider",
            provider.Name, failure.Message);

        return failure;
    }

    private void LogAnswered(ChatProvider provider, string? modelId, TimeSpan elapsed) =>
        _logger.LogInformation(
            "Answered by {Provider} ({Model}) in {ElapsedMs:0} ms",
            provider.Name, modelId ?? "unknown model", elapsed.TotalMilliseconds);

    private Task RecordFailureAsync(ChatProvider provider, Exception failure, TimeSpan elapsed) =>
        RecordAsync(provider, modelId: null, usage: null, elapsed, failure.Message);

    private async Task RecordAsync(ChatProvider provider, string? modelId, UsageDetails? usage, TimeSpan elapsed, string? error)
    {
        var record = new AiCallRecord(
            Timestamp: _time.GetUtcNow(),
            Provider: provider.Name,
            Model: modelId ?? provider.Client.GetService<ChatClientMetadata>()?.DefaultModelId,
            InputTokens: usage?.InputTokenCount,
            OutputTokens: usage?.OutputTokenCount,
            Duration: elapsed,
            EstimatedCostUsd: provider.EstimateCost(usage),
            Succeeded: error is null,
            Error: error);

        try
        {
            await _callLog.RecordAsync(record);
        }
        catch (Exception ex)
        {
            // The call log is a record of the call, not part of it. A full disk or locked
            // database file must never cost the caller their answer.
            _logger.LogWarning(ex, "Could not write to the AI call log");
        }
    }
}

/// <summary>Thrown when every provider in the list failed. <see cref="AggregateException.InnerExceptions"/> has one entry per provider.</summary>
public sealed class AllProvidersFailedException(IReadOnlyList<Exception> failures)
    : AggregateException($"All {failures.Count} AI providers failed. See InnerExceptions for each one.", failures);
