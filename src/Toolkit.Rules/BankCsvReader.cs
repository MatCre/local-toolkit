using System.Globalization;

namespace Toolkit.Rules;

/// <summary>
/// Reads a bank's exported transactions CSV. Expects a header row with a date column, a
/// description column (named Description, Details, Payee or Narrative), and an Amount column
/// where spending is negative. Column order and exact names don't matter; which bank's export
/// looks like this does.
/// </summary>
public static class BankCsvReader
{
    private static readonly string[] DateFormats = ["yyyy-MM-dd", "dd/MM/yyyy", "d/MM/yyyy", "MM/dd/yyyy"];
    private static readonly string[] DescriptionColumnNames = ["description", "details", "payee", "narrative"];

    public static IReadOnlyList<Transaction> Read(string path)
    {
        using var reader = new StreamReader(path);
        return ReadFrom(reader);
    }

    public static IReadOnlyList<Transaction> ReadFrom(TextReader reader)
    {
        var headerLine = reader.ReadLine() ?? throw new InvalidDataException("The file has no header row.");
        var header = CsvLine.Split(headerLine);

        var dateColumn = IndexOf(header, "date");
        var descriptionColumn = IndexOfAny(header, DescriptionColumnNames);
        var amountColumn = IndexOf(header, "amount");

        if (dateColumn < 0 || descriptionColumn < 0 || amountColumn < 0)
        {
            throw new InvalidDataException(
                $"Expected a Date, a Description (or Details/Payee/Narrative) and an Amount column. " +
                $"Found: {string.Join(", ", header)}");
        }

        var transactions = new List<Transaction>();
        var lineNumber = 1;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var fields = CsvLine.Split(line);
            var required = Math.Max(dateColumn, Math.Max(descriptionColumn, amountColumn));
            if (fields.Length <= required)
            {
                throw new InvalidDataException($"Line {lineNumber}: expected at least {required + 1} fields, found {fields.Length}.");
            }

            transactions.Add(new Transaction(
                ParseDate(fields[dateColumn], lineNumber),
                fields[descriptionColumn].Trim(),
                ParseAmount(fields[amountColumn], lineNumber)));
        }

        return transactions;
    }

    private static int IndexOf(string[] header, string name) =>
        Array.FindIndex(header, h => string.Equals(h.Trim(), name, StringComparison.OrdinalIgnoreCase));

    private static int IndexOfAny(string[] header, string[] names) =>
        Array.FindIndex(header, h => names.Contains(h.Trim(), StringComparer.OrdinalIgnoreCase));

    private static DateOnly ParseDate(string text, int lineNumber)
    {
        text = text.Trim();
        if (DateOnly.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        throw new InvalidDataException($"Line {lineNumber}: \"{text}\" isn't a date in a recognised format.");
    }

    private static decimal ParseAmount(string text, int lineNumber)
    {
        text = text.Trim().Replace(",", "");
        if (decimal.TryParse(text, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var amount))
        {
            return amount;
        }

        throw new InvalidDataException($"Line {lineNumber}: \"{text}\" isn't a number.");
    }
}
