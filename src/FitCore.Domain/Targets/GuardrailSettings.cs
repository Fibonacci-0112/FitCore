namespace FitCore.Domain.Targets;

/// <summary>
/// The safety limits for targets (BRD §6.2). The defaults are the BRD's initial values; final values need clinical review.
/// </summary>
/// <param name="MaxLossFractionPerWeek">Soft limit: weekly weight loss as a fraction of current body weight.</param>
/// <param name="SoftCalorieFloorKcal">Soft limit: the lowest daily calories without an explicit override.</param>
/// <param name="ProteinGramsPerKg">Soft limit and estimate default: daily protein per kilogram of body weight.</param>
/// <param name="HardCalorieFloorKcal">Hard floor: daily calories below this are blocked for members and coaches.</param>
/// <param name="MinimumGoalBmi">Hard floor: a goal weight with a BMI below this is blocked for members and coaches.</param>
public sealed record GuardrailSettings(
    decimal MaxLossFractionPerWeek = 0.01m,
    decimal SoftCalorieFloorKcal = 1200m,
    decimal ProteinGramsPerKg = 1.6m,
    decimal HardCalorieFloorKcal = 800m,
    decimal MinimumGoalBmi = 18.5m)
{
    public static GuardrailSettings Default { get; } = new();

    /// <summary>Every limit is positive, and the soft calorie floor isn't below the hard floor.</summary>
    public bool IsValid =>
        MaxLossFractionPerWeek > 0
        && SoftCalorieFloorKcal > 0
        && ProteinGramsPerKg > 0
        && HardCalorieFloorKcal > 0
        && MinimumGoalBmi > 0
        && SoftCalorieFloorKcal >= HardCalorieFloorKcal;

    /// <summary>Throws when the settings break an invariant, which is a configuration error rather than member input.</summary>
    /// <exception cref="ArgumentException">A limit isn't positive, or the soft calorie floor is below the hard floor.</exception>
    public void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            throw new ArgumentException("Guardrail limits must be positive, and the soft calorie floor can't be below the hard floor.", "settings");
        }
    }
}
