using FitCore.Domain.Quantities;

namespace FitCore.Domain.Tests;

/// <summary>Sample foods shared by the tests.</summary>
internal static class Foods
{
    public static readonly Guid SliceId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public static readonly Guid CupId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    public static readonly Guid BarId = Guid.Parse("00000000-0000-0000-0000-000000000003");
    public static readonly Guid PieceId = Guid.Parse("00000000-0000-0000-0000-000000000004");
    public static readonly Guid OtherId = Guid.Parse("00000000-0000-0000-0000-0000000000ff");

    /// <summary>Bread: per 100 g, "1 slice" = 28 g, no density.</summary>
    public static readonly FoodMeasures Bread = new(NutrientBasis.Per100Grams, [new Serving(SliceId, "1 slice", Grams: 28)]);

    /// <summary>Milk: per 100 ml, "1 cup" = 240 ml, density 1.03 g/ml.</summary>
    public static readonly FoodMeasures Milk = new(
        NutrientBasis.Per100Milliliters, [new Serving(CupId, "1 cup", Milliliters: 240)], DensityGramsPerMilliliter: 1.03m);

    /// <summary>Juice: per 100 ml, no density.</summary>
    public static readonly FoodMeasures Juice = new(NutrientBasis.Per100Milliliters, []);

    /// <summary>Protein bar: per bar, "1 bar" = 60 g.</summary>
    public static readonly FoodMeasures ProteinBar = new(
        NutrientBasis.PerServing, [new Serving(BarId, "1 bar", Grams: 60)], BasisServingId: BarId);

    /// <summary>Egg: per egg, a serving with no weight or volume.</summary>
    public static readonly FoodMeasures Egg = new(
        NutrientBasis.PerServing, [new Serving(PieceId, "1 egg")], BasisServingId: PieceId);

    /// <summary>Syrup: per serving, "1 tbsp" = 15 ml, no density.</summary>
    public static readonly FoodMeasures Syrup = new(
        NutrientBasis.PerServing, [new Serving(CupId, "1 tbsp", Milliliters: 15)], BasisServingId: CupId);

    /// <summary>A per-serving food whose basis serving is missing.</summary>
    public static readonly FoodMeasures Broken = new(NutrientBasis.PerServing, [], BasisServingId: SliceId);
}
