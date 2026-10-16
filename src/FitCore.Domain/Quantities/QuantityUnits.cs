namespace FitCore.Domain.Quantities;

/// <summary>What a unit measures.</summary>
public enum Dimension
{
    /// <summary>Converts to grams.</summary>
    Mass,

    /// <summary>Converts to millilitres.</summary>
    Volume,

    /// <summary>A food-specific measure, which converts only through that food's servings.</summary>
    Serving,
}

/// <summary>
/// Conversions between units of the same dimension. US customary units use their exact definitions:
/// the international avoirdupois pound (453.59237 g) and the US liquid gallon (231 cubic inches, 3,785.411784 ml).
/// A cup is the US customary cup of 236.5882365 ml, not the 240 ml "legal cup" of nutrition labels;
/// a label's own gram weight for a serving is stored as a serving.
/// </summary>
public static class QuantityUnits
{
    public const decimal GramsPerPound = 453.59237m;
    public const decimal GramsPerOunce = GramsPerPound / 16;
    public const decimal MillilitersPerFluidOunce = 29.5735295625m;

    /// <summary>Every mass and volume unit; servings are per food.</summary>
    public static IReadOnlyList<QuantityUnit> Measurable { get; } =
        [.. Enum.GetValues<QuantityUnit>().Where(u => DimensionOf(u) != Dimension.Serving)];

    public static Dimension DimensionOf(QuantityUnit unit) => unit switch
    {
        QuantityUnit.Milligrams or QuantityUnit.Grams or QuantityUnit.Kilograms or QuantityUnit.Ounces or QuantityUnit.Pounds
            => Dimension.Mass,
        QuantityUnit.Milliliters or QuantityUnit.Liters or QuantityUnit.Teaspoons or QuantityUnit.Tablespoons
            or QuantityUnit.FluidOunces or QuantityUnit.Cups or QuantityUnit.Pints or QuantityUnit.Quarts or QuantityUnit.Gallons
            => Dimension.Volume,
        QuantityUnit.Servings => Dimension.Serving,
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null),
    };

    /// <summary>How many grams (mass) or millilitres (volume) one of <paramref name="unit"/> is.</summary>
    public static decimal CanonicalPerUnit(QuantityUnit unit) => unit switch
    {
        QuantityUnit.Milligrams => 0.001m,
        QuantityUnit.Grams => 1m,
        QuantityUnit.Kilograms => 1000m,
        QuantityUnit.Ounces => GramsPerOunce,
        QuantityUnit.Pounds => GramsPerPound,
        QuantityUnit.Milliliters => 1m,
        QuantityUnit.Liters => 1000m,
        QuantityUnit.Teaspoons => MillilitersPerFluidOunce / 6,
        QuantityUnit.Tablespoons => MillilitersPerFluidOunce / 2,
        QuantityUnit.FluidOunces => MillilitersPerFluidOunce,
        QuantityUnit.Cups => MillilitersPerFluidOunce * 8,
        QuantityUnit.Pints => MillilitersPerFluidOunce * 16,
        QuantityUnit.Quarts => MillilitersPerFluidOunce * 32,
        QuantityUnit.Gallons => MillilitersPerFluidOunce * 128,
        _ => throw new ArgumentException("A serving has no fixed size; convert it through the food's servings.", nameof(unit)),
    };

    /// <summary>The amount in grams (mass units) or millilitres (volume units).</summary>
    public static decimal ToCanonical(decimal amount, QuantityUnit unit) => amount * CanonicalPerUnit(unit);

    /// <summary>The amount in <paramref name="unit"/>, from grams (mass units) or millilitres (volume units).</summary>
    public static decimal FromCanonical(decimal canonical, QuantityUnit unit) => canonical / CanonicalPerUnit(unit);

    /// <summary>
    /// Converts an amount between units, for display or entry. Mass↔volume needs the food's density;
    /// without it, or for servings, the conversion is refused rather than guessed.
    /// </summary>
    public static Result<decimal, QuantityError> Convert(
        decimal amount, QuantityUnit from, QuantityUnit to, decimal? densityGramsPerMilliliter = null)
    {
        var (fromDimension, toDimension) = (DimensionOf(from), DimensionOf(to));
        if (fromDimension == Dimension.Serving || toDimension == Dimension.Serving)
        {
            return QuantityError.Unconvertible;
        }

        var canonical = ToCanonical(amount, from);
        if (fromDimension != toDimension)
        {
            if (densityGramsPerMilliliter is not > 0)
            {
                return QuantityError.Unconvertible;
            }

            canonical = fromDimension == Dimension.Mass ? canonical / densityGramsPerMilliliter.Value : canonical * densityGramsPerMilliliter.Value;
        }

        return FromCanonical(canonical, to);
    }
}
