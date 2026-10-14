using FitCore.Domain.Quantities;
using FitCore.Domain.Units;

namespace FitCore.Domain.Tests;

public sealed class UnitsTests
{
    [Theory]
    [InlineData(QuantityUnit.Milligrams, Dimension.Mass, "0.001")]
    [InlineData(QuantityUnit.Grams, Dimension.Mass, "1")]
    [InlineData(QuantityUnit.Kilograms, Dimension.Mass, "1000")]
    [InlineData(QuantityUnit.Ounces, Dimension.Mass, "28.349523125")]
    [InlineData(QuantityUnit.Pounds, Dimension.Mass, "453.59237")]
    [InlineData(QuantityUnit.Milliliters, Dimension.Volume, "1")]
    [InlineData(QuantityUnit.Liters, Dimension.Volume, "1000")]
    [InlineData(QuantityUnit.Teaspoons, Dimension.Volume, "4.92892159375")]
    [InlineData(QuantityUnit.Tablespoons, Dimension.Volume, "14.78676478125")]
    [InlineData(QuantityUnit.FluidOunces, Dimension.Volume, "29.5735295625")]
    [InlineData(QuantityUnit.Cups, Dimension.Volume, "236.5882365")]
    [InlineData(QuantityUnit.Pints, Dimension.Volume, "473.176473")]
    [InlineData(QuantityUnit.Quarts, Dimension.Volume, "946.352946")]
    [InlineData(QuantityUnit.Gallons, Dimension.Volume, "3785.411784")]
    public void Every_unit_has_an_exact_size_in_grams_or_millilitres(QuantityUnit unit, Dimension dimension, string canonical)
    {
        Assert.Equal(dimension, QuantityUnits.DimensionOf(unit));
        Assert.Equal(decimal.Parse(canonical, System.Globalization.CultureInfo.InvariantCulture), QuantityUnits.CanonicalPerUnit(unit));
    }

    [Fact]
    public void Every_unit_but_servings_is_measurable()
    {
        Assert.Equal(Enum.GetValues<QuantityUnit>().Length - 1, QuantityUnits.Measurable.Count);
        Assert.DoesNotContain(QuantityUnit.Servings, QuantityUnits.Measurable);
        Assert.Equal(Dimension.Serving, QuantityUnits.DimensionOf(QuantityUnit.Servings));
    }

    [Theory]
    [InlineData(3, QuantityUnit.Teaspoons, QuantityUnit.Tablespoons, 1)]
    [InlineData(16, QuantityUnit.Tablespoons, QuantityUnit.Cups, 1)]
    [InlineData(8, QuantityUnit.FluidOunces, QuantityUnit.Cups, 1)]
    [InlineData(2, QuantityUnit.Cups, QuantityUnit.Pints, 1)]
    [InlineData(2, QuantityUnit.Pints, QuantityUnit.Quarts, 1)]
    [InlineData(4, QuantityUnit.Quarts, QuantityUnit.Gallons, 1)]
    [InlineData(1, QuantityUnit.Liters, QuantityUnit.Milliliters, 1000)]
    [InlineData(16, QuantityUnit.Ounces, QuantityUnit.Pounds, 1)]
    [InlineData(1000, QuantityUnit.Milligrams, QuantityUnit.Grams, 1)]
    [InlineData(2.5, QuantityUnit.Kilograms, QuantityUnit.Grams, 2500)]
    public void Converts_exactly_within_a_dimension(double amount, QuantityUnit from, QuantityUnit to, double expected)
    {
        Assert.Equal((decimal)expected, QuantityUnits.Convert((decimal)amount, from, to).Value);
    }

    [Fact]
    public void Converts_between_mass_and_volume_only_with_a_density()
    {
        Assert.Equal(103m, QuantityUnits.Convert(100, QuantityUnit.Milliliters, QuantityUnit.Grams, 1.03m).Value);
        Assert.Equal(100m, QuantityUnits.Convert(103, QuantityUnit.Grams, QuantityUnit.Milliliters, 1.03m).Value);
        Assert.Equal(QuantityError.Unconvertible, QuantityUnits.Convert(1, QuantityUnit.Cups, QuantityUnit.Grams).Error);
        Assert.Equal(QuantityError.Unconvertible, QuantityUnits.Convert(1, QuantityUnit.Ounces, QuantityUnit.FluidOunces, 0).Error);
    }

    [Fact]
    public void Servings_never_convert_without_their_food()
    {
        Assert.Equal(QuantityError.Unconvertible, QuantityUnits.Convert(1, QuantityUnit.Servings, QuantityUnit.Grams).Error);
        Assert.Equal(QuantityError.Unconvertible, QuantityUnits.Convert(1, QuantityUnit.Grams, QuantityUnit.Servings).Error);
        Assert.Throws<ArgumentException>(() => QuantityUnits.CanonicalPerUnit(QuantityUnit.Servings));
        Assert.Throws<ArgumentException>(() => Quantity.Of(1, QuantityUnit.Servings));
        Assert.Throws<ArgumentOutOfRangeException>(() => QuantityUnits.DimensionOf((QuantityUnit)99));
    }

    [Fact]
    public void Converts_body_measurements_to_us_customary_and_back()
    {
        Assert.Equal(1m, UsCustomary.PoundsToKilograms(UsCustomary.KilogramsToPounds(1)));
        Assert.Equal(45.359237m, UsCustomary.PoundsToKilograms(100));
        Assert.Equal(220.462m, decimal.Round(UsCustomary.KilogramsToPounds(100), 3));
        Assert.Equal(70m, UsCustomary.CentimetersToInches(177.8m));
        Assert.Equal(177.8m, UsCustomary.InchesToCentimeters(70));
    }
}
