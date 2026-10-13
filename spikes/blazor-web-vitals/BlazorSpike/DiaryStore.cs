using System.Collections.Concurrent;
using BlazorSpike.Core;

namespace BlazorSpike;

// In-memory stand-in for the Nutrition module. One member, one day of 18 entries, some with
// unknown nutrients or no price. Writes wait 80 ms to stand in for a database round trip.
public sealed class DiaryStore
{
    private static readonly TimeZoneInfo ProfileZone = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
    private readonly ConcurrentDictionary<Guid, DiaryEntryDto> added = new();
    private int todayReads;

    public int TodayReads => todayReads;

    // The member's local date comes from the profile time zone, never the server clock or the
    // browser, so prerendering on the server and rendering on the client agree.
    public static DateOnly Today() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ProfileZone));

    public DiaryDayDto GetToday()
    {
        Interlocked.Increment(ref todayReads);
        var entries = Seed().Concat(added.Values).ToList();
        return new DiaryDayDto(Today(), 2200, 140m, entries);
    }

    public async Task<DiaryEntryDto> AddAsync(DateOnly date, DiaryEntryDto entry)
    {
        await Task.Delay(80);
        return added.GetOrAdd(entry.Id, entry); // client-generated ID makes retries idempotent
    }

    public void Reset()
    {
        added.Clear();
        todayReads = 0;
    }

    private static IEnumerable<DiaryEntryDto> Seed()
    {
        string[] meals = ["Breakfast", "Lunch", "Dinner", "Snack"];
        string[] foods =
        [
            "Oats, rolled", "Milk, 2%", "Blueberries", "Coffee, black", "Chicken breast, grilled",
            "Brown rice, cooked", "Broccoli, steamed", "Olive oil", "Greek yogurt, plain", "Almonds",
            "Salmon, baked", "Sweet potato", "Spinach", "Homemade chili", "Cheddar cheese",
            "Whole-wheat bread", "Apple", "Protein bar",
        ];
        for (var i = 0; i < foods.Length; i++)
        {
            var id = new Guid(i + 1, 0, 0, new byte[8]);
            int? kcal = i % 7 == 6 ? null : 60 + (i * 37 % 300);
            decimal? protein = i % 7 == 6 ? null : Math.Round(2m + i * 1.7m, 1);
            long? cost = i % 5 == 4 ? null : 40 + i * 23;
            yield return new DiaryEntryDto(id, foods[i], meals[i * 4 / foods.Length], 50 + i * 10, kcal, protein, cost, "USD");
        }
    }
}

// Server-side IDiaryClient for prerendering and Interactive Server: calls the module directly.
public sealed class InProcessDiaryClient(DiaryStore store) : IDiaryClient
{
    public Task<DiaryDayDto> GetTodayAsync(CancellationToken ct = default) => Task.FromResult(store.GetToday());

    public Task<DiaryEntryDto> AddAsync(DateOnly date, DiaryEntryDto entry, CancellationToken ct = default) =>
        store.AddAsync(date, entry);
}
