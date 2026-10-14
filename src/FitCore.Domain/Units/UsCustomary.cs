namespace FitCore.Domain.Units;

/// <summary>
/// Conversions between the metric units FitCore stores and the US customary units it shows by default (NFR-L10N-1).
/// Display only: stored values stay metric.
/// </summary>
public static class UsCustomary
{
    public const decimal PoundsPerKilogram = 2.20462262185m;
    public const decimal GramsPerOunce = 28.349523125m;
    public const decimal MillilitersPerFluidOunce = 29.5735295625m;
    public const decimal CentimetersPerInch = 2.54m;

    public static decimal KilogramsToPounds(decimal kilograms) => kilograms * PoundsPerKilogram;

    public static decimal PoundsToKilograms(decimal pounds) => pounds / PoundsPerKilogram;

    public static decimal GramsToOunces(decimal grams) => grams / GramsPerOunce;

    public static decimal OuncesToGrams(decimal ounces) => ounces * GramsPerOunce;

    public static decimal MillilitersToFluidOunces(decimal milliliters) => milliliters / MillilitersPerFluidOunce;

    public static decimal FluidOuncesToMilliliters(decimal fluidOunces) => fluidOunces * MillilitersPerFluidOunce;

    public static decimal CentimetersToInches(decimal centimeters) => centimeters / CentimetersPerInch;

    public static decimal InchesToCentimeters(decimal inches) => inches * CentimetersPerInch;
}
