namespace FitCore.Domain.Quantities;

/// <summary>
/// How an amount is expressed. Every mass unit converts exactly to grams and every volume unit to millilitres,
/// the canonical units. Food-specific measures such as "1 slice" or "1 cup, chopped = 128 g" are servings (BRD §6.1).
/// </summary>
public enum QuantityUnit
{
    Grams,
    Milliliters,
    Servings,
    Milligrams,
    Kilograms,
    Ounces,
    Pounds,
    Liters,
    Teaspoons,
    Tablespoons,
    FluidOunces,
    Cups,
    Pints,
    Quarts,
    Gallons,
}

/// <summary>An amount of a food as the member entered it.</summary>
/// <param name="Amount">Must be positive.</param>
/// <param name="Unit">The unit of <paramref name="Amount"/>.</param>
/// <param name="ServingId">The serving, when <paramref name="Unit"/> is <see cref="QuantityUnit.Servings"/>.</param>
public readonly record struct Quantity(decimal Amount, QuantityUnit Unit, Guid? ServingId = null)
{
    public static Quantity Grams(decimal amount) => new(amount, QuantityUnit.Grams);

    public static Quantity Milliliters(decimal amount) => new(amount, QuantityUnit.Milliliters);

    public static Quantity Servings(decimal count, Guid servingId) => new(count, QuantityUnit.Servings, servingId);

    /// <summary>An amount in a mass or volume unit. Use <see cref="Servings"/> for servings.</summary>
    public static Quantity Of(decimal amount, QuantityUnit unit) => unit == QuantityUnit.Servings
        ? throw new ArgumentException("A serving needs its serving ID.", nameof(unit))
        : new(amount, unit);
}

/// <summary>A household measure or count for one food, such as "1 slice = 28 g".</summary>
public sealed record Serving(Guid Id, string Label, decimal? Grams = null, decimal? Milliliters = null);

/// <summary>What a food's nutrient values are per.</summary>
public enum NutrientBasis
{
    Per100Grams,
    Per100Milliliters,
    PerServing,
}

/// <summary>How a food can be measured: its nutrient basis, its servings and, if known, its density.</summary>
/// <param name="Basis">What the food's nutrient values are per.</param>
/// <param name="Servings">The food's servings.</param>
/// <param name="BasisServingId">The serving the nutrients are per, when <paramref name="Basis"/> is <see cref="NutrientBasis.PerServing"/>.</param>
/// <param name="DensityGramsPerMilliliter">Needed for any mass↔volume conversion. Never guessed.</param>
public sealed record FoodMeasures(
    NutrientBasis Basis,
    IReadOnlyList<Serving> Servings,
    Guid? BasisServingId = null,
    decimal? DensityGramsPerMilliliter = null);

/// <summary>Why a quantity can't be used.</summary>
public enum QuantityError
{
    /// <summary>The amount is zero or negative.</summary>
    InvalidAmount,

    /// <summary>The serving doesn't belong to this food.</summary>
    UnknownServing,

    /// <summary>The food's nutrients are per serving, but that serving is missing.</summary>
    MissingBasisServing,

    /// <summary>The amount can't be expressed in the unit needed, for example millilitres of a food with no density.</summary>
    Unconvertible,
}

/// <summary>A quantity expressed in grams and millilitres, where each is known.</summary>
public readonly record struct Measures(decimal? Grams, decimal? Milliliters, Guid? ServingId, decimal? ServingCount);

/// <summary>A quantity resolved against a food's nutrient basis.</summary>
/// <param name="Measures">The quantity in grams and millilitres.</param>
/// <param name="BasisMultiplier">How many basis units were eaten: 1.5 for 150 g of a food whose nutrients are per 100 g.</param>
public readonly record struct ResolvedQuantity(Measures Measures, decimal BasisMultiplier);
