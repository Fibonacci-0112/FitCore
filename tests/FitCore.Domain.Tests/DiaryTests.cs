using FitCore.Domain.Costs;
using FitCore.Domain.Diary;
using FitCore.Domain.Nutrition;
using FitCore.Domain.Quantities;

namespace FitCore.Domain.Tests;

public sealed class DiaryTests
{
    private static readonly Guid BreadId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly DateOnly Today = new(2026, 10, 14);
    private static readonly CurrencyCode Usd = CurrencyCode.Usd;

    private static readonly Nutrients BreadPer100G = new() { EnergyKcal = 250, ProteinGrams = 9, CarbohydrateGrams = 49, FatGrams = 3.2m };

    private static readonly PriceRecord BreadPrice = new(
        Guid.Parse("20000000-0000-0000-0000-000000000001"), BreadId, Quantity.Grams(500), Money.FromMajor(3.49m, Usd), Today.AddDays(-7), DateTimeOffset.UnixEpoch);

    private static Result<EntrySnapshot, QuantityError> Snapshot(Quantity eaten, PriceRecord[] prices, FoodMeasures? food = null) =>
        EntrySnapshot.Take(BreadId, food ?? Foods.Bread, BreadPer100G, eaten, prices, Today, Usd);

    [Fact]
    public void A_snapshot_keeps_the_nutrients_and_cost_at_log_time()
    {
        var entry = Snapshot(Quantity.Servings(2, Foods.SliceId), [BreadPrice]).Value;

        Assert.Equal(56m, entry.Measures.Grams);
        Assert.Equal(140m, entry.Nutrients.EnergyKcal);
        Assert.Equal(5.04m, entry.Nutrients.ProteinGrams);
        Assert.Null(entry.Nutrients.FiberGrams);
        Assert.Equal(CostStatus.Priced, entry.CostStatus);
        Assert.Equal(new Money(39, Usd), entry.Cost);
        Assert.Equal(BreadPrice.Id, entry.PriceId);
    }

    [Fact]
    public void A_later_price_change_does_not_alter_an_existing_snapshot()
    {
        var entry = Snapshot(Quantity.Grams(100), [BreadPrice]).Value;
        var newerPrice = BreadPrice with { Id = Guid.NewGuid(), Price = Money.FromMajor(5m, Usd), EffectiveDate = Today };

        var later = Snapshot(Quantity.Grams(100), [BreadPrice, newerPrice]).Value;

        Assert.Equal(new Money(70, Usd), entry.Cost);
        Assert.Equal(new Money(100, Usd), later.Cost);
    }

    public static TheoryData<string, PriceRecord[], CostStatus> Unpriced => new()
    {
        { "no price", [], CostStatus.NoPrice },
        { "a price from tomorrow", [BreadPrice with { EffectiveDate = Today.AddDays(1) }], CostStatus.NotYetEffective },
        { "a price in euros", [BreadPrice with { Price = Money.FromMajor(3m, CurrencyCode.Parse("EUR")) }], CostStatus.CurrencyMismatch },
        { "a price per package of millilitres", [BreadPrice with { Package = Quantity.Milliliters(500) }], CostStatus.Unconvertible },
    };

    [Theory]
    [MemberData(nameof(Unpriced))]
    public void A_missing_or_unusable_price_saves_the_entry_as_unpriced(string _, PriceRecord[] prices, CostStatus status)
    {
        var entry = Snapshot(Quantity.Grams(100), prices).Value;

        Assert.Equal(status, entry.CostStatus);
        Assert.Null(entry.Cost);
        Assert.Null(entry.PriceId);
        Assert.Equal(250m, entry.Nutrients.EnergyKcal);
    }

    [Fact]
    public void An_amount_that_cannot_convert_to_the_nutrient_basis_is_rejected()
    {
        Assert.Equal(QuantityError.Unconvertible, Snapshot(Quantity.Milliliters(100), [BreadPrice]).Error);
    }

    [Fact]
    public void The_day_summary_is_a_lower_bound_with_incomplete_and_unpriced_counts()
    {
        EntrySnapshot[] entries =
        [
            Snapshot(Quantity.Grams(100), [BreadPrice]).Value,
            Snapshot(Quantity.Grams(100), []).Value,
            new(default, new Nutrients { EnergyKcal = null, ProteinGrams = 20 }, CostStatus.Priced, new Money(150, Usd), Guid.NewGuid()),
        ];

        var day = DaySummary.Of(entries, Usd);

        Assert.Equal(3, day.EntryCount);
        Assert.Equal(new NutrientTotal(500, 1), day.Nutrients[Nutrient.EnergyKcal]);
        Assert.Equal(new NutrientTotal(38, 0), day.Nutrients[Nutrient.ProteinGrams]);
        Assert.Equal(new CostTotal(new Money(220, Usd), 1), day.EstimatedCost);
    }

    [Fact]
    public void An_empty_day_has_no_entries_and_costs_nothing()
    {
        var day = DaySummary.Of([], Usd);

        Assert.Equal(0, day.EntryCount);
        Assert.Equal(new CostTotal(Money.Zero(Usd), 0), day.EstimatedCost);
    }
}
