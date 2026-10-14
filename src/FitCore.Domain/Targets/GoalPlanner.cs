namespace FitCore.Domain.Targets;

/// <summary>How the member sets the pace of a goal: a weekly rate, or a date to reach it by (BRD §6.2).</summary>
public abstract record GoalPace
{
    private GoalPace()
    {
    }

    /// <summary>A weekly rate of change, as a positive number of kilograms.</summary>
    public sealed record Rate(decimal KgPerWeek) : GoalPace;

    /// <summary>A date to reach the goal weight by.</summary>
    public sealed record ByDate(DateOnly TargetDate) : GoalPace;
}

public enum GoalDirection
{
    Lose,
    Maintain,
    Gain,
}

public enum GoalPlanError
{
    /// <summary>A weight or rate is zero or negative.</summary>
    InvalidInput,

    /// <summary>The goal date is today or earlier.</summary>
    TargetDateNotInFuture,
}

/// <summary>A goal with both its rate and its date known.</summary>
/// <param name="Direction">Lose, maintain or gain.</param>
/// <param name="KgPerWeek">Signed: negative for loss.</param>
/// <param name="TargetDate">When the goal is reached; null when maintaining.</param>
/// <param name="ExceedsSafeLossRate">True when the loss is faster than the soft limit. The plan is never changed silently.</param>
/// <param name="DateAtSafeRate">When the rate is too fast: the date the goal would be reached at the fastest safe rate.</param>
public sealed record GoalPlan(
    GoalDirection Direction,
    decimal KgPerWeek,
    DateOnly? TargetDate,
    bool ExceedsSafeLossRate,
    DateOnly? DateAtSafeRate);

/// <summary>Derives a goal's date from its rate, or its rate from its date, and flags a loss rate past the soft limit.</summary>
public static class GoalPlanner
{
    /// <summary>Goal weights this close to the current weight count as maintenance.</summary>
    public const decimal MaintainToleranceKg = 0.1m;

    /// <summary>
    /// Plans a goal. When the loss rate is too fast, the plan says so and offers the date at the safe rate;
    /// the member then picks a later date, the capped rate or an override.
    /// </summary>
    public static Result<GoalPlan, GoalPlanError> Plan(
        decimal currentWeightKg, decimal goalWeightKg, DateOnly today, GoalPace pace, GuardrailSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(pace);
        settings ??= GuardrailSettings.Default;
        if (currentWeightKg <= 0 || goalWeightKg <= 0 || settings.MaxLossFractionPerWeek <= 0)
        {
            return GoalPlanError.InvalidInput;
        }

        var change = goalWeightKg - currentWeightKg;
        if (Math.Abs(change) < MaintainToleranceKg)
        {
            return new GoalPlan(GoalDirection.Maintain, 0, null, false, null);
        }

        var direction = change < 0 ? GoalDirection.Lose : GoalDirection.Gain;
        decimal rate;
        DateOnly targetDate;
        switch (pace)
        {
            case GoalPace.Rate r:
                if (r.KgPerWeek <= 0)
                {
                    return GoalPlanError.InvalidInput;
                }

                rate = r.KgPerWeek;
                targetDate = AddWeeks(today, Math.Abs(change) / rate);
                break;
            case GoalPace.ByDate d:
                var days = d.TargetDate.DayNumber - today.DayNumber;
                if (days <= 0)
                {
                    return GoalPlanError.TargetDateNotInFuture;
                }

                rate = Math.Abs(change) / (days / 7m);
                targetDate = d.TargetDate;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(pace), pace, null);
        }

        var maxLoss = currentWeightKg * settings.MaxLossFractionPerWeek;
        var tooFast = direction == GoalDirection.Lose && rate > maxLoss;
        return new GoalPlan(
            direction,
            direction == GoalDirection.Lose ? -rate : rate,
            targetDate,
            tooFast,
            tooFast ? AddWeeks(today, Math.Abs(change) / maxLoss) : null);
    }

    private static DateOnly AddWeeks(DateOnly date, decimal weeks) => date.AddDays((int)Math.Ceiling(weeks * 7));
}
