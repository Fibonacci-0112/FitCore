namespace FitCore.Domain.Targets;

/// <summary>A target someone wants to set, with whatever goal context comes with it.</summary>
/// <param name="Kcal">Daily calories.</param>
/// <param name="ProteinGrams">Daily protein, if set.</param>
/// <param name="KgPerWeek">The goal's signed weekly rate, if there is a goal; negative for loss.</param>
/// <param name="GoalWeightKg">The goal weight, if there is a goal.</param>
public sealed record ProposedTarget(decimal Kcal, decimal? ProteinGrams = null, decimal? KgPerWeek = null, decimal? GoalWeightKg = null);

/// <summary>A soft limit: the member can go past it after an explicit warning, and the override is logged.</summary>
public enum SoftLimit
{
    LossRateAboveMaximum,
    CaloriesBelowSoftFloor,
    ProteinBelowDefault,
}

/// <summary>A hard floor: members and coaches can't go past it. Only a clinician can, and the member is told.</summary>
public enum HardLimit
{
    CaloriesBelowHardFloor,
    GoalBmiBelowMinimum,
}

/// <summary>Who is setting a target.</summary>
public enum TargetSetter
{
    Member,
    Coach,
    Clinician,
}

/// <summary>What happens to a proposed target.</summary>
public enum TargetDecision
{
    /// <summary>No limit is crossed: the target applies.</summary>
    Apply,

    /// <summary>The member crossed a soft limit and acknowledged the warning: the target applies and the override is audited.</summary>
    ApplyAsOverride,

    /// <summary>The member crossed a soft limit: show the warning and ask them to acknowledge it.</summary>
    NeedsAcknowledgement,

    /// <summary>A coach crossed a soft limit: the target waits until the member reads the warning and accepts it.</summary>
    PendingMemberAcceptance,

    /// <summary>A member or coach crossed a hard floor: the target is refused.</summary>
    Blocked,

    /// <summary>A clinician crossed a hard floor: the target applies and the member is told.</summary>
    ApplyAndNotifyMember,
}

/// <summary>The limits a proposed target crosses.</summary>
public sealed record GuardrailReport(IReadOnlyList<SoftLimit> SoftLimits, IReadOnlyList<HardLimit> HardLimits)
{
    public bool IsClear => SoftLimits.Count == 0 && HardLimits.Count == 0;
}

/// <summary>
/// Checks a target against the soft limits and hard floor, and decides what happens by who is setting it (BRD §6.2).
/// The decision table is the BRD's initial position, pending clinical and legal review.
/// </summary>
public static class TargetGuardrails
{
    /// <summary>Lists every limit the target crosses, for a member of the given weight and height.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The weight or height isn't positive.</exception>
    /// <exception cref="ArgumentException">The settings are invalid (<see cref="GuardrailSettings.IsValid"/>).</exception>
    public static GuardrailReport Evaluate(
        ProposedTarget target, decimal currentWeightKg, decimal heightCm, GuardrailSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(currentWeightKg);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heightCm);
        settings ??= GuardrailSettings.Default;
        settings.ThrowIfInvalid();
        var soft = new List<SoftLimit>();
        var hard = new List<HardLimit>();

        if (target.KgPerWeek < 0 && -target.KgPerWeek > currentWeightKg * settings.MaxLossFractionPerWeek)
        {
            soft.Add(SoftLimit.LossRateAboveMaximum);
        }

        if (target.Kcal < settings.SoftCalorieFloorKcal)
        {
            soft.Add(SoftLimit.CaloriesBelowSoftFloor);
        }

        if (target.ProteinGrams < settings.ProteinGramsPerKg * currentWeightKg)
        {
            soft.Add(SoftLimit.ProteinBelowDefault);
        }

        if (target.Kcal < settings.HardCalorieFloorKcal)
        {
            hard.Add(HardLimit.CaloriesBelowHardFloor);
        }

        if (target.GoalWeightKg is { } goal && EnergyExpenditure.BodyMassIndex(goal, heightCm) < settings.MinimumGoalBmi)
        {
            hard.Add(HardLimit.GoalBmiBelowMinimum);
        }

        return new GuardrailReport(soft, hard);
    }

    /// <summary>Decides what happens to a target with this report, set by <paramref name="setter"/>.</summary>
    /// <param name="report">The limits the target crosses.</param>
    /// <param name="setter">Who is setting the target.</param>
    /// <param name="memberAcknowledged">For a member: whether they acknowledged the soft-limit warning.</param>
    /// <param name="memberOverridesDisabled">
    /// True when the member is flagged by eating-disorder screening. Overrides past the soft limits are then disabled
    /// (BRD §6.2), so a member's or coach's target past a soft limit is blocked. A clinician is unaffected.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">The setter isn't defined.</exception>
    public static TargetDecision Decide(
        GuardrailReport report, TargetSetter setter, bool memberAcknowledged = false, bool memberOverridesDisabled = false)
    {
        if (!Enum.IsDefined(setter))
        {
            throw new ArgumentOutOfRangeException(nameof(setter), setter, null);
        }

        ArgumentNullException.ThrowIfNull(report);
        if (report.HardLimits.Count > 0)
        {
            return setter == TargetSetter.Clinician ? TargetDecision.ApplyAndNotifyMember : TargetDecision.Blocked;
        }

        if (report.SoftLimits.Count == 0)
        {
            return TargetDecision.Apply;
        }

        return setter switch
        {
            TargetSetter.Clinician => TargetDecision.Apply,
            _ when memberOverridesDisabled => TargetDecision.Blocked,
            TargetSetter.Member => memberAcknowledged ? TargetDecision.ApplyAsOverride : TargetDecision.NeedsAcknowledgement,
            _ => TargetDecision.PendingMemberAcceptance,
        };
    }
}
