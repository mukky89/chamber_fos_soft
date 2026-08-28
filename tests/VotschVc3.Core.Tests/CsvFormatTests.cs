using System.Globalization;
using VotschVc3.Core.Recording;
using Xunit;

namespace VotschVc3.Core.Tests;

/// <summary>
/// The S!MPAC controller reports the actual temperature to four decimals (SIMPATI shows e.g.
/// 40,0213 °C) and <c>Ascii2Protocol.ParseReading</c> keeps every digit, so the recording must
/// not round it away on the way to the file.
/// </summary>
public class CsvFormatTests
{
    [Fact]
    public void A_measured_value_keeps_the_decimals_the_chamber_reported()
    {
        Assert.Equal("40,0213", CsvFormat.Fmt(40.0213));
        Assert.Equal("-19,9876", CsvFormat.Fmt(-19.9876));
    }

    /// <summary>A controller that really only sends one decimal must not gain fake zeros.</summary>
    [Fact]
    public void Trailing_zeros_are_not_invented()
    {
        Assert.Equal("40,0", CsvFormat.Fmt(40.0));
        Assert.Equal("25,5", CsvFormat.Fmt(25.5));
    }

    [Fact]
    public void A_missing_value_is_an_empty_cell()
    {
        Assert.Equal(string.Empty, CsvFormat.Fmt(null));
    }

    /// <summary>The CSV uses a comma decimal separator, so the file opens in a Slovak Excel
    /// regardless of the machine's locale.</summary>
    [Fact]
    public void The_decimal_separator_is_a_comma_whatever_the_machine_locale_is()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; // would print a dot
            Assert.Equal("12,3456", CsvFormat.Fmt(12.3456));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>An explicit format still wins – the reference thermometer keeps its three
    /// decimals with the trailing zeros its specification implies.</summary>
    [Fact]
    public void An_explicit_format_overrides_the_default()
    {
        Assert.Equal("25,100", CsvFormat.Fmt(25.1, "0.000"));
    }
}
