using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;

namespace Toolkit.AI.Caching;

/// <summary>
/// Microsoft.Extensions.AI's caching layer with one change: the cache key is built from
/// normalized message text (see <see cref="CacheKeyNormalizer"/>). Everything else, including
/// what is sent to the model and what is stored, is the base class's behaviour.
/// </summary>
public sealed class NormalizingCachingChatClient : DistributedCachingChatClient
{
    /// <summary>
    /// Added to every key, so keys made with normalization never match keys made without it.
    /// Bump the version if <see cref="CacheKeyNormalizer"/>'s rules change, to retire old entries.
    /// </summary>
    private const string KeyVersion = "toolkit-normalized-keys-v1";

    public NormalizingCachingChatClient(IChatClient innerClient, IDistributedCache storage)
        : base(innerClient, storage)
    {
        CacheKeyAdditionalValues = [KeyVersion];
    }

    protected override string GetCacheKey(
        IEnumerable<ChatMessage> messages, ChatOptions? options, params ReadOnlySpan<object?> additionalValues) =>
        base.GetCacheKey(messages.Select(NormalizeForKey), options, additionalValues);

    private static ChatMessage NormalizeForKey(ChatMessage message) =>
        new(message.Role, message.Contents.Select(NormalizeContent).ToList())
        {
            AuthorName = message.AuthorName,
        };

    // Only text is normalized. Images, tool calls and other content go into the key unchanged.
    private static AIContent NormalizeContent(AIContent content) =>
        content is TextContent text ? new TextContent(CacheKeyNormalizer.Normalize(text.Text)) : content;
}
