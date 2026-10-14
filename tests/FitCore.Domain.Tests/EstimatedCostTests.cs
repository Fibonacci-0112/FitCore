using FitCore.Domain.Costs;
using FitCore.Domain.Quantities;

namespace FitCore.Domain.Tests;

public sealed class EstimatedCostTests
{
    private static readonly Guid BreadId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly CurrencyCode Usd = CurrencyCode.Usd;
    private static readonly CurrencyCode Eur = CurrencyCode.Parse("EUR");
    private static readonly DateTimeOffset Created = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static PriceRecord Price(string id, decimal major, DateOnly from, CurrencyCode? currency = null, Guid? food = null, int createdMinutes = 0) =>
        new(Guid.Parse(id), food ?? BreadId, Quantity.Grams(500), Money.FromMajor(major, currency ?? Usd), from, Created.AddMinutes(createdMinutes));

    private static readonly DateOnly Oct1 = new(2026, 10, 1);
    private static readonly DateOnly Oct10 = new(2026, 10, 10);

    [Fact]
    public void Picks_the_latest_price_effective_on_or_before_the_diary_date()
    {
        PriceRecord[] prices =
        [
            Price("00000000-0000-0000-0000-00000000000a", 3.00m, Oct1),
            Price("00000000-0000-0000-0000-00000000000b", 3.50m, Oct10),
            Price("00000000-0000-0000-0000-00000000000c", 4.00m, Oct10.AddDays(1)),
        ];

        Assert.Equal(300, EstimatedCost.SelectPrice(prices, BreadId, Oct10.AddDays(-1), Usd).Value.Price.MinorUnits);
        Assert.Equal(350, EstimatedCost.SelectPrice(prices, BreadId, Oct10, Usd).Value.Price.MinorUnits);
        Assert.Equal(400, EstimatedCost.SelectPrice(prices, BreadId, Oct10.AddDays(5), Usd).Value.Price.MinorUnits);
    }

    [Fact]
    public void Breaks_a_tie_on_effective_date_with_the_newest_record()
    {
        PriceRecord[] prices =
        [
            Price("00000000-0000-0000-0000-00000000000a", 3.00m, Oct1, createdMinutes: 5),
            Price("00000000-0000-0000-0000-00000000000b", 3.20m, Oct1, createdMinutes: 10),
            Price("00000000-0000-0000-0000-00000000000c", 3.10m, Oct1, createdMinutes: 1),
        ];

        Assert.Equal(320, EstimatedCost.SelectPrice(prices, BreadId, Oct10, Usd).Value.Price.MinorUnits);
    }

    [Fact]
    public void Reports_why_no_price_applies()
    {
        var other = Price("00000000-0000-0000-0000-00000000000a", 1m, Oct1, food: Guid.NewGuid());
        var future = Price("00000000-0000-0000-0000-00000000000b", 1m, Oct10);
        var euros = Price("00000000-0000-0000-0000-00000000000c", 1m, Oct1, Eur);

        Assert.Equal(PriceUnavailable.NoPrice, EstimatedCost.SelectPrice([other], BreadId, Oct10, Usd).Error);
        Assert.Equal(PriceUnavailable.NotYetEffective, EstimatedCost.SelectPrice([future], BreadId, Oct1, Usd).Error);
        Assert.Equal(PriceUnavailable.CurrencyMismatch, EstimatedCost.SelectPrice([euros], BreadId, Oct10, Usd).Error);
    }

    [Fact]
    public void Ignores_prices_in_another_currency_when_one_in_the_profile_currency_applies()
    {
        PriceRecord[] prices =
        [
            Price("00000000-0000-0000-0000-00000000000a", 2.80m, Oct1),
            Price("00000000-0000-0000-0000-00000000000b", 9.99m, Oct10, Eur),
        ];

        Assert.Equal(280, EstimatedCost.SelectPrice(prices, BreadId, Oct10, Usd).Value.Price.MinorUnits);
    }

    public static TheoryData<string, Quantity, long> Costs => new()
    {
        { "a tenth of the package", Quantity.Grams(50), 35 },
        { "two slices of 28 g", Quantity.Servings(2, Foods.SliceId), 39 },
        { "a third, rounded to the nearest cent", Quantity.Grams(500m / 3), 116 },
        { "half, with half a cent rounded away from zero", Quantity.Grams(250), 175 },
        { "the whole package", Quantity.Grams(500), 349 },
    };

    [Theory]
    [MemberData(nameof(Costs))]
    public void Costs_the_amount_eaten_in_integer_minor_units(string _, Quantity eaten, long minor)
    {
        var price = Price("00000000-0000-0000-0000-00000000000a", 3.49m, Oct1);

        var cost = EstimatedCost.Of(Foods.Bread, eaten, price);

        Assert.Equal(new Money(minor, Usd), cost.Value);
    }

    [Fact]
    public void Cannot_cost_an_amount_that_does_not_compare_with_the_package()
    {
        var price = Price("00000000-0000-0000-0000-00000000000a", 3.49m, Oct1);

        Assert.Equal(QuantityError.Unconvertible, EstimatedCost.Of(Foods.Bread, Quantity.Milliliters(100), price).Error);
    }

    [Fact]
    public void Sums_priced_entries_and_counts_the_rest_as_unpriced()
    {
        Money?[] costs = [new Money(120, Usd), null, new Money(80, Usd), new Money(500, Eur)];

        var total = EstimatedCost.Sum(costs, Usd);

        Assert.Equal(new CostTotal(new Money(200, Usd), 2), total);
        Assert.False(total.IsComplete);
        Assert.True(EstimatedCost.Sum([new Money(1, Usd)], Usd).IsComplete);
    }
}
