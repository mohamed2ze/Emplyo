using System.Text;
using Emplyo.Web.Services;
using Xunit;

namespace Emplyo.UnitTests;

public class CsvTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("plain", "plain")]
    [InlineData("  spaced  ", "  spaced  ")]
    public void Ordinary_values_are_left_unchanged(string? value, string expected)
    {
        Assert.Equal(expected, Csv.Escape(value));
    }

    [Fact]
    public void Commas_quotes_and_newlines_are_quoted_and_escaped()
    {
        Assert.Equal("\"Smith, John\"", Csv.Escape("Smith, John"));
        Assert.Equal("\"He said \"\"hello\"\"\"", Csv.Escape("He said \"hello\""));
        Assert.Equal("\"line1\nline2\"", Csv.Escape("line1\nline2"));
        Assert.Equal("\"line1\r\nline2\"", Csv.Escape("line1\r\nline2"));
    }

    [Theory]
    [InlineData("=cmd")]
    [InlineData("+cmd")]
    [InlineData("-cmd")]
    [InlineData("@cmd")]
    [InlineData("\tcmd")]
    [InlineData("\rcmd")]
    [InlineData("\ncmd")]
    public void Formula_injection_prefixes_are_neutralized(string value)
    {
        var escaped = Csv.Escape(value);

        Assert.Contains("'" + value, escaped);
    }

    [Fact]
    public void A_formula_that_also_needs_quoting_is_still_neutralized()
    {
        Assert.Equal("\"'=SUM(A1,A2)\"", Csv.Escape("=SUM(A1,A2)"));
    }

    [Fact]
    public void Line_joins_escaped_values()
    {
        Assert.Equal("a,\"b,c\",", Csv.Line("a", "b,c", null));
    }

    [Fact]
    public void ToBytes_emits_a_utf8_bom_crlf_separators_and_a_trailing_newline()
    {
        var bytes = Csv.ToBytes(new[] { "one", "two" });

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Equal("one\r\ntwo\r\n", Encoding.UTF8.GetString(bytes[3..]));
    }
}
