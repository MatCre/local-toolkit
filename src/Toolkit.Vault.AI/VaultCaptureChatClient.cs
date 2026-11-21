using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Toolkit.AI.Routing;

namespace Toolkit.Vault.AI;

/// <summary>
/// A pipeline layer that saves each question and answer into the vault, so anything asked through
/// the AI layer can be found again later with <c>vault search</c>, without asking again.
/// </summary>
/// <remarks>
/// <para>
/// It sits inside the cache, so a cache hit (a repeat of something already saved) isn't saved
/// twice. Failed calls and empty answers are never saved.
/// </para>
/// <para>
/// Saving is a side effect, never part of the answer: if the vault refuses the entry (it looks
/// like it contains a secret) or can't be written, that's logged and the caller still gets the
/// answer. Callers can opt a request out with <see cref="VaultCaptureHints.Skip"/>.
/// </para>
/// </remarks>
public sealed class VaultCaptureChatClient(IChatClient innerClient, IVaultStore vault, ILogger<VaultCaptureChatClient> logger)
    : DelegatingChatClient(innerClient)
{
    /// <summary>Tag on every captured entry, so they can be told apart from ones added by hand.</summary>
    public const string CaptureTag = "ai-capture";

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var messageList = messages as IList<ChatMessage> ?? messages.ToList();
        var response = await base.GetResponseAsync(messageList, options, cancellationToken);

        if (!VaultCaptureHints.IsSkipped(options))
        {
            await SaveAsync(messageList, response, cancellationToken);
        }

        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messageList = messages as IList<ChatMessage> ?? messages.ToList();
        var updates = new List<ChatResponseUpdate>();

        await foreach (var update in base.GetStreamingResponseAsync(messageList, options, cancellationToken))
        {
            updates.Add(update);
            yield return update;
        }

        // Only once the stream has finished do we have the whole answer to save.
        if (!VaultCaptureHints.IsSkipped(options))
        {
            await SaveAsync(messageList, updates.ToChatResponse(), cancellationToken);
        }
    }

    private async Task SaveAsync(IList<ChatMessage> messages, ChatResponse response, CancellationToken cancellationToken)
    {
        var question = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text?.Trim();
        var answer = response.Text?.Trim();
        if (string.IsNullOrEmpty(question) || string.IsNullOrEmpty(answer))
        {
            return;
        }

        var provider = response.AdditionalProperties?.TryGetValue(ToolkitRoutingChatClient.ProviderPropertyKey, out var p) == true
            ? p?.ToString()
            : null;

        var entry = new NewVaultEntry(
            Title: question,
            Body: $"Question:\n{question}\n\nAnswer:\n{answer}",
            Source: $"ai: {provider ?? "unknown provider"} / {response.ModelId ?? "unknown model"}",
            Tags: [CaptureTag, provider ?? ""]);

        try
        {
            var id = await vault.AddAsync(entry, cancellationToken);
            logger.LogInformation("Saved to the vault as #{Id}", id);
        }
        catch (SecretDetectedException ex)
        {
            logger.LogWarning(
                "Not saved to the vault: the question or answer looks like it contains a secret ({Kinds})",
                string.Join(", ", ex.Findings.Select(f => f.Kind)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not save the answer to the vault");
        }
    }
}
