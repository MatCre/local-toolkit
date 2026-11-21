using TextCopy;

namespace Toolkit.Vault.Cli;

/// <summary>What each command does. Each returns the process exit code: 0 for success, 1 for a problem.</summary>
internal static class VaultCommands
{
    public static async Task<int> AddAsync(
        IVaultStore store, string? title, string? tags, string? source, FileInfo? file, CancellationToken ct)
    {
        string? body;
        string readFrom;

        if (file is not null)
        {
            if (!file.Exists)
            {
                return ConsoleOutput.Error($"File not found: {file.FullName}");
            }

            body = await File.ReadAllTextAsync(file.FullName, ct);
            readFrom = file.FullName;
        }
        else if (Console.IsInputRedirected)
        {
            // e.g.  Get-Content notes.md | vault add   or   echo "..." | vault add
            body = await Console.In.ReadToEndAsync(ct);
            readFrom = "stdin";
        }
        else
        {
            body = await ClipboardService.GetTextAsync(ct);
            readFrom = "clipboard";
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return ConsoleOutput.Error(readFrom == "clipboard"
                ? "The clipboard has no text. Copy something first, or use --file or piped input."
                : $"No text to add from {readFrom}.");
        }

        var id = await store.AddAsync(new NewVaultEntry(title, body, source ?? readFrom, tags is null ? null : [tags]), ct);
        var entry = (await store.GetAsync(id, ct))!;

        ConsoleOutput.Line($"Added {ConsoleOutput.Id(id)}  {ConsoleOutput.Bold(entry.Title)}");
        ConsoleOutput.Line(ConsoleOutput.Dim($"       {entry.Body.Length:N0} characters from {readFrom}{FormatTags(entry.Tags, prefix: ", tags: ")}"));
        return 0;
    }

    public static async Task<int> SearchAsync(IVaultStore store, string text, int limit, CancellationToken ct)
    {
        var results = await store.SearchAsync(text, limit, ct);
        if (results.Count == 0)
        {
            ConsoleOutput.Line($"Nothing matches \"{text}\". Every word has to appear somewhere in an entry; try fewer words.");
            return 0;
        }

        foreach (var hit in results)
        {
            ConsoleOutput.Line($"{ConsoleOutput.Id(hit.Id)}  {ConsoleOutput.Bold(hit.Title)}{ConsoleOutput.Dim(FormatTags(hit.Tags, prefix: "  "))}");
            ConsoleOutput.Line($"       {ConsoleOutput.Snippet(hit.Snippet)}");
        }

        ConsoleOutput.Line(ConsoleOutput.Dim($"\n{results.Count} found. Full text: vault show <id>"));
        return 0;
    }

    public static async Task<int> ShowAsync(IVaultStore store, long id, CancellationToken ct)
    {
        var entry = await store.GetAsync(id, ct);
        if (entry is null)
        {
            return ConsoleOutput.Error($"No entry #{id}.");
        }

        ConsoleOutput.Line($"{ConsoleOutput.Id(entry.Id)}  {ConsoleOutput.Bold(entry.Title)}");
        ConsoleOutput.Line(ConsoleOutput.Dim(
            $"       added {entry.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm}" +
            (entry.Source is null ? "" : $" · from {entry.Source}") +
            FormatTags(entry.Tags, prefix: " · tags: ")));
        ConsoleOutput.Line("");
        ConsoleOutput.Line(entry.Body);
        return 0;
    }

    public static async Task<int> ListAsync(IVaultStore store, int limit, CancellationToken ct)
    {
        var entries = await store.ListRecentAsync(limit, ct);
        if (entries.Count == 0)
        {
            ConsoleOutput.Line($"The vault is empty ({store.Location}). Copy some text and run: vault add --title \"...\"");
            return 0;
        }

        foreach (var entry in entries)
        {
            ConsoleOutput.Line(
                $"{ConsoleOutput.Id(entry.Id)}  {ConsoleOutput.Dim(entry.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd"))}  " +
                $"{entry.Title}{ConsoleOutput.Dim(FormatTags(entry.Tags, prefix: "  "))}");
        }

        ConsoleOutput.Line(ConsoleOutput.Dim($"\n{await store.CountAsync(ct)} entries in {store.Location}"));
        return 0;
    }

    public static async Task<int> RemoveAsync(IVaultStore store, long id, CancellationToken ct)
    {
        var entry = await store.GetAsync(id, ct);
        if (entry is null || !await store.RemoveAsync(id, ct))
        {
            return ConsoleOutput.Error($"No entry #{id}.");
        }

        ConsoleOutput.Line($"Removed {ConsoleOutput.Id(id)}  {entry.Title}");
        return 0;
    }

    private static string FormatTags(IReadOnlyList<string> tags, string prefix) =>
        tags.Count == 0 ? "" : prefix + string.Join(' ', tags.Select(t => "#" + t));
}
