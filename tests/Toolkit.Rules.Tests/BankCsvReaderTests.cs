namespace Toolkit.Rules.Tests;

public class BankCsvReaderTests
{
    [Fact]
    public void Reads_date_description_and_amount()
    {
        var transactions = BankCsvReader.ReadFrom(new StringReader(
            "Date,Description,Amount\n" +
            "2026-01-05,COUNTDOWN SUPERMARKET,-42.50\n" +
            "2026-01-06,SALARY ACME LTD,2500\n"));

        Assert.Equal(2, transactions.Count);
        Assert.Equal(new Transaction(new DateOnly(2026, 1, 5), "COUNTDOWN SUPERMARKET", -42.50m), transactions[0]);
        Assert.Equal(new Transaction(new DateOnly(2026, 1, 6), "SALARY ACME LTD", 2500m), transactions[1]);
    }

    [Theory]
    [InlineData("Details")]
    [InlineData("Payee")]
    [InlineData("Narrative")]
    [InlineData("description")] // header matching is case-insensitive
    public void Accepts_common_alternative_names_for_the_description_column(string columnName)
    {
        var transactions = BankCsvReader.ReadFrom(new StringReader(
            $"Date,{columnName},Amount\n2026-01-05,KMART,-10\n"));

        Assert.Equal("KMART", transactions[0].Description);
    }

    [Fact]
    public void Blank_lines_are_skipped()
    {
        var transactions = BankCsvReader.ReadFrom(new StringReader(
            "Date,Description,Amount\n2026-01-05,KMART,-10\n\n2026-01-06,BUNNINGS,-20\n"));

        Assert.Equal(2, transactions.Count);
    }

    [Fact]
    public void Column_order_does_not_matter()
    {
        var transactions = BankCsvReader.ReadFrom(new StringReader(
            "Amount,Date,Description\n-10,2026-01-05,KMART\n"));

        Assert.Equal(new Transaction(new DateOnly(2026, 1, 5), "KMART", -10m), transactions[0]);
    }

    [Fact]
    public void Throws_with_the_header_when_a_required_column_is_missing()
    {
        var ex = Assert.Throws<InvalidDataException>(() =>
            BankCsvReader.ReadFrom(new StringReader("Date,Amount\n2026-01-05,-10\n")));

        Assert.Contains("Date, Amount", ex.Message);
    }

    [Fact]
    public void Throws_naming_the_line_on_a_bad_date()
    {
        var ex = Assert.Throws<InvalidDataException>(() =>
            BankCsvReader.ReadFrom(new StringReader("Date,Description,Amount\n2026-01-05,OK,-1\nnot-a-date,KMART,-10\n")));

        Assert.Contains("Line 3", ex.Message);
    }

    [Fact]
    public void Throws_naming_the_line_on_a_bad_amount()
    {
        var ex = Assert.Throws<InvalidDataException>(() =>
            BankCsvReader.ReadFrom(new StringReader("Date,Description,Amount\n2026-01-05,KMART,not-a-number\n")));

        Assert.Contains("Line 2", ex.Message);
    }

    [Fact]
    public void A_quoted_description_containing_a_comma_is_kept_whole()
    {
        var transactions = BankCsvReader.ReadFrom(new StringReader(
            "Date,Description,Amount\n2026-01-05,\"SMITH, JOHN - TRANSFER\",-10\n"));

        Assert.Equal("SMITH, JOHN - TRANSFER", transactions[0].Description);
    }
}
