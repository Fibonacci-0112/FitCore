using FitCore.Domain.Quantities;

namespace FitCore.Domain.Costs;

/// <summary>
/// Estimated food cost: the consumed quantity times the unit price from the member's applicable price record.
/// It is consumption cost, always shown as an estimate and never as spend (BRD §6.1).
/// </summary>
public static class EstimatedCost
{
    /// <summary>
    /// Picks the price that applies on <paramref name="diaryDate"/>: same food, same currency,
    /// latest effective date on or before the diary date, newest record on a tie.
    /// </summary>
    public static Result<PriceRecord, PriceUnavailable> SelectPrice(
        IEnumerable<PriceRecord> prices, Guid foodId, DateOnly diaryDate, CurrencyCode currency)
    {
        ArgumentNullException.ThrowIfNull(prices);
        var forFood = prices.Where(p => p.FoodId == foodId).ToList();
        if (forFood.Count == 0)
        {
            return PriceUnavailable.NoPrice;
        }

        var effective = forFood.Where(p => p.EffectiveDate <= diaryDate).ToList();
        if (effective.Count == 0)
        {
            return PriceUnavailable.NotYetEffective;
        }

        var best = effective
            .Where(p => p.Price.Currency == currency)
            .OrderByDescending(p => p.EffectiveDate)
            .ThenByDescending(p => p.CreatedAt)
            .FirstOrDefault();
        return best is null ? PriceUnavailable.CurrencyMismatch : best;
    }

    /// <summary>The cost of eating <paramref name="consumed"/> of a food bought at <paramref name="price"/>, rounded half away from zero.</summary>
    public static Result<Money, QuantityError> Of(FoodMeasures food, Quantity consumed, PriceRecord price)
    {
        ArgumentNullException.ThrowIfNull(price);
        var ratio = QuantityResolver.Ratio(food, consumed, price.Package);
        if (!ratio.IsSuccess)
        {
            return ratio.Error;
        }

        var minor = decimal.Round(price.Price.MinorUnits * ratio.Value, MidpointRounding.AwayFromZero);
        return new Money((long)minor, price.Price.Currency);
    }

    /// <summary>Adds entry costs in <paramref name="currency"/>. Unpriced entries and other currencies are counted, never added as zero.</summary>
    public static CostTotal Sum(IEnumerable<Money?> costs, CurrencyCode currency)
    {
        ArgumentNullException.ThrowIfNull(costs);
        long total = 0;
        var unpriced = 0;
        foreach (var cost in costs)
        {
            if (cost is { } money && money.Currency == currency)
            {
                total += money.MinorUnits;
            }
            else
            {
                unpriced++;
            }
        }

        return new CostTotal(new Money(total, currency), unpriced);
    }
}

/// <summary>The estimated cost of several entries.</summary>
/// <param name="Known">The sum of the priced entries. A lower bound when <paramref name="UnpricedCount"/> is above zero.</param>
/// <param name="UnpricedCount">How many entries have no cost in this currency.</param>
public readonly record struct CostTotal(Money Known, int UnpricedCount)
{
    public bool IsComplete => UnpricedCount == 0;
}
