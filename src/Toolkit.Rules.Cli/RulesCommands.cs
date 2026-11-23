using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Toolkit.AI;
using Toolkit.AI.Routing;
using Toolkit.Rules;
using Toolkit.Vault.AI;

namespace Toolkit.Rules.Cli;

/// <summary>What each command does. Each returns the process exit code: 0 for success, 1 for a problem.</summary>
internal static class RulesCommands
{
    private const int MaxSkippedRowsShown = 5;

    /// <summary>
    /// One folder per user, the same from wherever `rules` is run: %LOCALAPPDATA%\local-toolkit on
    /// Windows, next to the vault. A path relative to the current folder would give each folder its
    /// own, empty, rules file.
    /// </summary>
    private static readonly string DataDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "local-toolkit");

    public static string DefaultRulesPath { get; } = Path.Combine(DataDirectory, "rules.json");

    public static string DefaultProposedPath { get; } = Path.Combine(DataDirectory, "rules.proposed.json");

    public static Task<int> CategorizeAsync(FileInfo csv, string rulesPath, CancellationToken ct)
    {
        if (!TryReadTransactions(csv, out var transactions, out var error)
            || !TryLoadRules(rulesPath, out var ruleSet, out error))
        {
            return Task.FromResult(ConsoleOutput.Error(error));
        }

        if (!File.Exists(rulesPath))
        {
            ConsoleOutput.Line(ConsoleOutput.Dim($"No rules file at {rulesPath} yet, so every row is unmatched."));
        }

        var summary = ruleSet.Categorize(transactions);
        PrintSummary(summary, ruleSet.Rules.Count, rulesPath);
        return Task.FromResult(0);
    }

    public static async Task<int> ProposeAsync(FileInfo csv, string rulesPath, string proposedPath, int limit, CancellationToken ct)
    {
        if (!TryReadTransactions(csv, out var transactions, out var error)
            || !TryLoadRules(rulesPath, out var ruleSet, out error))
        {
            return ConsoleOutput.Error(error);
        }

        var summary = ruleSet.Categorize(transactions);
        var unmatched = summary.UnmatchedDescriptions(limit);

        if (unmatched.Count == 0)
        {
            ConsoleOutput.Line("Nothing unmatched: every row already has a rule.");
            return 0;
        }

        ConsoleOutput.Line(
            $"{unmatched.Count} distinct unmatched payee(s) behind {summary.Uncategorized.Count} of {summary.Total} rows. " +
            "Asking the local model once (Ollama only; not saved to the vault)...");

        using var host = BuildLocalOnlyHost();
        var chat = host.Services.GetRequiredService<IChatClient>();

        List<CategoryRule> proposed;
        try
        {
            proposed = await AskForProposalsAsync(chat, unmatched, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ConsoleOutput.Error($"The local model didn't answer: {ex.Message}");
        }

        if (proposed.Count == 0)
        {
            return ConsoleOutput.Error("The model's reply didn't contain anything usable as rules.");
        }

        // Written to disk, not just printed, so "rules apply" commits exactly what's reviewed here
        // rather than asking the model again and risking a different answer the second time.
        RuleSetFile.Save(proposedPath, new RuleSet(proposed));

        ConsoleOutput.Line($"\nProposed {proposed.Count} rule(s), written to {proposedPath}:");
        foreach (var rule in proposed)
        {
            ConsoleOutput.Line($"  \"{rule.Pattern}\" -> {rule.Category}");
        }

        ConsoleOutput.Line(ConsoleOutput.Dim("\nReview them (edit or delete lines in that file if needed), then run: rules apply"));
        return 0;
    }

    public static Task<int> ApplyAsync(string proposedPath, string rulesPath, CancellationToken ct)
    {
        if (!File.Exists(proposedPath))
        {
            return Task.FromResult(ConsoleOutput.Error($"No proposed rules at {proposedPath}. Run \"rules propose\" first."));
        }

        if (!TryLoadRules(proposedPath, out var proposed, out var error)
            || !TryLoadRules(rulesPath, out var current, out error))
        {
            return Task.FromResult(ConsoleOutput.Error(error));
        }

        var merged = current.With(proposed.Rules);
        RuleSetFile.Save(rulesPath, merged);

        var added = merged.Rules.Count - current.Rules.Count;
        ConsoleOutput.Line(
            $"Added {added} rule(s) to {rulesPath} ({merged.Rules.Count} total; " +
            $"{proposed.Rules.Count - added} were already covered). Re-run categorize to see the new match rate.");
        return Task.FromResult(0);
    }

    private static bool TryReadTransactions(FileInfo csv, out IReadOnlyList<Transaction> transactions, out string error)
    {
        transactions = [];
        error = "";

        if (!csv.Exists)
        {
            error = $"File not found: {csv.FullName}";
            return false;
        }

        CsvReadResult result;
        try
        {
            result = BankCsvReader.Read(csv.FullName);
        }
        catch (InvalidDataException ex)
        {
            error = $"Couldn't read {csv.Name}: {ex.Message}";
            return false;
        }

        if (result.Skipped.Count > 0)
        {
            ConsoleOutput.Line(ConsoleOutput.Dim($"Skipped {result.Skipped.Count} row(s) that couldn't be read:"));
            foreach (var row in result.Skipped.Take(MaxSkippedRowsShown))
            {
                ConsoleOutput.Line(ConsoleOutput.Dim($"  line {row.LineNumber}: {row.Reason}"));
            }

            if (result.Skipped.Count > MaxSkippedRowsShown)
            {
                ConsoleOutput.Line(ConsoleOutput.Dim($"  …and {result.Skipped.Count - MaxSkippedRowsShown} more"));
            }
        }

        if (result.Transactions.Count == 0)
        {
            error = $"No rows could be read from {csv.Name}.";
            return false;
        }

        transactions = result.Transactions;
        return true;
    }

    /// <summary>Loads a rules file, turning a broken one into a message that names the file and the rule.</summary>
    private static bool TryLoadRules(string path, out RuleSet ruleSet, out string error)
    {
        try
        {
            ruleSet = RuleSetFile.Load(path);
            error = "";
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            ruleSet = RuleSet.Empty;
            error = ex.Message;
            return false;
        }
    }

    private static void PrintSummary(CategorizationSummary summary, int ruleCount, string rulesPath)
    {
        ConsoleOutput.Line(
            $"{summary.Categorized:N0} of {summary.Total:N0} rows categorised by {ruleCount} rule(s) " +
            $"({summary.CategorizedRate:P0}), AI switched off.");
        ConsoleOutput.Line(ConsoleOutput.Dim($"Rules: {rulesPath}"));

        if (summary.Uncategorized.Count > 0)
        {
            ConsoleOutput.Line(ConsoleOutput.Dim(
                $"{summary.Uncategorized.Count:N0} unmatched. Try: rules propose --csv <same file>"));
        }
    }

    /// <summary>One AI call: a single prompt listing every unmatched payee, local-only and marked out of the vault.</summary>
    private static async Task<List<CategoryRule>> AskForProposalsAsync(
        IChatClient chat, IReadOnlyList<string> unmatchedDescriptions, CancellationToken ct)
    {
        var prompt = RuleProposalPrompt.Build(unmatchedDescriptions);

        var options = new ChatOptions().WithoutVaultCapture();
        (options.AdditionalProperties ??= new())[RoutingHints.LocalOnly] = true;

        var response = await chat.GetResponseAsync(prompt, options, ct);
        return RuleProposalParser.Parse(response.Text);
    }

    /// <summary>
    /// Only the AI layer, with no storage adapter: the rules CLI makes one call per invocation,
    /// so neither a response cache nor a call log earns its keep here.
    /// </summary>
    private static IHost BuildLocalOnlyHost()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);
        builder.Configuration.AddUserSecrets<Program>(optional: true);
        builder.Configuration.AddEnvironmentVariables();

        builder.Services.AddToolkitAI(builder.Configuration);

        return builder.Build();
    }
}
