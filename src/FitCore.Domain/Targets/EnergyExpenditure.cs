namespace FitCore.Domain.Targets;

/// <summary>The sex the Mifflin-St Jeor equation is calibrated for.</summary>
public enum BiologicalSex
{
    Male,
    Female,
}

public enum ActivityLevel
{
    Sedentary,
    Light,
    Moderate,
    Active,
    VeryActive,
}

/// <summary>Daily energy needs: Mifflin-St Jeor resting rate times an activity factor (BRD §6.2).</summary>
public static class EnergyExpenditure
{
    /// <summary>The usual multipliers from resting rate to total daily energy expenditure.</summary>
    public static decimal ActivityFactor(ActivityLevel level) => level switch
    {
        ActivityLevel.Sedentary => 1.2m,
        ActivityLevel.Light => 1.375m,
        ActivityLevel.Moderate => 1.55m,
        ActivityLevel.Active => 1.725m,
        ActivityLevel.VeryActive => 1.9m,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
    };

    /// <summary>Resting energy expenditure in kcal/day, by the Mifflin-St Jeor equation.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The sex isn't defined, the weight or height isn't positive, or the age is negative.</exception>
    public static decimal RestingKcal(BiologicalSex sex, decimal weightKg, decimal heightCm, int ageYears)
    {
        if (!Enum.IsDefined(sex))
        {
            throw new ArgumentOutOfRangeException(nameof(sex), sex, null);
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(weightKg);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heightCm);
        ArgumentOutOfRangeException.ThrowIfNegative(ageYears);
        var common = (10 * weightKg) + (6.25m * heightCm) - (5 * ageYears);
        return sex == BiologicalSex.Male ? common + 5 : common - 161;
    }

    /// <summary>Total daily energy expenditure in kcal/day.</summary>
    public static decimal TotalKcal(decimal restingKcal, ActivityLevel level) => restingKcal * ActivityFactor(level);

    /// <summary>Body mass index: weight in kilograms over height in metres squared.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The weight or height isn't positive.</exception>
    public static decimal BodyMassIndex(decimal weightKg, decimal heightCm)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(weightKg);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heightCm);
        var meters = heightCm / 100;
        return weightKg / (meters * meters);
    }
}
