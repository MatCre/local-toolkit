using System.IO.Compression;
using Microsoft.Data.Sqlite;
using Toolkit.Vault.Import;
using Toolkit.Vault.Sqlite;

namespace Toolkit.Vault.Tests;

public sealed class ClaudeImportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "toolkit-vault-tests", Guid.NewGuid().ToString("N"));

    // The shape of a claude.ai export's conversations.json, cut down to the fields the reader uses
    // plus a few it should ignore. Message text appears both ways real exports use: in "text", and
    // in "content" blocks (with a tool-use block that must be skipped).
    private const string ExportJson = """
        [
          {
            "uuid": "c1",
            "name": "Retry policy for HttpClient",
            "created_at": "2026-10-14T09:30:00.000000Z",
            "updated_at": "2026-10-14T09:45:00.000000Z",
            "account": { "uuid": "ignored" },
            "chat_messages": [
              { "uuid": "m1", "sender": "human", "text": "How do I add retries to HttpClient in .NET?", "created_at": "2026-10-14T09:30:00Z" },
              { "uuid": "m2", "sender": "assistant", "text": "", "content": [
                  { "type": "text", "text": "Use AddStandardResilienceHandler from Microsoft.Extensions.Http.Resilience." },
                  { "type": "tool_use", "name": "web_search", "input": {} },
                  { "type": "text", "text": "It adds retry, circuit breaker and timeouts." } ] }
            ]
          },
          {
            "uuid": "c2",
            "name": "",
            "created_at": "2026-10-20T18:00:00Z",
            "chat_messages": [
              { "sender": "human", "text": "What is a covering index?" },
              { "sender": "assistant", "text": "An index that contains every column a query needs." }
            ]
          },
          { "uuid": "c3", "name": "Empty", "created_at": "2026-10-21T00:00:00Z", "chat_messages": [] },
          { "name": "No id, so not importable", "chat_messages": [ { "sender": "human", "text": "hi" } ] }
        ]
        """;

    private string WriteFile(string name, string content)
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private string WriteZip(string json)
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "data-export.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(zip.CreateEntry("users.json").Open()))
        {
            writer.Write("[]");
        }

        using (var writer = new StreamWriter(zip.CreateEntry("conversations.json").Open()))
        {
            writer.Write(json);
        }

        return path;
    }

    private SqliteVaultStore NewVault() => new(Path.Combine(_dir, "vault.db"));

    private static async Task<List<ClaudeConversation>> ReadAll(string path)
    {
        var all = new List<ClaudeConversation>();
        await foreach (var conversation in ClaudeExportReader.ReadAsync(path))
        {
            all.Add(conversation);
        }

        return all;
    }

    [Fact]
    public async Task Reads_conversations_and_skips_unusable_ones()
    {
        var conversations = await ReadAll(WriteFile("conversations.json", ExportJson));

        Assert.Equal(["c1", "c2"], conversations.Select(c => c.Id)); // c3 has no messages, the last has no id
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 9, 30, 0, TimeSpan.Zero), conversations[0].CreatedUtc);
    }

    [Fact]
    public async Task Reads_text_from_content_blocks_and_ignores_tool_calls()
    {
        var retry = (await ReadAll(WriteFile("conversations.json", ExportJson)))[0];

        Assert.Equal(
            "You:\nHow do I add retries to HttpClient in .NET?\n\n" +
            "Claude:\nUse AddStandardResilienceHandler from Microsoft.Extensions.Http.Resilience.\n\n" +
            "It adds retry, circuit breaker and timeouts.",
            retry.ToTranscript());
    }

    [Fact]
    public async Task Uses_the_first_question_as_title_when_the_conversation_has_no_name()
    {
        var unnamed = (await ReadAll(WriteFile("conversations.json", ExportJson)))[1];

        Assert.Equal("What is a covering index?", unnamed.Title);
    }

    [Fact]
    public async Task Reads_the_downloaded_zip_directly()
    {
        Assert.Equal(2, (await ReadAll(WriteZip(ExportJson))).Count);
    }

    [Fact]
    public async Task Accepts_conversations_wrapped_in_an_object()
    {
        var wrapped = WriteFile("conversations.json", $$"""{ "conversations": {{ExportJson}} }""");

        Assert.Equal(2, (await ReadAll(wrapped)).Count);
    }

    [Fact]
    public async Task Reports_a_zip_that_is_not_a_claude_export()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "other.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            zip.CreateEntry("readme.txt");
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => ReadAll(path));
    }

    [Fact]
    public async Task Imports_each_conversation_as_a_searchable_entry_with_its_original_date()
    {
        var vault = NewVault();

        var summary = await ClaudeExportImporter.ImportAsync(vault, WriteZip(ExportJson));

        Assert.Equal(2, summary.Added);
        var hit = Assert.Single(await vault.SearchAsync("circuit breaker"));
        Assert.Equal("Retry policy for HttpClient", hit.Title);
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 9, 30, 0, TimeSpan.Zero), hit.CreatedUtc);
        Assert.Contains("claude", hit.Tags);
    }

    [Fact]
    public async Task Importing_again_adds_only_new_conversations()
    {
        var vault = NewVault();
        var export = WriteZip(ExportJson);

        await ClaudeExportImporter.ImportAsync(vault, export);
        var again = await ClaudeExportImporter.ImportAsync(vault, export);

        Assert.Equal(0, again.Added);
        Assert.Equal(2, again.AlreadyInVault);
        Assert.Equal(2, await vault.CountAsync());
    }

    [Fact]
    public async Task Skips_a_conversation_containing_a_secret_and_says_why()
    {
        var fakeKey = "sk-" + "or-v1-" + new string('7', 20) + "abcdef0123456789abcdef0123456789abcdef01";
        var json = $$"""
            [ { "uuid": "s1", "name": "Setting up OpenRouter", "created_at": "2026-10-01T00:00:00Z",
                "chat_messages": [ { "sender": "human", "text": "My key is {{fakeKey}}, why does it fail?" },
                                   { "sender": "assistant", "text": "Check the endpoint." } ] } ]
            """;
        var vault = NewVault();

        var summary = await ClaudeExportImporter.ImportAsync(vault, WriteFile("conversations.json", json));

        var skipped = Assert.Single(summary.Skipped);
        Assert.Equal("Setting up OpenRouter", skipped.Title);
        Assert.DoesNotContain(fakeKey, skipped.Reason);
        Assert.Equal(0, await vault.CountAsync());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
