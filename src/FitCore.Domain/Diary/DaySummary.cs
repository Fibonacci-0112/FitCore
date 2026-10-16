using FitCore.Domain.Costs;
using FitCore.Domain.Nutrition;

namespace FitCore.Domain.Diary;

/// <summary>
/// A day's totals, as the Today screen shows them. Unknown nutrients and unpriced entries are counted,
/// so totals read as a lower bound with the number of incomplete items (BRD §6.1).
/// </summary>
/// <param name="EntryCount">How many entries were logged.</param>
/// <param name="Nutrients">The totals of what was logged.</param>
/// <param name="EstimatedCost">The estimated cost of what was logged.</param>
/// <param name="IsComplete">
/// Whether the member marked the day complete. Until they do, the totals cover only what was logged, and the day's
/// intake is unknown, never zero: an empty day that isn't complete means nothing was recorded (BRD §6.1).
/// Only complete days feed adaptive targets.
/// </param>
public sealed record DaySummary(int EntryCount, NutrientTotals Nutrients, CostTotal EstimatedCost, bool IsComplete)
{
    /// <summary>Totals a day's entries.</summary>
    /// <param name="entries">The day's entries.</param>
    /// <param name="currency">The profile currency.</param>
    /// <param name="markedComplete">Whether the member marked the day complete.</param>
    public static DaySummary Of(IReadOnlyCollection<EntrySnapshot> entries, CurrencyCode currency, bool markedComplete)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return new DaySummary(
            entries.Count,
            NutrientTotals.Sum(entries.Select(e => e.Nutrients)),
            Costs.EstimatedCost.Sum(entries.Select(e => e.Cost), currency),
            markedComplete);
    }

    /// <summary>
    /// The day's known total energy intake: the logged total when the day is complete and every entry knows its
    /// calories; otherwise null, because the intake is unknown.
    /// </summary>
    public decimal? KnownEnergyKcal =>
        IsComplete && Nutrients[Nutrition.Nutrient.EnergyKcal] is { IsComplete: true } total ? total.Known : null;
}
