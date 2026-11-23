namespace Toolkit.Rules.Tests;

public class BankCsvReaderTests
{
    private static CsvReadResult Read(string csv) => BankCsvReader.ReadFrom(new StringReader(csv));

    [Fact]
    public void Reads_date_description_and_amount()
    {
        var result = Read(
            "Date,Description,Amount\n" +
            "2026-01-05,COUNTDOWN SUPERMARKET,-42.50\n" +
            "2026-01-06,SALARY ACME LTD,2500\n");

        Assert.Equal(
            [
                new Transaction(new DateOnly(2026, 1, 5), "COUNTDOWN SUPERMARKET", -42.50m),
                new Transaction(new DateOnly(2026, 1, 6), "SALARY ACME LTD", 2500m),
            ],
            result.Transactions);
        Assert.Empty(result.Skipped);
    }

    [Theory]
    [InlineData("Details")]
    [InlineData("Payee")]
    [InlineData("Narrative")]
    [InlineData("Memo/Description")]
    [InlineData("Memo")]
    [InlineData("Other Party")]
    [InlineData("description")] // header matching is case-insensitive
    public void Accepts_common_names_for_the_description_column(string columnName)
    {
        var result = Read($"Date,{columnName},Amount\n2026-01-05,KMART,-10\n");

        Assert.Equal("KMART", result.Transactions[0].Description);
    }

    [Fact]
    public void Prefers_Description_when_a_file_has_several_candidate_columns()
    {
        var result = Read("Date,Other Party,Description,Amount\n2026-01-05,J SMITH,KMART WAIRAU,-10\n");

        Assert.Equal("KMART WAIRAU", result.Transactions[0].Description);
    }

    [Fact]
    public void Skips_account_details_above_the_header_row()
    {
        var result = Read(
            "Created date / time : 23 November 2026 / 09:15:00\n" +
            "Bank 12; Branch 3456; Account 0123456-00 (Everyday)\n" +
            "\n" +
            "Date,Description,Amount\n" +
            "2026-01-05,KMART,-10\n");

        Assert.Equal("KMART", Assert.Single(result.Transactions).Description);
    }

    [Theory]
    [InlineData("2026-01-05")]
    [InlineData("05/01/2026")]
    [InlineData("5/1/2026")]   // single-digit day and month
    [InlineData("5/01/26")]
    [InlineData("05-01-2026")]
    [InlineData("5 Jan 2026")]
    [InlineData("05-Jan-2026")]
    public void Reads_common_day_first_date_formats(string date)
    {
        var result = Read($"Date,Description,Amount\n{date},KMART,-10\n");

        Assert.Equal(new DateOnly(2026, 1, 5), Assert.Single(result.Transactions).Date);
    }

    [Fact]
    public void Month_first_dates_are_not_guessed()
    {
        // 12/25/2026 can only be month-first. Reading it would mean 04/05/2026 means different
        // things in different rows, so it's skipped and reported instead.
        var result = Read("Date,Description,Amount\n12/25/2026,KMART,-10\n");

        Assert.Empty(result.Transactions);
        Assert.Contains("12/25/2026", Assert.Single(result.Skipped).Reason);
    }

    [Fact]
    public void Blank_lines_are_skipped()
    {
        var result = Read("Date,Description,Amount\n2026-01-05,KMART,-10\n\n2026-01-06,BUNNINGS,-20\n");

        Assert.Equal(2, result.Transactions.Count);
    }

    [Fact]
    public void Column_order_does_not_matter()
    {
        var result = Read("Amount,Date,Description\n-10,2026-01-05,KMART\n");

        Assert.Equal(new Transaction(new DateOnly(2026, 1, 5), "KMART", -10m), result.Transactions[0]);
    }

    [Fact]
    public void Says_what_it_found_when_no_header_row_has_the_columns()
    {
        var ex = Assert.Throws<InvalidDataException>(() => Read("Date,Amount\n2026-01-05,-10\n"));

        Assert.Contains("Date,Amount", ex.Message);
    }

    [Fact]
    public void Reports_an_empty_file()
    {
        var ex = Assert.Throws<InvalidDataException>(() => Read(""));

        Assert.Contains("empty", ex.Message);
    }

    [Fact]
    public void A_row_with_a_bad_date_is_skipped_and_the_rest_are_read()
    {
        var result = Read("Date,Description,Amount\n2026-01-05,OK,-1\nnot-a-date,KMART,-10\n2026-01-07,BUNNINGS,-20\n");

        Assert.Equal(["OK", "BUNNINGS"], result.Transactions.Select(t => t.Description));
        var skipped = Assert.Single(result.Skipped);
        Assert.Equal(3, skipped.LineNumber);
        Assert.Contains("not-a-date", skipped.Reason);
    }

    [Fact]
    public void A_row_with_a_bad_amount_is_skipped_and_reported()
    {
        var result = Read("Date,Description,Amount\n2026-01-05,KMART,not-a-number\n");

        Assert.Empty(result.Transactions);
        Assert.Equal(2, Assert.Single(result.Skipped).LineNumber);
    }

    [Fact]
    public void Line_numbers_count_the_lines_above_the_header()
    {
        var result = Read("Account summary\nDate,Description,Amount\nbad,KMART,-10\n");

        Assert.Equal(3, Assert.Single(result.Skipped).LineNumber);
    }

    [Fact]
    public void A_short_row_is_skipped_and_reported()
    {
        var result = Read("Date,Description,Amount\n2026-01-05,KMART\n");

        Assert.Contains("fields", Assert.Single(result.Skipped).Reason);
    }

    [Fact]
    public void A_quoted_description_containing_a_comma_is_kept_whole()
    {
        var result = Read("Date,Description,Amount\n2026-01-05,\"SMITH, JOHN - TRANSFER\",-10\n");

        Assert.Equal("SMITH, JOHN - TRANSFER", result.Transactions[0].Description);
    }
}
