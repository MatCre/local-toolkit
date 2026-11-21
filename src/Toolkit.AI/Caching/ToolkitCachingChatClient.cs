using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;

namespace Toolkit.AI.Caching;

/// <summary>
/// Microsoft.Extensions.AI's caching layer with two additions:
/// <list type="bullet">
/// <item>Answers served from the cache are marked with <see cref="CacheHitPropertyKey"/>, so callers
/// can tell a hit from a fresh answer. A cached answer otherwise looks exactly like the original,
/// provider name included.</item>
/// <item>Optionally, the cache key is built from normalized text (see <see cref="CacheKeyNormalizer"/>),
/// so trivially reworded prompts share an entry. The model always receives the original text.</item>
/// </list>
/// </summary>
public sealed class ToolkitCachingChatClient : DistributedCachingChatClient
{
    /// <summary>AdditionalProperties key set to <c>true</c> on a response that came from the cache.</summary>
    public const string CacheHitPropertyKey = "toolkit.cacheHit";

    /// <summary>
    /// Added to every normalized key, so keys made with normalization never match keys made without it.
    /// Bump the version if <see cref="CacheKeyNormalizer"/>'s rules change, to retire old entries.
    /// </summary>
    private const string NormalizedKeyVersion = "toolkit-normalized-keys-v1";

    public ToolkitCachingChatClient(IChatClient innerClient, IDistributedCache storage, bool normalizeKeys = true)
        : base(innerClient, storage)
    {
        NormalizeKeys = normalizeKeys;
        if (normalizeKeys)
        {
            CacheKeyAdditionalValues = [NormalizedKeyVersion];
        }
    }

    public bool NormalizeKeys { get; }

    /// <summary>True when the response was served from the cache rather than by a provider.</summary>
    public static bool IsCacheHit(ChatResponse response) =>
        response.AdditionalProperties?.TryGetValue(CacheHitPropertyKey, out var hit) == true && hit is true;

    protected override string GetCacheKey(
        IEnumerable<ChatMessage> messages, ChatOptions? options, params ReadOnlySpan<object?> additionalValues) =>
        base.GetCacheKey(NormalizeKeys ? messages.Select(NormalizeForKey) : messages, options, additionalValues);

    // Reads happen only on the way out of the cache, so the mark is never stored with the answer.
    protected override async Task<ChatResponse?> ReadCacheAsync(string key, CancellationToken cancellationToken)
    {
        var response = await base.ReadCacheAsync(key, cancellationToken);
        if (response is not null)
        {
            (response.AdditionalProperties ??= new())[CacheHitPropertyKey] = true;
        }

        return response;
    }

    protected override async Task<IReadOnlyList<ChatResponseUpdate>?> ReadCacheStreamingAsync(string key, CancellationToken cancellationToken)
    {
        var updates = await base.ReadCacheStreamingAsync(key, cancellationToken);
        if (updates is { Count: > 0 })
        {
            (updates[0].AdditionalProperties ??= new())[CacheHitPropertyKey] = true;
        }

        return updates;
    }

    private static ChatMessage NormalizeForKey(ChatMessage message) =>
        new(message.Role, message.Contents.Select(NormalizeContent).ToList())
        {
            AuthorName = message.AuthorName,
        };

    // Only text is normalized. Images, tool calls and other content go into the key unchanged.
    private static AIContent NormalizeContent(AIContent content) =>
        content is TextContent text ? new TextContent(CacheKeyNormalizer.Normalize(text.Text)) : content;
}
