using System.Net.Http.Json;
using BlazorSpike.Core;

namespace BlazorSpike.Client;

// Stand-in for the typed API client in FitCore.Contracts, used by the WebAssembly runtime.
public sealed class HttpDiaryClient(HttpClient http) : IDiaryClient
{
    public async Task<DiaryDayDto> GetTodayAsync(CancellationToken ct = default) =>
        (await http.GetFromJsonAsync("api/diary/today", DiaryJson.Default.DiaryDayDto, ct))!;

    public async Task<DiaryEntryDto> AddAsync(DateOnly date, DiaryEntryDto entry, CancellationToken ct = default)
    {
        using var res = await http.PostAsJsonAsync($"api/diary/{date:yyyy-MM-dd}", entry, DiaryJson.Default.DiaryEntryDto, ct);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync(DiaryJson.Default.DiaryEntryDto, ct))!;
    }
}
