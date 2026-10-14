namespace FitCore.Domain.Targets;

/// <summary>An estimated daily target. A manual target always replaces it (BRD §6.2).</summary>
/// <param name="RestingKcal">Mifflin-St Jeor resting rate, rounded.</param>
/// <param name="MaintenanceKcal">Resting rate times the activity factor, rounded.</param>
/// <param name="Kcal">The daily calorie target.</param>
/// <param name="ProteinGrams">Daily protein.</param>
/// <param name="FatGrams">Daily fat.</param>
/// <param name="CarbohydrateGrams">Daily carbohydrate: the calories left after protein and fat.</param>
/// <param name="CalorieFloorApplied">True when the rate asked for would go below the soft calorie floor, so the estimate stops at the floor.</param>
/// <param name="MacrosExceedCalories">True when protein and fat alone exceed the calorie target, so carbohydrate is zero.</param>
public sealed record TargetEstimate(
    decimal RestingKcal,
    decimal MaintenanceKcal,
    decimal Kcal,
    decimal ProteinGrams,
    decimal FatGrams,
    decimal CarbohydrateGrams,
    bool CalorieFloorApplied,
    bool MacrosExceedCalories);

/// <summary>Estimates daily calorie and macro targets from the profile and the goal's weekly rate (BRD §6.2).</summary>
public static class TargetEstimator
{
    /// <summary>The approximate energy in one kilogram of body-weight change.</summary>
    public const decimal KcalPerKg = 7700m;

    /// <summary>The share of calories the estimate gives to fat.</summary>
    public const decimal FatShareOfKcal = 0.25m;

    /// <summary>
    /// Estimates targets. FitCore's own estimate never goes below the soft calorie floor;
    /// a member who wants less sets a manual target and goes through the guardrails.
    /// </summary>
    /// <param name="sex">For the Mifflin-St Jeor equation.</param>
    /// <param name="ageYears">Completed years.</param>
    /// <param name="heightCm">Height.</param>
    /// <param name="weightKg">Current weight.</param>
    /// <param name="activity">Activity level.</param>
    /// <param name="kgPerWeek">The goal's signed weekly rate, negative for loss.</param>
    /// <param name="settings">The guardrail settings; the defaults when null.</param>
    public static TargetEstimate Estimate(
        BiologicalSex sex,
        int ageYears,
        decimal heightCm,
        decimal weightKg,
        ActivityLevel activity,
        decimal kgPerWeek,
        GuardrailSettings? settings = null)
    {
        settings ??= GuardrailSettings.Default;
        var resting = EnergyExpenditure.RestingKcal(sex, weightKg, heightCm, ageYears);
        var maintenance = EnergyExpenditure.TotalKcal(resting, activity);

        var kcal = Round(maintenance + (kgPerWeek * KcalPerKg / 7));
        var floorApplied = kcal < settings.SoftCalorieFloorKcal;
        if (floorApplied)
        {
            kcal = settings.SoftCalorieFloorKcal;
        }

        var protein = Round(settings.ProteinGramsPerKg * weightKg);
        var fat = Round(kcal * FatShareOfKcal / 9);
        var remaining = kcal - (protein * 4) - (fat * 9);
        var carbohydrate = remaining > 0 ? Round(remaining / 4) : 0;

        return new TargetEstimate(Round(resting), Round(maintenance), kcal, protein, fat, carbohydrate, floorApplied, remaining < 0);
    }

    private static decimal Round(decimal value) => decimal.Round(value, MidpointRounding.AwayFromZero);
}
