using System.Collections.Concurrent;
using System.Text.Json;
using BlazorSpike.Core;

namespace BlazorSpike;

// In-memory stand-in for the Nutrition module. One member, one day of 18 entries, some with
// unknown nutrients or no price. Writes wait 80 ms to stand in for a database round trip.
// With a file path, added entries survive a server restart, as the database would make them.
public sealed class DiaryStore
{
    private static readonly TimeZoneInfo ProfileZone = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
    private readonly ConcurrentDictionary<Guid, DiaryEntryDto> added = new();
    private readonly string? file;
    private readonly Lock fileLock = new();
    private int todayReads;

    public DiaryStore(string? file = null)
    {
        this.file = file;
        if (file is not null && File.Exists(file))
        {
            foreach (var e in JsonSerializer.Deserialize<List<DiaryEntryDto>>(File.ReadAllText(file)) ?? [])
                added[e.Id] = e;
        }
    }

    public int AddedCount => added.Count;

    public int TodayReads => todayReads;

    /// <summary>Returns the current date in the member's profile time zone, shared by all render modes.</summary>
    public static DateOnly Today() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ProfileZone));

    /// <summary>Returns today's seeded and added entries with fixed targets, incrementing the read counter.</summary>
    public DiaryDayDto GetToday()
    {
        Interlocked.Increment(ref todayReads);
        var entries = Seed().Concat(added.Values).ToList();
        return new DiaryDayDto(Today(), 2200, 140m, entries);
    }

    /// <summary>Simulates an 80 ms write and returns the entry stored by ID, making repeated IDs idempotent.</summary>
    /// <remarks>This single-day spike ignores the supplied date.</remarks>
    public async Task<DiaryEntryDto> AddAsync(DateOnly date, DiaryEntryDto entry)
    {
        await Task.Delay(80);
        var saved = added.GetOrAdd(entry.Id, entry); // client-generated ID makes retries idempotent
        Save();
        return saved;
    }

    /// <summary>Returns today's entries without counting a read, for the islands fragment after a save.</summary>
    public DiaryDayDto Snapshot() => new(Today(), 2200, 140m, Seed().Concat(added.Values).ToList());

    /// <summary>Clears added entries and the read counter between measurement runs.</summary>
    public void Reset()
    {
        added.Clear();
        todayReads = 0;
        Save();
    }

    private void Save()
    {
        if (file is null) return;
        lock (fileLock) File.WriteAllText(file, JsonSerializer.Serialize(added.Values.ToList()));
    }

    /// <summary>Creates 18 reproducible diary entries, including entries with unknown nutrients or missing prices.</summary>
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

/// <summary>Calls the diary store directly for prerendering and Interactive Server.</summary>
public sealed class InProcessDiaryClient(DiaryStore store) : IDiaryClient
{
    /// <summary>Returns today's diary directly from the store; the spike ignores cancellation.</summary>
    public Task<DiaryDayDto> GetTodayAsync(CancellationToken ct = default) => Task.FromResult(store.GetToday());

    /// <summary>Adds an entry directly to the store; the spike ignores cancellation.</summary>
    public Task<DiaryEntryDto> AddAsync(DateOnly date, DiaryEntryDto entry, CancellationToken ct = default) =>
        store.AddAsync(date, entry);
}
