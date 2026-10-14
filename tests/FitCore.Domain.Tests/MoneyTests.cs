using FitCore.Domain.Costs;

namespace FitCore.Domain.Tests;

public sealed class MoneyTests
{
    [Theory]
    [InlineData("USD", 2)]
    [InlineData("EUR", 2)]
    [InlineData("JPY", 0)]
    [InlineData("KWD", 3)]
    [InlineData("CLF", 4)]
    [InlineData("UYW", 4)]
    public void Knows_each_currency_minor_unit(string code, int digits)
    {
        Assert.Equal(digits, CurrencyCode.Parse(code).MinorUnitDigits);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("usd")]
    [InlineData("US")]
    [InlineData("USDX")]
    [InlineData("U5D")]
    [InlineData("AAA")]
    [InlineData("XXX")]
    [InlineData("XAU")]
    public void Rejects_anything_but_an_assigned_currency_with_a_minor_unit(string? text)
    {
        Assert.False(CurrencyCode.TryParse(text, out _));
        Assert.Throws<FormatException>(() => CurrencyCode.Parse(text!));
    }

    [Theory]
    [InlineData("3.49", "USD", 349)]
    [InlineData("3.495", "USD", 350)]
    [InlineData("3.494", "USD", 349)]
    [InlineData("-0.005", "USD", -1)]
    [InlineData("1200", "JPY", 1200)]
    [InlineData("1200.5", "JPY", 1201)]
    [InlineData("1.2345", "KWD", 1235)]
    [InlineData("1.2345", "CLF", 12345)]
    [InlineData("1.23455", "UYW", 12346)]
    public void Converts_major_units_to_integer_minor_units(string major, string currency, long minor)
    {
        var money = Money.FromMajor(decimal.Parse(major, System.Globalization.CultureInfo.InvariantCulture), CurrencyCode.Parse(currency));

        Assert.Equal(minor, money.MinorUnits);
    }

    [Theory]
    [InlineData(349, "USD", "3.49")]
    [InlineData(1200, "JPY", "1200")]
    [InlineData(1235, "KWD", "1.235")]
    [InlineData(12345, "CLF", "1.2345")]
    public void Converts_minor_units_back_to_major_units(long minor, string currency, string major)
    {
        Assert.Equal(decimal.Parse(major, System.Globalization.CultureInfo.InvariantCulture), new Money(minor, CurrencyCode.Parse(currency)).ToMajor());
    }

    [Fact]
    public void Zero_is_zero_minor_units()
    {
        Assert.Equal(new Money(0, CurrencyCode.Usd), Money.Zero(CurrencyCode.Usd));
        Assert.Equal("USD", CurrencyCode.Usd.ToString());
    }
}
