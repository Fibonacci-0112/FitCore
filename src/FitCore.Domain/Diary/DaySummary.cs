using FitCore.Domain.Costs;
using FitCore.Domain.Nutrition;

namespace FitCore.Domain.Diary;

/// <summary>
/// A day's totals, as the Today screen shows them. Unknown nutrients and unpriced entries are counted,
/// so totals read as a lower bound with the number of incomplete items (BRD §6.1).
/// </summary>
public sealed record DaySummary(int EntryCount, NutrientTotals Nutrients, CostTotal EstimatedCost)
{
    public static DaySummary Of(IReadOnlyCollection<EntrySnapshot> entries, CurrencyCode currency)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return new DaySummary(
            entries.Count,
            NutrientTotals.Sum(entries.Select(e => e.Nutrients)),
            Costs.EstimatedCost.Sum(entries.Select(e => e.Cost), currency));
    }
}
