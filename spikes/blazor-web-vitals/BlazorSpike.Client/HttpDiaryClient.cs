using System.Net.Http.Json;
using BlazorSpike.Core;

namespace BlazorSpike.Client;

/// <summary>Calls the spike diary API from the WebAssembly runtime.</summary>
public sealed class HttpDiaryClient(HttpClient http) : IDiaryClient
{
    /// <summary>Fetches today's diary for the member's local date from the API.</summary>
    public async Task<DiaryDayDto> GetTodayAsync(CancellationToken ct = default) =>
        (await http.GetFromJsonAsync("api/diary/today", DiaryJson.Default.DiaryDayDto, ct))!;

    /// <summary>Posts an entry for the supplied date and returns the saved entry, throwing on HTTP failure.</summary>
    public async Task<DiaryEntryDto> AddAsync(DateOnly date, DiaryEntryDto entry, CancellationToken ct = default)
    {
        using var res = await http.PostAsJsonAsync($"api/diary/{date:yyyy-MM-dd}", entry, DiaryJson.Default.DiaryEntryDto, ct);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync(DiaryJson.Default.DiaryEntryDto, ct))!;
    }
}
