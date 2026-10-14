using FitCore.Domain.Targets;

namespace FitCore.Domain.Tests;

public sealed class TargetsTests
{
    private static readonly DateOnly Today = new(2026, 10, 14);

    [Theory]
    [InlineData(BiologicalSex.Male, 80, 180, 30, 1780)]
    [InlineData(BiologicalSex.Female, 60, 165, 40, 1270.25)]
    public void Resting_energy_follows_Mifflin_St_Jeor(BiologicalSex sex, double weightKg, double heightCm, int age, double kcal)
    {
        Assert.Equal((decimal)kcal, EnergyExpenditure.RestingKcal(sex, (decimal)weightKg, (decimal)heightCm, age));
    }

    [Theory]
    [InlineData(ActivityLevel.Sedentary, 1.2)]
    [InlineData(ActivityLevel.Light, 1.375)]
    [InlineData(ActivityLevel.Moderate, 1.55)]
    [InlineData(ActivityLevel.Active, 1.725)]
    [InlineData(ActivityLevel.VeryActive, 1.9)]
    public void Maintenance_is_resting_energy_times_the_activity_factor(ActivityLevel level, double factor)
    {
        Assert.Equal(1000 * (decimal)factor, EnergyExpenditure.TotalKcal(1000, level));
    }

    [Fact]
    public void An_unknown_activity_level_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EnergyExpenditure.ActivityFactor((ActivityLevel)99));
    }

    [Fact]
    public void Estimates_calories_and_macros_from_the_weekly_rate()
    {
        var estimate = TargetEstimator.Estimate(BiologicalSex.Male, 30, 180, 80, ActivityLevel.Moderate, -0.5m);

        Assert.Equal(new TargetEstimate(1780, 2759, 2209, 128, 61, 287, false, false), estimate);
    }

    [Fact]
    public void The_estimate_stops_at_the_soft_calorie_floor_and_says_so()
    {
        var estimate = TargetEstimator.Estimate(BiologicalSex.Female, 40, 165, 60, ActivityLevel.Sedentary, -1m);

        Assert.Equal(new TargetEstimate(1270, 1524, 1200, 96, 33, 130, true, false), estimate);
    }

    [Fact]
    public void The_floor_is_configurable()
    {
        var estimate = TargetEstimator.Estimate(BiologicalSex.Female, 40, 165, 60, ActivityLevel.Sedentary, -1m, new GuardrailSettings(SoftCalorieFloorKcal: 1400));

        Assert.Equal(1400m, estimate.Kcal);
    }

    [Fact]
    public void When_protein_and_fat_exceed_the_calories_carbohydrate_is_zero_and_flagged()
    {
        var estimate = TargetEstimator.Estimate(BiologicalSex.Female, 60, 150, 150, ActivityLevel.Sedentary, -1.5m);

        Assert.Equal(0m, estimate.CarbohydrateGrams);
        Assert.True(estimate.MacrosExceedCalories);
    }

    public static TheoryData<string, decimal, decimal, GoalPace, GoalPlan> Plans => new()
    {
        { "a safe weekly rate gives the date", 80, 70, new GoalPace.Rate(0.5m), new GoalPlan(GoalDirection.Lose, -0.5m, new DateOnly(2027, 3, 3), false, null) },
        { "a fast rate is flagged with the date at the safe rate", 80, 70, new GoalPace.Rate(1m), new GoalPlan(GoalDirection.Lose, -1m, new DateOnly(2026, 12, 23), true, new DateOnly(2027, 1, 10)) },
        { "a goal date gives the rate", 80, 70, new GoalPace.ByDate(new DateOnly(2027, 3, 3)), new GoalPlan(GoalDirection.Lose, -0.5m, new DateOnly(2027, 3, 3), false, null) },
        { "an aggressive goal date is flagged", 80, 70, new GoalPace.ByDate(new DateOnly(2026, 12, 23)), new GoalPlan(GoalDirection.Lose, -1m, new DateOnly(2026, 12, 23), true, new DateOnly(2027, 1, 10)) },
        { "a gain has a positive rate", 60, 65, new GoalPace.Rate(0.25m), new GoalPlan(GoalDirection.Gain, 0.25m, new DateOnly(2027, 3, 3), false, null) },
        { "a goal within 0.1 kg is maintenance", 80, 80.05m, new GoalPace.Rate(0.5m), new GoalPlan(GoalDirection.Maintain, 0, null, false, null) },
    };

    [Theory]
    [MemberData(nameof(Plans))]
    public void Plans_a_goal_from_a_rate_or_a_date(string _, decimal current, decimal goal, GoalPace pace, GoalPlan plan)
    {
        Assert.Equal(plan, GoalPlanner.Plan(current, goal, Today, pace).Value);
    }

    public static TheoryData<string, decimal, decimal, GoalPace, GoalPlanError> BadPlans => new()
    {
        { "a goal date of today", 80, 70, new GoalPace.ByDate(Today), GoalPlanError.TargetDateNotInFuture },
        { "a goal date in the past", 80, 70, new GoalPace.ByDate(Today.AddDays(-1)), GoalPlanError.TargetDateNotInFuture },
        { "a zero rate", 80, 70, new GoalPace.Rate(0), GoalPlanError.InvalidInput },
        { "a zero goal weight", 80, 0, new GoalPace.Rate(0.5m), GoalPlanError.InvalidInput },
        { "a zero current weight", 0, 70, new GoalPace.Rate(0.5m), GoalPlanError.InvalidInput },
    };

    [Theory]
    [MemberData(nameof(BadPlans))]
    public void Rejects_an_impossible_goal(string _, decimal current, decimal goal, GoalPace pace, GoalPlanError error)
    {
        Assert.Equal(error, GoalPlanner.Plan(current, goal, Today, pace).Error);
    }

    public static TheoryData<string, ProposedTarget, SoftLimit[], HardLimit[]> Reports => new()
    {
        { "a sensible target", new ProposedTarget(2000, 130, -0.5m, 70), [], [] },
        { "loss faster than 1% a week", new ProposedTarget(2000, 130, -1m, 70), [SoftLimit.LossRateAboveMaximum], [] },
        { "a fast gain isn't a loss limit", new ProposedTarget(2800, 130, 1m, 90), [], [] },
        { "calories under 1,200", new ProposedTarget(1100, 130), [SoftLimit.CaloriesBelowSoftFloor], [] },
        { "protein under 1.6 g/kg", new ProposedTarget(2000, 100), [SoftLimit.ProteinBelowDefault], [] },
        { "calories under 800", new ProposedTarget(700, 130), [SoftLimit.CaloriesBelowSoftFloor], [HardLimit.CaloriesBelowHardFloor] },
        { "a goal BMI under 18.5", new ProposedTarget(2000, 130, -0.5m, 55), [], [HardLimit.GoalBmiBelowMinimum] },
    };

    [Theory]
    [MemberData(nameof(Reports))]
    public void Lists_every_limit_a_target_crosses(string _, ProposedTarget target, SoftLimit[] soft, HardLimit[] hard)
    {
        var report = TargetGuardrails.Evaluate(target, currentWeightKg: 80, heightCm: 180);

        Assert.Equal(soft, report.SoftLimits);
        Assert.Equal(hard, report.HardLimits);
        Assert.Equal(soft.Length == 0 && hard.Length == 0, report.IsClear);
    }

    public static TheoryData<string, ProposedTarget, bool> Thresholds => new()
    {
        { "exactly 1,200 kcal is allowed", new ProposedTarget(1200, 128), true },
        { "just under 1,200 kcal is a soft limit", new ProposedTarget(1199.9m, 128), false },
        { "exactly 1.6 g/kg of protein is allowed", new ProposedTarget(2000, 128), true },
        { "just under 1.6 g/kg of protein is a soft limit", new ProposedTarget(2000, 127.9m), false },
        { "exactly 1% a week is allowed", new ProposedTarget(2000, 128, -0.8m), true },
        { "just over 1% a week is a soft limit", new ProposedTarget(2000, 128, -0.81m), false },
    };

    [Theory]
    [MemberData(nameof(Thresholds))]
    public void Soft_limits_apply_only_past_their_value(string _, ProposedTarget target, bool clear)
    {
        Assert.Equal(clear, TargetGuardrails.Evaluate(target, currentWeightKg: 80, heightCm: 180).IsClear);
    }

    [Theory]
    [InlineData(800, false)]
    [InlineData(799.9, true)]
    public void The_calorie_hard_floor_applies_only_below_800(double kcal, bool blocked)
    {
        var report = TargetGuardrails.Evaluate(new ProposedTarget((decimal)kcal), currentWeightKg: 80, heightCm: 180);

        Assert.Equal(blocked, report.HardLimits.Contains(HardLimit.CaloriesBelowHardFloor));
    }

    [Theory]
    [InlineData(59.94, false)]
    [InlineData(59.93, true)]
    public void The_goal_bmi_floor_applies_only_below_18_5(double goalKg, bool blocked)
    {
        // At 180 cm, a BMI of 18.5 is 59.94 kg.
        var report = TargetGuardrails.Evaluate(new ProposedTarget(2000, GoalWeightKg: (decimal)goalKg), currentWeightKg: 80, heightCm: 180);

        Assert.Equal(blocked, report.HardLimits.Contains(HardLimit.GoalBmiBelowMinimum));
    }

    private static readonly GuardrailReport Clear = new([], []);
    private static readonly GuardrailReport Soft = new([SoftLimit.CaloriesBelowSoftFloor], []);
    private static readonly GuardrailReport Hard = new([SoftLimit.CaloriesBelowSoftFloor], [HardLimit.CaloriesBelowHardFloor]);

    public static TheoryData<string, GuardrailReport, TargetSetter, bool, TargetDecision> Decisions => new()
    {
        { "member, clear", Clear, TargetSetter.Member, false, TargetDecision.Apply },
        { "member, soft, not acknowledged", Soft, TargetSetter.Member, false, TargetDecision.NeedsAcknowledgement },
        { "member, soft, acknowledged", Soft, TargetSetter.Member, true, TargetDecision.ApplyAsOverride },
        { "member, hard, even acknowledged", Hard, TargetSetter.Member, true, TargetDecision.Blocked },
        { "coach, clear", Clear, TargetSetter.Coach, false, TargetDecision.Apply },
        { "coach, soft", Soft, TargetSetter.Coach, false, TargetDecision.PendingMemberAcceptance },
        { "coach, hard", Hard, TargetSetter.Coach, false, TargetDecision.Blocked },
        { "clinician, soft", Soft, TargetSetter.Clinician, false, TargetDecision.Apply },
        { "clinician, hard", Hard, TargetSetter.Clinician, false, TargetDecision.ApplyAndNotifyMember },
    };

    [Theory]
    [MemberData(nameof(Decisions))]
    public void Decides_by_who_sets_the_target(string _, GuardrailReport report, TargetSetter setter, bool acknowledged, TargetDecision decision)
    {
        Assert.Equal(decision, TargetGuardrails.Decide(report, setter, acknowledged));
    }

    [Fact]
    public void Body_mass_index_is_kilograms_over_metres_squared()
    {
        Assert.Equal(25m, EnergyExpenditure.BodyMassIndex(81, 180));
    }
}
