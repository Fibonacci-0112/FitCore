using FitCore.Domain.Quantities;

namespace FitCore.Domain.Costs;

/// <summary>A member's price for a food: what a package of a given size cost from a given date.</summary>
/// <param name="Id">The price record.</param>
/// <param name="FoodId">The food it prices.</param>
/// <param name="Package">The package size the price is for.</param>
/// <param name="Price">The price of one package.</param>
/// <param name="EffectiveDate">The first local date the price applies to.</param>
/// <param name="CreatedAt">Breaks ties between records with the same effective date: the newest wins.</param>
public sealed record PriceRecord(Guid Id, Guid FoodId, Quantity Package, Money Price, DateOnly EffectiveDate, DateTimeOffset CreatedAt);

/// <summary>Why no price applies to an entry. The entry is still saved, as unpriced.</summary>
public enum PriceUnavailable
{
    /// <summary>The member has no price for this food.</summary>
    NoPrice,

    /// <summary>Every price for this food starts after the diary date.</summary>
    NotYetEffective,

    /// <summary>The applicable prices are in another currency. Early releases don't convert currencies.</summary>
    CurrencyMismatch,
}
