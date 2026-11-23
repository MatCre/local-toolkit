namespace Toolkit.Rules.Tests;

public class CsvLineTests
{
    [Fact]
    public void Splits_plain_comma_separated_fields() =>
        Assert.Equal(["2026-01-05", "COUNTDOWN", "-42.50"], CsvLine.Split("2026-01-05,COUNTDOWN,-42.50"));

    [Fact]
    public void A_quoted_field_can_contain_a_comma() =>
        Assert.Equal(["2026-01-05", "SMITH, JOHN", "-10"], CsvLine.Split("""2026-01-05,"SMITH, JOHN",-10"""));

    [Fact]
    public void A_doubled_quote_inside_a_quoted_field_is_a_literal_quote() =>
        Assert.Equal(["SAY \"HI\" THANKS"], CsvLine.Split("\"SAY \"\"HI\"\" THANKS\""));

    [Fact]
    public void An_empty_line_is_a_single_empty_field() =>
        Assert.Equal([""], CsvLine.Split(""));

    [Fact]
    public void Trailing_empty_fields_are_kept() =>
        Assert.Equal(["a", "b", ""], CsvLine.Split("a,b,"));
}
