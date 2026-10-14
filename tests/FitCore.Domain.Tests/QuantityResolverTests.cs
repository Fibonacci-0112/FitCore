using FitCore.Domain.Quantities;

namespace FitCore.Domain.Tests;

public sealed class QuantityResolverTests
{
    public static TheoryData<string, FoodMeasures, Quantity, decimal> Resolvable => new()
    {
        { "grams on a per-100 g food", Foods.Bread, Quantity.Grams(150), 1.5m },
        { "a serving with grams on a per-100 g food", Foods.Bread, Quantity.Servings(2, Foods.SliceId), 0.56m },
        { "millilitres on a per-100 ml food", Foods.Milk, Quantity.Milliliters(250), 2.5m },
        { "grams on a per-100 ml food with a density", Foods.Milk, Quantity.Grams(103), 1m },
        { "a serving with millilitres on a per-100 ml food", Foods.Milk, Quantity.Servings(1, Foods.CupId), 2.4m },
        { "the basis serving itself", Foods.ProteinBar, Quantity.Servings(1.5m, Foods.BarId), 1.5m },
        { "grams on a per-serving food", Foods.ProteinBar, Quantity.Grams(30), 0.5m },
        { "a count of a weightless serving", Foods.Egg, Quantity.Servings(3, Foods.PieceId), 3m },
        { "millilitres on a per-serving food", Foods.Syrup, Quantity.Milliliters(45), 3m },
    };

    [Theory]
    [MemberData(nameof(Resolvable))]
    public void Resolves_into_the_nutrient_basis(string _, FoodMeasures food, Quantity quantity, decimal multiplier)
    {
        var resolved = QuantityResolver.Resolve(food, quantity);

        Assert.True(resolved.IsSuccess, resolved.ToString());
        Assert.Equal(multiplier, resolved.Value.BasisMultiplier);
    }

    public static TheoryData<string, FoodMeasures, Quantity, QuantityError> Rejected => new()
    {
        { "zero", Foods.Bread, Quantity.Grams(0), QuantityError.InvalidAmount },
        { "a negative amount", Foods.Bread, Quantity.Grams(-1), QuantityError.InvalidAmount },
        { "another food's serving", Foods.Bread, Quantity.Servings(1, Foods.CupId), QuantityError.UnknownServing },
        { "millilitres of a food with no density", Foods.Bread, Quantity.Milliliters(100), QuantityError.Unconvertible },
        { "grams of a liquid with no density", Foods.Juice, Quantity.Grams(100), QuantityError.Unconvertible },
        { "grams of a weightless serving", Foods.Egg, Quantity.Grams(50), QuantityError.Unconvertible },
        { "grams of a per-serving food measured in millilitres", Foods.Syrup, Quantity.Grams(15), QuantityError.Unconvertible },
        { "a missing basis serving", Foods.Broken, Quantity.Grams(10), QuantityError.MissingBasisServing },
    };

    [Theory]
    [MemberData(nameof(Rejected))]
    public void Rejects_what_it_cannot_convert_instead_of_guessing(string _, FoodMeasures food, Quantity quantity, QuantityError error)
    {
        var resolved = QuantityResolver.Resolve(food, quantity);

        Assert.False(resolved.IsSuccess);
        Assert.Equal(error, resolved.Error);
    }

    [Fact]
    public void Converts_between_mass_and_volume_only_with_a_density()
    {
        var milk = QuantityResolver.ToMeasures(Foods.Milk, Quantity.Milliliters(100)).Value;
        var juice = QuantityResolver.ToMeasures(Foods.Juice, Quantity.Milliliters(100)).Value;

        Assert.Equal(103m, milk.Grams);
        Assert.Null(juice.Grams);
    }

    [Fact]
    public void Records_the_serving_and_its_count()
    {
        var measures = QuantityResolver.ToMeasures(Foods.Bread, Quantity.Servings(2, Foods.SliceId)).Value;

        Assert.Equal(new Measures(56, null, Foods.SliceId, 2), measures);
    }

    public static TheoryData<string, FoodMeasures, Quantity, Quantity, decimal> Ratios => new()
    {
        { "grams to grams", Foods.Bread, Quantity.Grams(50), Quantity.Grams(500), 0.1m },
        { "a serving to grams", Foods.Bread, Quantity.Servings(1, Foods.SliceId), Quantity.Grams(560), 0.05m },
        { "millilitres to a serving", Foods.Juice, Quantity.Milliliters(250), Quantity.Milliliters(1000), 0.25m },
        { "grams to millilitres through density", Foods.Milk, Quantity.Grams(103), Quantity.Milliliters(1000), 0.1m },
        { "the same weightless serving", Foods.Egg, Quantity.Servings(2, Foods.PieceId), Quantity.Servings(12, Foods.PieceId), 2m / 12 },
    };

    [Theory]
    [MemberData(nameof(Ratios))]
    public void Compares_two_quantities_in_a_shared_unit(string _, FoodMeasures food, Quantity a, Quantity b, decimal ratio)
    {
        Assert.Equal(ratio, QuantityResolver.Ratio(food, a, b).Value);
    }

    public static TheoryData<string, FoodMeasures, Quantity, Quantity, QuantityError> Incomparable => new()
    {
        { "a weightless serving to grams", Foods.Egg, Quantity.Servings(2, Foods.PieceId), Quantity.Grams(50), QuantityError.Unconvertible },
        { "another food's serving", Foods.Egg, Quantity.Servings(2, Foods.PieceId), Quantity.Servings(1, Foods.OtherId), QuantityError.UnknownServing },
        { "grams to millilitres with no density", Foods.Bread, Quantity.Grams(10), Quantity.Milliliters(10), QuantityError.Unconvertible },
        { "an invalid first amount", Foods.Bread, Quantity.Grams(0), Quantity.Grams(10), QuantityError.InvalidAmount },
        { "an invalid second amount", Foods.Bread, Quantity.Grams(10), Quantity.Grams(-5), QuantityError.InvalidAmount },
    };

    [Theory]
    [MemberData(nameof(Incomparable))]
    public void Refuses_to_compare_quantities_with_no_shared_unit(string _, FoodMeasures food, Quantity a, Quantity b, QuantityError error)
    {
        Assert.Equal(error, QuantityResolver.Ratio(food, a, b).Error);
    }
}
