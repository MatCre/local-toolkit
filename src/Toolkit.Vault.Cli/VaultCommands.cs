using TextCopy;

namespace Toolkit.Vault.Cli;

/// <summary>What each command does. Each returns the process exit code: 0 for success, 1 for a problem.</summary>
internal static class VaultCommands
{
    private const int PreviewLength = 70;

    public static async Task<int> AddAsync(
        IVaultStore store, string? title, string? tags, string? source, FileInfo? file, bool fromClipboard, bool allowSecret,
        CancellationToken ct)
    {
        // Where the text comes from is always something you asked for: a file, piped input, or
        // --clipboard. The clipboard is never read by default, because it often holds a password
        // or key copied for something else.
        if (file is not null && fromClipboard)
        {
            return ConsoleOutput.Error("Use either --file or --clipboard, not both.");
        }

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
        else if (fromClipboard)
        {
            body = await ClipboardService.GetTextAsync(ct);
            readFrom = "clipboard";
        }
        else if (Console.IsInputRedirected)
        {
            // e.g.  Get-Content notes.md | vault add   or   echo "..." | vault add
            body = await Console.In.ReadToEndAsync(ct);
            readFrom = "piped input";
        }
        else
        {
            return ConsoleOutput.Error(
                "Nothing to add. Say where the text comes from:\n" +
                "  vault add --clipboard --title \"...\"       what you've copied\n" +
                "  vault add --file notes.md                 a file\n" +
                "  Get-Content notes.md | vault add          piped input");
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return ConsoleOutput.Error($"No text to add: the {readFrom} is empty.");
        }

        long id;
        try
        {
            var entry = new NewVaultEntry(title, body, source ?? readFrom, tags is null ? null : [tags]) { AllowSecrets = allowSecret };
            id = await store.AddAsync(entry, ct);
        }
        catch (SecretDetectedException ex)
        {
            ConsoleOutput.Error($"Not saved: the {readFrom} looks like it contains a secret.");
            foreach (var finding in ex.Findings)
            {
                ConsoleOutput.Error($"  {finding.Kind}: {finding.Masked}");
            }

            ConsoleOutput.Error(
                "Vault entries are plain text and are shown in search results. Keep keys in user secrets or a\n" +
                "password manager. If this really should be stored, run the command again with --allow-secret.");
            return 1;
        }

        var saved = (await store.GetAsync(id, ct))!;
        ConsoleOutput.Line($"Added {ConsoleOutput.Id(id)}  {ConsoleOutput.Bold(saved.Title)}");
        ConsoleOutput.Line(ConsoleOutput.Dim($"       {saved.Body.Length:N0} characters from {readFrom}{FormatTags(saved.Tags, prefix: ", tags: ")}"));

        // Show what was actually stored, so a wrong paste is obvious now, not at the next search.
        ConsoleOutput.Line(ConsoleOutput.Dim($"       \"{Preview(saved.Body)}\""));
        if (allowSecret)
        {
            ConsoleOutput.Line(ConsoleOutput.Dim("       Stored with --allow-secret: it will appear in search results."));
        }

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
            ConsoleOutput.Line($"The vault is empty ({store.Location}). Copy some text and run: vault add --clipboard --title \"...\"");
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

    private static string Preview(string body)
    {
        var oneLine = string.Join(' ', body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return oneLine.Length <= PreviewLength ? oneLine : oneLine[..(PreviewLength - 1)] + "…";
    }

    private static string FormatTags(IReadOnlyList<string> tags, string prefix) =>
        tags.Count == 0 ? "" : prefix + string.Join(' ', tags.Select(t => "#" + t));
}
