using System.Globalization;

namespace BlazorSpike.Core;

// Stand-ins for FitCore.Contracts and FitCore.Domain. Nutrients and cost are nullable:
// unknown is not zero (BRD §6.1). Money is integer minor units plus an ISO 4217 code.
public sealed record DiaryEntryDto(
    Guid Id, string Food, string Meal, decimal Grams,
    int? Kcal, decimal? ProteinG, long? CostMinor, string Currency);

public sealed record DiaryDayDto(
    DateOnly Date, int TargetKcal, decimal TargetProteinG, List<DiaryEntryDto> Entries);

public interface IDiaryClient
{
    // "Today" is the member's local date, resolved on the server from the profile time zone.
    Task<DiaryDayDto> GetTodayAsync(CancellationToken ct = default);
    Task<DiaryEntryDto> AddAsync(DateOnly date, DiaryEntryDto entry, CancellationToken ct = default);
}

public sealed record NutritionTotals(
    int KcalAtLeast, decimal ProteinAtLeast, long CostMinorAtLeast,
    int IncompleteNutrients, int MissingCost)
{
    public static NutritionTotals Sum(IEnumerable<DiaryEntryDto> entries)
    {
        int kcal = 0, incomplete = 0, missingCost = 0;
        decimal protein = 0;
        long cost = 0;
        foreach (var e in entries)
        {
            if (e.Kcal is null || e.ProteinG is null) incomplete++;
            kcal += e.Kcal ?? 0;
            protein += e.ProteinG ?? 0;
            if (e.CostMinor is { } c) cost += c; else missingCost++;
        }
        return new(kcal, protein, cost, incomplete, missingCost);
    }
}

public static class Money
{
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    // The spike only handles USD; real code looks up the currency's minor-unit exponent.
    public static string Format(long minor, string currency) =>
        currency == "USD" ? (minor / 100m).ToString("C", Us) : $"{minor} {currency} (minor units)";
}

// Source-generated JSON so the trimmed WebAssembly bundle needs no reflection metadata.
[System.Text.Json.Serialization.JsonSourceGenerationOptions(System.Text.Json.JsonSerializerDefaults.Web)]
[System.Text.Json.Serialization.JsonSerializable(typeof(DiaryDayDto))]
[System.Text.Json.Serialization.JsonSerializable(typeof(DiaryEntryDto))]
public sealed partial class DiaryJson : System.Text.Json.Serialization.JsonSerializerContext;
