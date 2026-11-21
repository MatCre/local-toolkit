using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Toolkit.AI.Routing;
using Toolkit.Vault.AI;
using Toolkit.Vault.Sqlite;

namespace Toolkit.Vault.Tests;

public sealed class VaultCaptureTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "toolkit-vault-tests", Guid.NewGuid().ToString("N"));
    private readonly SqliteVaultStore _vault;

    public VaultCaptureTests() => _vault = new SqliteVaultStore(Path.Combine(_dir, "vault.db"));

    private VaultCaptureChatClient Capture(IChatClient provider, IVaultStore? vault = null) =>
        new(provider, vault ?? _vault, NullLogger<VaultCaptureChatClient>.Instance);

    [Fact]
    public async Task Saves_the_question_and_answer()
    {
        var chat = Capture(new AnsweringClient("Use AddStandardResilienceHandler.", provider: "ollama", model: "qwen2.5-coder:7b"));

        var response = await chat.GetResponseAsync("How do I add retries to HttpClient?");

        Assert.Equal("Use AddStandardResilienceHandler.", response.Text);
        var hit = Assert.Single(await _vault.SearchAsync("retries httpclient"));
        Assert.Equal("How do I add retries to HttpClient?", hit.Title);
        Assert.Equal("ai: ollama / qwen2.5-coder:7b", hit.Source);
        Assert.Equal([VaultCaptureChatClient.CaptureTag, "ollama"], hit.Tags);
        Assert.Contains("Answer:\nUse AddStandardResilienceHandler.", (await _vault.GetAsync(hit.Id))!.Body);
    }

    [Fact]
    public async Task Saves_a_streamed_answer_once_it_is_complete()
    {
        var chat = Capture(new AnsweringClient("Covering indexes contain every column a query needs."));

        await foreach (var _ in chat.GetStreamingResponseAsync("What is a covering index?"))
        {
            Assert.Equal(0, await _vault.CountAsync()); // nothing saved mid-stream
        }

        Assert.Single(await _vault.SearchAsync("covering index"));
    }

    [Fact]
    public async Task Skips_requests_marked_private()
    {
        var chat = Capture(new AnsweringClient("Groceries."));

        await chat.GetResponseAsync("Categorise: COUNTDOWN AUCKLAND -45.20", new ChatOptions().WithoutVaultCapture());

        Assert.Equal(0, await _vault.CountAsync());
    }

    [Fact]
    public async Task Still_answers_when_the_question_contains_a_secret()
    {
        var fakeKey = "sk-" + "or-v1-" + new string('7', 20) + "abcdef0123456789abcdef0123456789abcdef01";
        var chat = Capture(new AnsweringClient("Check the endpoint URL."));

        var response = await chat.GetResponseAsync($"Why does {fakeKey} get a 401?");

        Assert.Equal("Check the endpoint URL.", response.Text);
        Assert.Equal(0, await _vault.CountAsync());
    }

    [Fact]
    public async Task Still_answers_when_the_vault_cannot_be_written()
    {
        var chat = Capture(new AnsweringClient("Fine."), vault: new BrokenVault());

        var response = await chat.GetResponseAsync("Anything");

        Assert.Equal("Fine.", response.Text);
    }

    [Fact]
    public async Task Does_not_save_an_empty_answer()
    {
        await Capture(new AnsweringClient("")).GetResponseAsync("Hello?");

        Assert.Equal(0, await _vault.CountAsync());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    /// <summary>Stands in for the rest of the pipeline: answers every request with fixed text.</summary>
    private sealed class AnsweringClient(string answer, string provider = "fake", string model = "fake-model") : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)) { ModelId = model };
            (response.AdditionalProperties ??= new())[ToolkitRoutingChatClient.ProviderPropertyKey] = provider;
            return Task.FromResult(response);
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var word in answer.Split(' '))
            {
                await Task.Yield();
                yield return new ChatResponseUpdate(ChatRole.Assistant, word + " ") { ModelId = model };
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class BrokenVault : IVaultStore
    {
        public string Location => "nowhere";

        public Task<long> AddAsync(NewVaultEntry entry, CancellationToken cancellationToken = default) =>
            throw new IOException("disk full");

        public Task<VaultEntry?> GetAsync(long id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<long?> FindByExternalIdAsync(string externalId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<VaultEntry>> ListRecentAsync(int limit = 20, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<VaultSearchResult>> SearchAsync(string text, int limit = 10, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> RemoveAsync(long id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<long> CountAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
