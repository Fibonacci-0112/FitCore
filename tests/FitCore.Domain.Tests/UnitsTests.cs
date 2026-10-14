using FitCore.Domain.Units;

namespace FitCore.Domain.Tests;

public sealed class UnitsTests
{
    [Fact]
    public void Converts_metric_to_us_customary_and_back()
    {
        Assert.Equal(220.462262185m, UsCustomary.KilogramsToPounds(100));
        Assert.Equal(1m, decimal.Round(UsCustomary.PoundsToKilograms(UsCustomary.PoundsPerKilogram), 10));
        Assert.Equal(1m, UsCustomary.GramsToOunces(UsCustomary.GramsPerOunce));
        Assert.Equal(28.349523125m, UsCustomary.OuncesToGrams(1));
        Assert.Equal(1m, UsCustomary.MillilitersToFluidOunces(UsCustomary.MillilitersPerFluidOunce));
        Assert.Equal(29.5735295625m, UsCustomary.FluidOuncesToMilliliters(1));
        Assert.Equal(70m, UsCustomary.CentimetersToInches(177.8m));
        Assert.Equal(177.8m, UsCustomary.InchesToCentimeters(70));
    }
}
