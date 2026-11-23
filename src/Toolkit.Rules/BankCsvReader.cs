using System.Globalization;

namespace Toolkit.Rules;

/// <summary>A data row that couldn't be read, and why. The rest of the file is still read.</summary>
public sealed record SkippedRow(int LineNumber, string Reason);

/// <summary>What a CSV read produced: the transactions, plus any rows that were skipped.</summary>
public sealed record CsvReadResult(IReadOnlyList<Transaction> Transactions, IReadOnlyList<SkippedRow> Skipped);

/// <summary>
/// Reads a bank's exported transactions CSV. Needs a header row with a date column, a description
/// column and an Amount column. Column order doesn't matter, and lines before the header (account
/// details some banks put at the top of an export) are skipped.
/// </summary>
/// <remarks>
/// Built without a real export to test against, so it's deliberately lenient: a row whose date or
/// amount can't be read is reported and skipped rather than stopping the import. Categorising only
/// uses the description, so one odd row shouldn't cost every other row its category.
/// </remarks>
public static class BankCsvReader
{
    /// <summary>How far down the file to look for the header row.</summary>
    private const int MaxLinesBeforeHeader = 20;

    // Day-first formats only (as NZ banks use). A month-first format like MM/dd/yyyy is left out on
    // purpose: "04/05/2026" would silently mean 4 May in one row and April 5 in another.
    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd", "yyyy/MM/dd",
        "d/M/yyyy", "d/M/yy",
        "d-M-yyyy", "d-M-yy",
        "d.M.yyyy",
        "d MMM yyyy", "d-MMM-yyyy",
    ];

    // In order of preference: when a file has several of these, the first one listed wins.
    private static readonly string[] DescriptionColumnNames =
    [
        "description", "details", "payee", "narrative", "narration", "memo/description", "memo",
        "transaction description", "transaction details", "merchant", "other party",
    ];

    public static CsvReadResult Read(string path)
    {
        using var reader = new StreamReader(path);
        return ReadFrom(reader);
    }

    public static CsvReadResult ReadFrom(TextReader reader)
    {
        var (header, lineNumber) = FindHeader(reader);

        var dateColumn = IndexOf(header, "date");
        var descriptionColumn = IndexOfPreferred(header, DescriptionColumnNames);
        var amountColumn = IndexOf(header, "amount");
        var required = Math.Max(dateColumn, Math.Max(descriptionColumn, amountColumn));

        var transactions = new List<Transaction>();
        var skipped = new List<SkippedRow>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var fields = CsvLine.Split(line);
            if (fields.Length <= required)
            {
                skipped.Add(new SkippedRow(lineNumber, $"expected at least {required + 1} fields, found {fields.Length}"));
                continue;
            }

            var description = fields[descriptionColumn].Trim();
            if (!TryParseDate(fields[dateColumn], out var date))
            {
                skipped.Add(new SkippedRow(lineNumber, $"\"{fields[dateColumn].Trim()}\" isn't a date in a recognised format"));
                continue;
            }

            if (!TryParseAmount(fields[amountColumn], out var amount))
            {
                skipped.Add(new SkippedRow(lineNumber, $"\"{fields[amountColumn].Trim()}\" isn't a number"));
                continue;
            }

            transactions.Add(new Transaction(date, description, amount));
        }

        return new CsvReadResult(transactions, skipped);
    }

    /// <summary>
    /// The first line, within the first <see cref="MaxLinesBeforeHeader"/>, that names a date, a
    /// description and an amount column. Returns it split into fields, with its line number.
    /// </summary>
    private static (string[] Header, int LineNumber) FindHeader(TextReader reader)
    {
        string? firstLine = null;
        for (var lineNumber = 1; lineNumber <= MaxLinesBeforeHeader; lineNumber++)
        {
            var line = reader.ReadLine();
            if (line is null)
            {
                break;
            }

            firstLine ??= line;
            var fields = CsvLine.Split(line);
            if (IndexOf(fields, "date") >= 0
                && IndexOfPreferred(fields, DescriptionColumnNames) >= 0
                && IndexOf(fields, "amount") >= 0)
            {
                return (fields, lineNumber);
            }
        }

        throw new InvalidDataException(firstLine is null
            ? "The file is empty."
            : $"No header row with a Date, a description (Description, Details, Payee, Memo, …) and an Amount column " +
              $"in the first {MaxLinesBeforeHeader} lines. The first line is: {firstLine}");
    }

    private static int IndexOf(string[] header, string name) =>
        Array.FindIndex(header, h => string.Equals(h.Trim(), name, StringComparison.OrdinalIgnoreCase));

    private static int IndexOfPreferred(string[] header, string[] names)
    {
        foreach (var name in names)
        {
            var index = IndexOf(header, name);
            if (index >= 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static bool TryParseDate(string text, out DateOnly date) =>
        DateOnly.TryParseExact(text.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static bool TryParseAmount(string text, out decimal amount) =>
        decimal.TryParse(text.Trim().Replace(",", ""), NumberStyles.Number | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out amount);
}
