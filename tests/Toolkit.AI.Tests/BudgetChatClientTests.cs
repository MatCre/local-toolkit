using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Toolkit.AI.Budget;
using Toolkit.AI.Routing;

namespace Toolkit.AI.Tests;

public class BudgetChatClientTests
{
    // A fixed "now" in the middle of a month, in UTC, so month boundaries are predictable.
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Now);
    private readonly InMemoryCallLog _log = new();
    private readonly FakeChatClient _cloud = new("from cloud");
    private readonly FakeChatClient _local = new("from local");

    /// <summary>Budget -> Router -> [cloud, local], the same order the real pipeline uses.</summary>
    private BudgetChatClient Pipeline(decimal capUsd, InMemoryCallLog? log = null)
    {
        log ??= _log;
        var router = new ToolkitRoutingChatClient(
            [
                new ChatProvider("cloud", _cloud, TimeSpan.FromSeconds(5)),
                new ChatProvider("local", _local, TimeSpan.FromSeconds(5), IsLocal: true),
            ],
            log,
            NullLogger<ToolkitRoutingChatClient>.Instance,
            _time);
        return new BudgetChatClient(router, log, capUsd, NullLogger<BudgetChatClient>.Instance, _time);
    }

    [Fact]
    public async Task Calls_over_the_cap_route_to_local()
    {
        _log.Spent(5.00m, at: Now.AddDays(-3));

        var response = await Pipeline(capUsd: 1.00m).GetResponseAsync("hi");

        Assert.Equal("from local", response.Text);
        Assert.Equal(0, _cloud.Calls);
    }

    [Fact]
    public async Task Calls_under_the_cap_use_the_cloud_provider()
    {
        _log.Spent(0.40m, at: Now.AddDays(-3));

        var response = await Pipeline(capUsd: 1.00m).GetResponseAsync("hi");

        Assert.Equal("from cloud", response.Text);
    }

    [Fact]
    public async Task Last_months_spending_does_not_count()
    {
        _log.Spent(5.00m, at: new DateTimeOffset(2026, 10, 31, 23, 59, 0, TimeSpan.Zero));

        var response = await Pipeline(capUsd: 1.00m).GetResponseAsync("hi");

        Assert.Equal("from cloud", response.Text);
    }

    [Fact]
    public async Task Streaming_calls_over_the_cap_route_to_local()
    {
        _log.Spent(5.00m, at: Now.AddDays(-3));

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in Pipeline(capUsd: 1.00m).GetStreamingResponseAsync("hi"))
        {
            updates.Add(update);
        }

        Assert.Equal("from local", updates.ToChatResponse().Text.Trim());
        Assert.Equal(0, _cloud.Calls);
    }

    [Fact]
    public async Task Goes_local_when_spending_cannot_be_read()
    {
        // Fail closed: if the log can't be read, assume the worst rather than risk overspending.
        var response = await Pipeline(capUsd: 1.00m, log: new InMemoryCallLog { FailReads = true }).GetResponseAsync("hi");

        Assert.Equal("from local", response.Text);
    }

    [Fact]
    public async Task The_routing_hint_is_not_sent_to_the_provider()
    {
        _log.Spent(5.00m, at: Now.AddDays(-3));
        var callerOptions = new ChatOptions { Temperature = 0.2f };

        await Pipeline(capUsd: 1.00m).GetResponseAsync("hi", callerOptions);

        Assert.Equal(0.2f, _local.LastOptions?.Temperature); // the caller's settings still arrive
        Assert.False(_local.LastOptions?.AdditionalProperties?.ContainsKey(RoutingHints.LocalOnly) ?? false);
        Assert.Null(callerOptions.AdditionalProperties); // and the caller's own object is untouched
    }

    [Fact]
    public async Task Fails_clearly_when_over_the_cap_with_no_local_provider()
    {
        _log.Spent(5.00m, at: Now.AddDays(-3));
        var cloudOnly = new ToolkitRoutingChatClient(
            [new ChatProvider("cloud", _cloud, TimeSpan.FromSeconds(5))], _log, NullLogger<ToolkitRoutingChatClient>.Instance, _time);
        var budget = new BudgetChatClient(cloudOnly, _log, 1.00m, NullLogger<BudgetChatClient>.Instance, _time);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => budget.GetResponseAsync("hi"));

        Assert.Contains("local", ex.Message);
        Assert.Equal(0, _cloud.Calls);
    }
}
