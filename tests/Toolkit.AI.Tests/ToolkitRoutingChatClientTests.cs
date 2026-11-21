using System.Net;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Toolkit.AI.Routing;

namespace Toolkit.AI.Tests;

public class ToolkitRoutingChatClientTests
{
    private readonly InMemoryCallLog _log = new();

    private ToolkitRoutingChatClient Router(params (string Name, IChatClient Client)[] providers) =>
        new(
            providers.Select(p => new ChatProvider(p.Name, p.Client, TimeSpan.FromSeconds(5))).ToList(),
            _log,
            NullLogger<ToolkitRoutingChatClient>.Instance);

    [Fact]
    public async Task Uses_the_first_provider_when_it_answers()
    {
        var cloud = new FakeChatClient("from cloud");
        var local = new FakeChatClient("from local");

        var response = await Router(("cloud", cloud), ("local", local)).GetResponseAsync("hi");

        Assert.Equal("from cloud", response.Text);
        Assert.Equal("cloud", response.AdditionalProperties?[ToolkitRoutingChatClient.ProviderPropertyKey]);
        Assert.Equal(0, local.Calls);
    }

    [Fact]
    public async Task Falls_back_when_a_provider_fails()
    {
        // What a rate limit or Wi-Fi off looks like to the caller: an exception from the client.
        var cloud = new FakeChatClient { Throw = new HttpRequestException("rate limited", null, HttpStatusCode.TooManyRequests) };
        var local = new FakeChatClient("from local");

        var response = await Router(("cloud", cloud), ("local", local)).GetResponseAsync("hi");

        Assert.Equal("from local", response.Text);
        Assert.Equal("local", response.AdditionalProperties?[ToolkitRoutingChatClient.ProviderPropertyKey]);

        // Both attempts are logged: the failure first, then the answer.
        Assert.Collection(_log.Records,
            failed => { Assert.Equal("cloud", failed.Provider); Assert.False(failed.Succeeded); Assert.Contains("rate limited", failed.Error); },
            answered => { Assert.Equal("local", answered.Provider); Assert.True(answered.Succeeded); Assert.Equal(20, answered.OutputTokens); });
    }

    [Fact]
    public async Task Falls_back_when_a_provider_is_too_slow()
    {
        var slow = new FakeChatClient { Delay = TimeSpan.FromMinutes(5) };
        var local = new FakeChatClient("from local");
        var router = new ToolkitRoutingChatClient(
            [new ChatProvider("slow", slow, TimeSpan.FromMilliseconds(50)), new ChatProvider("local", local, TimeSpan.FromSeconds(5))],
            _log,
            NullLogger<ToolkitRoutingChatClient>.Instance);

        var response = await router.GetResponseAsync("hi");

        Assert.Equal("from local", response.Text);
        Assert.Contains("did not answer within", _log.Records[0].Error);
    }

    [Fact]
    public async Task Does_not_fall_back_when_the_caller_cancels()
    {
        var slow = new FakeChatClient { Delay = TimeSpan.FromMinutes(5) };
        var local = new FakeChatClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Router(("slow", slow), ("local", local)).GetResponseAsync("hi", cancellationToken: cts.Token));

        Assert.Equal(0, local.Calls);
    }

    [Fact]
    public async Task Reports_every_failure_when_all_providers_fail()
    {
        var a = new FakeChatClient { Throw = new InvalidOperationException("a broke") };
        var b = new FakeChatClient { Throw = new InvalidOperationException("b broke") };

        var ex = await Assert.ThrowsAsync<AllProvidersFailedException>(() => Router(("a", a), ("b", b)).GetResponseAsync("hi"));

        Assert.Equal(["a broke", "b broke"], ex.InnerExceptions.Select(e => e.Message));
    }

    [Fact]
    public async Task Streaming_falls_back_before_the_first_chunk()
    {
        var cloud = new FakeChatClient { Throw = new HttpRequestException("offline") };
        var local = new FakeChatClient("hello from local");

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in Router(("cloud", cloud), ("local", local)).GetStreamingResponseAsync("hi"))
        {
            updates.Add(update);
        }

        Assert.Equal("hello from local", updates.ToChatResponse().Text.Trim());
        Assert.Equal("local", updates[0].AdditionalProperties?[ToolkitRoutingChatClient.ProviderPropertyKey]);
        Assert.Equal(20, _log.Records.Single(r => r.Succeeded).OutputTokens);
    }

    [Fact]
    public async Task Estimates_cost_from_provider_prices()
    {
        var paid = new FakeChatClient { Usage = new() { InputTokenCount = 1_000_000, OutputTokenCount = 500_000 } };
        var router = new ToolkitRoutingChatClient(
            [new ChatProvider("paid", paid, TimeSpan.FromSeconds(5), InputPricePerMillionTokens: 2m, OutputPricePerMillionTokens: 10m)],
            _log,
            NullLogger<ToolkitRoutingChatClient>.Instance);

        await router.GetResponseAsync("hi");

        Assert.Equal(7m, _log.Records.Single().EstimatedCostUsd); // 1M x $2 + 0.5M x $10
    }
}
