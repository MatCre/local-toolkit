using System.Globalization;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Toolkit.Vault.Import;

/// <summary>One message in an exported conversation.</summary>
/// <param name="FromHuman">True for your messages, false for Claude's.</param>
public sealed record ClaudeMessage(bool FromHuman, string Text);

/// <summary>One conversation from a claude.ai data export.</summary>
public sealed record ClaudeConversation(string Id, string? Name, DateTimeOffset CreatedUtc, IReadOnlyList<ClaudeMessage> Messages)
{
    /// <summary>Stable id for the vault, so importing the same export twice adds nothing new.</summary>
    public string ExternalId => $"claude:conversation:{Id}";

    /// <summary>The conversation's name, or your first message when it has none.</summary>
    public string Title =>
        !string.IsNullOrWhiteSpace(Name) ? Name.Trim() : Messages.FirstOrDefault(m => m.FromHuman)?.Text ?? "Untitled conversation";

    /// <summary>The whole conversation as plain text, each message labelled with who wrote it.</summary>
    public string ToTranscript()
    {
        var text = new StringBuilder();
        foreach (var message in Messages)
        {
            text.Append(message.FromHuman ? "You:\n" : "Claude:\n").Append(message.Text.Trim()).Append("\n\n");
        }

        return text.ToString().TrimEnd();
    }
}

/// <summary>
/// Reads conversations from a claude.ai data export (Settings → Privacy → Export data): either the
/// downloaded .zip or the conversations.json inside it.
/// </summary>
/// <remarks>
/// <para>
/// The file is a JSON array of conversations, each with <c>uuid</c>, <c>name</c>, <c>created_at</c>
/// and <c>chat_messages</c>; each message has <c>sender</c> ("human" or "assistant") and its text,
/// either in <c>text</c> or as <c>content</c> blocks of type "text". Anthropic doesn't publish this
/// as a stable format, so the reader is lenient: unknown fields are ignored, a wrapping object with
/// a "conversations" array is accepted, and a conversation it can't make sense of is skipped
/// rather than failing the whole import.
/// </para>
/// <para>
/// Conversations are streamed one at a time with <see cref="JsonSerializer.DeserializeAsyncEnumerable{TValue}(Stream, JsonSerializerOptions?, CancellationToken)"/>,
/// so a large export never has to fit in memory at once.
/// </para>
/// </remarks>
public static class ClaudeExportReader
{
    public static async IAsyncEnumerable<ClaudeConversation> ReadAsync(
        string path, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"No export found at {path}.", path);
        }

        if (string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = ZipFile.OpenRead(path);
            var entry = zip.Entries.FirstOrDefault(e => string.Equals(e.Name, "conversations.json", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException($"{path} has no conversations.json. Is it a claude.ai data export?");

            await using var stream = entry.Open();
            await foreach (var conversation in ReadAsync(stream, cancellationToken))
            {
                yield return conversation;
            }
        }
        else
        {
            await using var stream = File.OpenRead(path);
            await foreach (var conversation in ReadAsync(stream, cancellationToken))
            {
                yield return conversation;
            }
        }
    }

    public static async IAsyncEnumerable<ClaudeConversation> ReadAsync(
        Stream json, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Most exports are a bare array, which can be streamed element by element. Peek at the
        // first bytes to see which shape this one is. A zip entry's stream can't seek back, so
        // the peeked bytes are put back in front of the rest with a small wrapper stream.
        var head = new byte[256];
        var headLength = await json.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, cancellationToken);
        var first = FirstSignificantByte(head.AsSpan(0, headLength));
        var whole = new PrefixedStream(head, headLength, json);

        if (first == '[')
        {
            await foreach (var element in JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(whole, cancellationToken: cancellationToken))
            {
                if (Parse(element) is { } conversation)
                {
                    yield return conversation;
                }
            }

            yield break;
        }

        using var document = await JsonDocument.ParseAsync(whole, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("conversations", out var conversations) || conversations.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("This file has no list of conversations. Is it conversations.json from a claude.ai export?");
        }

        foreach (var element in conversations.EnumerateArray())
        {
            if (Parse(element) is { } conversation)
            {
                yield return conversation;
            }
        }
    }

    /// <summary>Returns null for anything that isn't a usable conversation, so one odd entry can't stop an import.</summary>
    internal static ClaudeConversation? Parse(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object || String(element, "uuid") is not { } id)
        {
            return null;
        }

        var messages = new List<ClaudeMessage>();
        if (element.TryGetProperty("chat_messages", out var chatMessages) && chatMessages.ValueKind == JsonValueKind.Array)
        {
            foreach (var message in chatMessages.EnumerateArray())
            {
                var text = MessageText(message);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    messages.Add(new ClaudeMessage(String(message, "sender") == "human", text));
                }
            }
        }

        if (messages.Count == 0)
        {
            return null;
        }

        var created = DateTimeOffset.TryParse(String(element, "created_at"), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : DateTimeOffset.UnixEpoch;

        return new ClaudeConversation(id, String(element, "name"), created, messages);
    }

    private static string? MessageText(JsonElement message)
    {
        if (String(message, "text") is { Length: > 0 } text)
        {
            return text;
        }

        // Newer exports put the text in content blocks; tool calls and other block types are skipped.
        if (message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            var parts = content.EnumerateArray()
                .Where(block => String(block, "type") == "text")
                .Select(block => String(block, "text"))
                .Where(part => !string.IsNullOrWhiteSpace(part));
            return string.Join("\n\n", parts);
        }

        return null;
    }

    private static string? String(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>The first byte that isn't whitespace or part of a UTF-8 byte order mark, or -1.</summary>
    private static int FirstSignificantByte(ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes)
        {
            if (b is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or 0xEF or 0xBB or 0xBF))
            {
                return b;
            }
        }

        return -1;
    }

    /// <summary>A read-only stream of some already-read bytes followed by the rest of another stream.</summary>
    private sealed class PrefixedStream(byte[] prefix, int prefixLength, Stream rest) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_position < prefixLength)
            {
                var n = Math.Min(buffer.Length, prefixLength - _position);
                prefix.AsSpan(_position, n).CopyTo(buffer);
                _position += n;
                return n;
            }

            return rest.Read(buffer);
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position < prefixLength)
            {
                return Read(buffer.Span);
            }

            return await rest.ReadAsync(buffer, cancellationToken);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
