namespace FitCore.Domain.Nutrition;

/// <summary>The nutrients FitCore tracks.</summary>
public enum Nutrient
{
    EnergyKcal,
    ProteinGrams,
    CarbohydrateGrams,
    FatGrams,
    FiberGrams,
    SugarGrams,
    SaturatedFatGrams,
    SodiumMilligrams,
}

/// <summary>
/// Nutrient values for a food or an entry. <c>null</c> means unknown, never zero (BRD §6.1, NFR-REL-5).
/// </summary>
public sealed record Nutrients
{
    /// <summary>Every nutrient, in display order.</summary>
    public static IReadOnlyList<Nutrient> All { get; } = Enum.GetValues<Nutrient>();

    /// <summary>The nutrients every custom food must declare.</summary>
    public static IReadOnlyList<Nutrient> RequiredForCustomFoods { get; } =
        [Nutrient.EnergyKcal, Nutrient.ProteinGrams, Nutrient.CarbohydrateGrams, Nutrient.FatGrams];

    /// <summary>Every value unknown.</summary>
    public static Nutrients Unknown { get; } = new();

    public decimal? EnergyKcal { get; init; }

    public decimal? ProteinGrams { get; init; }

    public decimal? CarbohydrateGrams { get; init; }

    public decimal? FatGrams { get; init; }

    public decimal? FiberGrams { get; init; }

    public decimal? SugarGrams { get; init; }

    public decimal? SaturatedFatGrams { get; init; }

    public decimal? SodiumMilligrams { get; init; }

    public decimal? this[Nutrient nutrient] => nutrient switch
    {
        Nutrient.EnergyKcal => EnergyKcal,
        Nutrient.ProteinGrams => ProteinGrams,
        Nutrient.CarbohydrateGrams => CarbohydrateGrams,
        Nutrient.FatGrams => FatGrams,
        Nutrient.FiberGrams => FiberGrams,
        Nutrient.SugarGrams => SugarGrams,
        Nutrient.SaturatedFatGrams => SaturatedFatGrams,
        Nutrient.SodiumMilligrams => SodiumMilligrams,
        _ => throw new ArgumentOutOfRangeException(nameof(nutrient), nutrient, null),
    };

    /// <summary>Multiplies every known value; unknown values stay unknown.</summary>
    public Nutrients Scale(decimal multiplier) => new()
    {
        EnergyKcal = EnergyKcal * multiplier,
        ProteinGrams = ProteinGrams * multiplier,
        CarbohydrateGrams = CarbohydrateGrams * multiplier,
        FatGrams = FatGrams * multiplier,
        FiberGrams = FiberGrams * multiplier,
        SugarGrams = SugarGrams * multiplier,
        SaturatedFatGrams = SaturatedFatGrams * multiplier,
        SodiumMilligrams = SodiumMilligrams * multiplier,
    };

    /// <summary>
    /// What stops these values from being saved as a custom food: a required nutrient that is unknown, or any negative value.
    /// An empty list means they're valid.
    /// </summary>
    public IReadOnlyList<NutrientProblem> ValidateForCustomFood()
    {
        var problems = new List<NutrientProblem>();
        foreach (var nutrient in All)
        {
            var value = this[nutrient];
            if (value is null && RequiredForCustomFoods.Contains(nutrient))
            {
                problems.Add(new NutrientProblem(nutrient, NutrientProblemKind.Missing));
            }
            else if (value < 0)
            {
                problems.Add(new NutrientProblem(nutrient, NutrientProblemKind.Negative));
            }
        }

        return problems;
    }
}

public enum NutrientProblemKind
{
    Missing,
    Negative,
}

public readonly record struct NutrientProblem(Nutrient Nutrient, NutrientProblemKind Kind);
