namespace FitCore.Domain.Nutrition;

/// <summary>The total of one nutrient over several entries.</summary>
/// <param name="Known">The sum of the known values. A lower bound when <paramref name="MissingCount"/> is above zero.</param>
/// <param name="MissingCount">How many entries don't know this nutrient.</param>
public readonly record struct NutrientTotal(decimal Known, int MissingCount)
{
    /// <summary>True when every entry knows this nutrient, so <see cref="Known"/> is the exact total.</summary>
    public bool IsComplete => MissingCount == 0;
}

/// <summary>Totals of every nutrient. Unknown values are counted, never added as zero (BRD §6.1, NFR-REL-5).</summary>
public sealed class NutrientTotals
{
    private readonly NutrientTotal[] _totals;

    private NutrientTotals(NutrientTotal[] totals) => _totals = totals;

    public NutrientTotal this[Nutrient nutrient] => _totals[(int)nutrient];

    public static NutrientTotals Sum(IEnumerable<Nutrients> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var totals = new NutrientTotal[Nutrients.All.Count];
        foreach (var entry in entries)
        {
            foreach (var nutrient in Nutrients.All)
            {
                var total = totals[(int)nutrient];
                totals[(int)nutrient] = entry[nutrient] is { } value
                    ? total with { Known = total.Known + value }
                    : total with { MissingCount = total.MissingCount + 1 };
            }
        }

        return new NutrientTotals(totals);
    }
}
