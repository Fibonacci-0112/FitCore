using FitCore.Domain.Costs;
using FitCore.Domain.Nutrition;
using FitCore.Domain.Quantities;

namespace FitCore.Domain.Diary;

/// <summary>Why a diary entry has, or lacks, an estimated cost.</summary>
public enum CostStatus
{
    Priced,
    NoPrice,
    NotYetEffective,
    CurrencyMismatch,

    /// <summary>A price applies, but its package size can't be compared with the amount eaten.</summary>
    Unconvertible,
}

/// <summary>
/// What a diary entry stores at log time: the amount eaten, its nutrients and its estimated cost.
/// Later edits to the food or its prices never change it (BRD §6.1, NFR-REL-4).
/// </summary>
public sealed record EntrySnapshot(
    Measures Measures,
    Nutrients Nutrients,
    CostStatus CostStatus,
    Money? Cost,
    Guid? PriceId)
{
    /// <summary>
    /// Takes the snapshot for eating <paramref name="quantity"/> of a food on <paramref name="diaryDate"/>.
    /// The quantity must convert into the food's nutrient basis, or the entry is rejected.
    /// A missing or unusable price never rejects the entry; it's saved as unpriced.
    /// </summary>
    /// <param name="foodId">The food eaten.</param>
    /// <param name="food">How the food can be measured.</param>
    /// <param name="nutrients">The food's nutrients per basis unit.</param>
    /// <param name="quantity">The amount eaten.</param>
    /// <param name="prices">The member's prices. Prices for other foods are ignored.</param>
    /// <param name="diaryDate">The member's local date for the entry.</param>
    /// <param name="currency">The profile currency. Prices in other currencies aren't converted.</param>
    public static Result<EntrySnapshot, QuantityError> Take(
        Guid foodId,
        FoodMeasures food,
        Nutrients nutrients,
        Quantity quantity,
        IEnumerable<PriceRecord> prices,
        DateOnly diaryDate,
        CurrencyCode currency)
    {
        ArgumentNullException.ThrowIfNull(nutrients);
        var resolved = QuantityResolver.Resolve(food, quantity);
        if (!resolved.IsSuccess)
        {
            return resolved.Error;
        }

        var measures = resolved.Value.Measures;
        var eaten = nutrients.Scale(resolved.Value.BasisMultiplier);

        var price = EstimatedCost.SelectPrice(prices, foodId, diaryDate, currency);
        if (!price.IsSuccess)
        {
            var status = price.Error switch
            {
                PriceUnavailable.NoPrice => CostStatus.NoPrice,
                PriceUnavailable.NotYetEffective => CostStatus.NotYetEffective,
                _ => CostStatus.CurrencyMismatch,
            };
            return new EntrySnapshot(measures, eaten, status, null, null);
        }

        var cost = EstimatedCost.Of(food, quantity, price.Value);
        return cost.IsSuccess
            ? new EntrySnapshot(measures, eaten, CostStatus.Priced, cost.Value, price.Value.Id)
            : new EntrySnapshot(measures, eaten, CostStatus.Unconvertible, null, null);
    }
}
