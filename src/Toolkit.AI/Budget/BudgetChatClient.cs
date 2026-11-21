using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Toolkit.AI.CallLog;
using Toolkit.AI.Routing;

namespace Toolkit.AI.Budget;

/// <summary>
/// A pipeline layer that checks this month's estimated spend before each request. Once the
/// cap is reached it marks the request local-only, and the router then skips cloud providers.
/// </summary>
/// <remarks>
/// <see cref="DelegatingChatClient"/> is the base class for middleware: it passes every call
/// to the inner client, and you override only what you need. This layer changes the options
/// on the way in and leaves the response alone.
/// </remarks>
public sealed class BudgetChatClient(
    IChatClient innerClient,
    IAiCallLog callLog,
    decimal monthlyCapUsd,
    ILogger<BudgetChatClient> logger,
    TimeProvider? time = null) : DelegatingChatClient(innerClient)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public decimal MonthlyCapUsd { get; } = monthlyCapUsd;

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        options = await ApplyBudgetAsync(options, cancellationToken);
        return await base.GetResponseAsync(messages, options, cancellationToken);
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        options = await ApplyBudgetAsync(options, cancellationToken);
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            yield return update;
        }
    }

    /// <summary>Start of the current calendar month in the machine's own time zone.</summary>
    public DateTimeOffset CurrentMonthStart()
    {
        var now = _time.GetLocalNow();
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(monthStart, _time.LocalTimeZone.GetUtcOffset(monthStart));
    }

    private async Task<ChatOptions?> ApplyBudgetAsync(ChatOptions? options, CancellationToken cancellationToken)
    {
        decimal spent;
        try
        {
            spent = await callLog.GetCostSinceAsync(CurrentMonthStart(), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Can't tell what's been spent, so play safe: local only for this request.
            logger.LogWarning(ex, "Could not read this month's AI spending; using local models only for this request");
            return LocalOnly(options);
        }

        if (spent < MonthlyCapUsd)
        {
            return options;
        }

        logger.LogWarning(
            "Monthly AI budget reached (${Spent:0.00} of ${Cap:0.00}); using local models only",
            spent, MonthlyCapUsd);
        return LocalOnly(options);
    }

    private static ChatOptions LocalOnly(ChatOptions? options)
    {
        // Clone rather than change the caller's object: they may reuse it for other requests.
        var copy = options?.Clone() ?? new ChatOptions();
        (copy.AdditionalProperties ??= new())[RoutingHints.LocalOnly] = true;
        return copy;
    }
}
