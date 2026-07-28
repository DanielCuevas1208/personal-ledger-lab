using PersonalLedgerLab.Core;

namespace PersonalLedgerLab.Tests;

public class MoneyTests
{
    [Fact]
    public void Of_stores_minor_units()
    {
        var m = Money.Of(12.34m, "usd");
        Assert.Equal(1234, m.MinorUnits);
        Assert.Equal("USD", m.CurrencyCode);
    }

    [Fact]
    public void Of_handles_negative_amounts()
    {
        var m = Money.Of(-7.50m, "EUR");
        Assert.Equal(-750, m.MinorUnits);
    }

    [Fact]
    public void Addition_keeps_currency()
    {
        var a = Money.Of(1.10m, "USD");
        var b = Money.Of(2.20m, "USD");
        Assert.Equal(330, (a + b).MinorUnits);
    }

    [Fact]
    public void Addition_rejects_mismatched_currency()
    {
        Assert.Throws<InvalidOperationException>(() => Money.Of(1m, "USD") + Money.Of(1m, "EUR"));
    }

    [Fact]
    public void Currency_code_must_be_three_letters()
    {
        Assert.Throws<ArgumentException>(() => new Money(0, "DOLLARS"));
        Assert.Throws<ArgumentException>(() => new Money(0, ""));
    }

    [Theory]
    [InlineData("12.34", 1234)]
    [InlineData("-5.00", -500)]
    [InlineData("0.01", 1)]
    public void Of_and_to_major_round_trip(string input, long expected)
    {
        decimal value = decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture);
        var m = Money.Of(value, "USD");
        Assert.Equal(expected, m.MinorUnits);
        Assert.Equal(value, m.ToMajor());
    }
}