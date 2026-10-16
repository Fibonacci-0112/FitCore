using FitCore.Domain.Quantities;

namespace FitCore.Domain.Units;

/// <summary>
/// Conversions for body measurements between the metric units FitCore stores and the US customary units it shows by default
/// (NFR-L10N-1). Display only: stored values stay metric. Food amounts convert with <see cref="QuantityUnits"/>.
/// </summary>
public static class UsCustomary
{
    public const decimal KilogramsPerPound = QuantityUnits.GramsPerPound / 1000;
    public const decimal CentimetersPerInch = 2.54m;

    public static decimal KilogramsToPounds(decimal kilograms) => kilograms / KilogramsPerPound;

    public static decimal PoundsToKilograms(decimal pounds) => pounds * KilogramsPerPound;

    public static decimal CentimetersToInches(decimal centimeters) => centimeters / CentimetersPerInch;

    public static decimal InchesToCentimeters(decimal inches) => inches * CentimetersPerInch;
}
