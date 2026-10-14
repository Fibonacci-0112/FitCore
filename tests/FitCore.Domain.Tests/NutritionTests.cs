using FitCore.Domain.Nutrition;

namespace FitCore.Domain.Tests;

public sealed class NutritionTests
{
    private static readonly Nutrients Complete = new()
    {
        EnergyKcal = 250,
        ProteinGrams = 9,
        CarbohydrateGrams = 49,
        FatGrams = 3.2m,
        FiberGrams = 2.7m,
        SugarGrams = 5,
        SaturatedFatGrams = 0.7m,
        SodiumMilligrams = 490,
    };

    [Fact]
    public void Scaling_keeps_unknown_values_unknown()
    {
        var food = new Nutrients { EnergyKcal = 200, ProteinGrams = null, SodiumMilligrams = 0 };

        var eaten = food.Scale(1.5m);

        Assert.Equal(300m, eaten.EnergyKcal);
        Assert.Null(eaten.ProteinGrams);
        Assert.Equal(0m, eaten.SodiumMilligrams);
    }

    [Fact]
    public void Scaling_multiplies_every_known_value()
    {
        var doubled = Complete.Scale(2);

        Assert.All(Nutrients.All, n => Assert.Equal(Complete[n] * 2, doubled[n]));
    }

    [Fact]
    public void Totals_count_unknown_values_instead_of_adding_zero()
    {
        Nutrients[] entries =
        [
            new() { EnergyKcal = 300, ProteinGrams = 10 },
            new() { EnergyKcal = 150, ProteinGrams = null },
            new() { EnergyKcal = null, ProteinGrams = 0 },
        ];

        var totals = NutrientTotals.Sum(entries);

        Assert.Equal(new NutrientTotal(450, 1), totals[Nutrient.EnergyKcal]);
        Assert.Equal(new NutrientTotal(10, 1), totals[Nutrient.ProteinGrams]);
        Assert.Equal(new NutrientTotal(0, 3), totals[Nutrient.FiberGrams]);
        Assert.False(totals[Nutrient.EnergyKcal].IsComplete);
    }

    [Fact]
    public void A_known_zero_is_complete()
    {
        var totals = NutrientTotals.Sum([new Nutrients { SugarGrams = 0 }]);

        Assert.True(totals[Nutrient.SugarGrams].IsComplete);
        Assert.Equal(0m, totals[Nutrient.SugarGrams].Known);
    }

    [Fact]
    public void An_empty_day_totals_zero_and_is_complete()
    {
        var totals = NutrientTotals.Sum([]);

        Assert.All(Nutrients.All, n => Assert.Equal(new NutrientTotal(0, 0), totals[n]));
    }

    [Fact]
    public void A_custom_food_with_every_required_value_is_valid()
    {
        Assert.Empty(new Nutrients { EnergyKcal = 100, ProteinGrams = 0, CarbohydrateGrams = 20, FatGrams = 1 }.ValidateForCustomFood());
        Assert.Empty(Complete.ValidateForCustomFood());
    }

    [Theory]
    [InlineData(Nutrient.EnergyKcal)]
    [InlineData(Nutrient.ProteinGrams)]
    [InlineData(Nutrient.CarbohydrateGrams)]
    [InlineData(Nutrient.FatGrams)]
    public void A_custom_food_without_kcal_or_a_macro_is_rejected(Nutrient missing)
    {
        var nutrients = Without(Complete, missing);

        Assert.Equal([new NutrientProblem(missing, NutrientProblemKind.Missing)], nutrients.ValidateForCustomFood());
    }

    [Theory]
    [InlineData(Nutrient.FiberGrams)]
    [InlineData(Nutrient.SodiumMilligrams)]
    public void Optional_nutrients_may_be_unknown(Nutrient missing)
    {
        Assert.Empty(Without(Complete, missing).ValidateForCustomFood());
    }

    [Fact]
    public void Negative_values_are_rejected()
    {
        var nutrients = Complete with { FatGrams = -1, SugarGrams = -0.1m };

        Assert.Equal(
            [new NutrientProblem(Nutrient.FatGrams, NutrientProblemKind.Negative), new NutrientProblem(Nutrient.SugarGrams, NutrientProblemKind.Negative)],
            nutrients.ValidateForCustomFood());
    }

    [Fact]
    public void An_unknown_nutrient_index_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Complete[(Nutrient)99]);
    }

    private static Nutrients Without(Nutrients n, Nutrient nutrient) => nutrient switch
    {
        Nutrient.EnergyKcal => n with { EnergyKcal = null },
        Nutrient.ProteinGrams => n with { ProteinGrams = null },
        Nutrient.CarbohydrateGrams => n with { CarbohydrateGrams = null },
        Nutrient.FatGrams => n with { FatGrams = null },
        Nutrient.FiberGrams => n with { FiberGrams = null },
        Nutrient.SodiumMilligrams => n with { SodiumMilligrams = null },
        _ => throw new ArgumentOutOfRangeException(nameof(nutrient)),
    };
}
