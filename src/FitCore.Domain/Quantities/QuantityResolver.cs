namespace FitCore.Domain.Quantities;

/// <summary>
/// Converts quantities between grams, millilitres and servings (BRD §6.1, NFR-REL-7).
/// Mass↔volume needs the food's density. Anything that can't be converted is rejected, never guessed.
/// </summary>
public static class QuantityResolver
{
    /// <summary>Expresses a quantity in grams and millilitres, using the food's density only when it's known.</summary>
    public static Result<Measures, QuantityError> ToMeasures(FoodMeasures food, Quantity quantity)
    {
        ArgumentNullException.ThrowIfNull(food);
        if (quantity.Amount <= 0)
        {
            return QuantityError.InvalidAmount;
        }

        decimal? grams = null;
        decimal? milliliters = null;
        Guid? servingId = null;
        decimal? servingCount = null;

        switch (quantity.Unit)
        {
            case QuantityUnit.Grams:
                grams = quantity.Amount;
                break;
            case QuantityUnit.Milliliters:
                milliliters = quantity.Amount;
                break;
            default:
                var serving = food.Servings.FirstOrDefault(s => s.Id == quantity.ServingId);
                if (serving is null)
                {
                    return QuantityError.UnknownServing;
                }

                servingId = serving.Id;
                servingCount = quantity.Amount;
                grams = serving.Grams > 0 ? serving.Grams * quantity.Amount : null;
                milliliters = serving.Milliliters > 0 ? serving.Milliliters * quantity.Amount : null;
                break;
        }

        if (food.DensityGramsPerMilliliter is > 0 and var density)
        {
            grams ??= milliliters * density;
            milliliters ??= grams / density;
        }

        return new Measures(grams, milliliters, servingId, servingCount);
    }

    /// <summary>Resolves a quantity into the food's nutrient basis.</summary>
    public static Result<ResolvedQuantity, QuantityError> Resolve(FoodMeasures food, Quantity quantity)
    {
        var measured = ToMeasures(food, quantity);
        if (!measured.IsSuccess)
        {
            return measured.Error;
        }

        var m = measured.Value;
        switch (food.Basis)
        {
            case NutrientBasis.Per100Grams:
                return m.Grams is { } g ? new ResolvedQuantity(m, g / 100) : QuantityError.Unconvertible;
            case NutrientBasis.Per100Milliliters:
                return m.Milliliters is { } ml ? new ResolvedQuantity(m, ml / 100) : QuantityError.Unconvertible;
            default:
                var basis = food.Servings.FirstOrDefault(s => s.Id == food.BasisServingId);
                if (basis is null)
                {
                    return QuantityError.MissingBasisServing;
                }

                if (m.ServingId == basis.Id)
                {
                    return new ResolvedQuantity(m, m.ServingCount!.Value);
                }

                if (basis.Grams > 0 && m.Grams is { } grams)
                {
                    return new ResolvedQuantity(m, grams / basis.Grams.Value);
                }

                if (basis.Milliliters > 0 && m.Milliliters is { } milliliters)
                {
                    return new ResolvedQuantity(m, milliliters / basis.Milliliters.Value);
                }

                return QuantityError.Unconvertible;
        }
    }

    /// <summary>
    /// The ratio of quantity <paramref name="a"/> to quantity <paramref name="b"/> of the same food,
    /// compared in grams, then millilitres, then the same serving.
    /// </summary>
    public static Result<decimal, QuantityError> Ratio(FoodMeasures food, Quantity a, Quantity b)
    {
        var ma = ToMeasures(food, a);
        if (!ma.IsSuccess)
        {
            return ma.Error;
        }

        var mb = ToMeasures(food, b);
        if (!mb.IsSuccess)
        {
            return mb.Error;
        }

        var (x, y) = (ma.Value, mb.Value);
        if (x.Grams is { } gx && y.Grams is { } gy)
        {
            return gx / gy;
        }

        if (x.Milliliters is { } mx && y.Milliliters is { } my)
        {
            return mx / my;
        }

        if (x.ServingId is { } sx && sx == y.ServingId)
        {
            return x.ServingCount!.Value / y.ServingCount!.Value;
        }

        return QuantityError.Unconvertible;
    }
}
