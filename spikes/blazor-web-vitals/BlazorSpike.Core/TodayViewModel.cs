using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BlazorSpike.Core;

// Stand-in for a FitCore.Client.Core view model shared by MAUI XAML and Blazor (NFR-MNT-4).
public sealed partial class TodayViewModel : ObservableObject
{
    private readonly IDiaryClient client;

    public TodayViewModel(DiaryDayDto day, IDiaryClient client)
    {
        this.client = client;
        Date = day.Date;
        TargetKcal = day.TargetKcal;
        TargetProteinG = day.TargetProteinG;
        Rows = new(day.Entries.Select(e => new EntryRow(e) { Saved = true }));
        Totals = NutritionTotals.Sum(day.Entries);
    }

    public DateOnly Date { get; }
    public int TargetKcal { get; }
    public decimal TargetProteinG { get; }
    public ObservableCollection<EntryRow> Rows { get; }

    [ObservableProperty]
    public partial NutritionTotals Totals { get; set; }

    // Optimistic add (NFR-PRF-7): the row shows before the server confirms it. The client
    // generates the ID so a retry is idempotent.
    [RelayCommand]
    private async Task QuickAddAsync()
    {
        var entry = new DiaryEntryDto(Guid.NewGuid(), "Banana, raw", "Snack", 118m, 105, 1.3m, 25, "USD");
        var row = new EntryRow(entry);
        Rows.Add(row);
        Totals = NutritionTotals.Sum(Rows.Select(r => r.Entry));
#if !SPIKE_NO_YIELD
        // Return to the UI before the API call, so the optimistic row paints first. On Blazor
        // WebAssembly the first HTTP and JSON call costs 200-400 ms of CPU on a phone; inside
        // the tap it would push the tap past INP's 200 ms. MAUI is unaffected either way.
        await Task.Yield();
#endif
        try
        {
            await client.AddAsync(Date, entry);
            row.Saved = true;
        }
        catch (Exception)
        {
            row.Failed = true;
        }
    }
}

public sealed partial class EntryRow(DiaryEntryDto entry) : ObservableObject
{
    public DiaryEntryDto Entry { get; } = entry;

    [ObservableProperty]
    public partial bool Saved { get; set; }

    [ObservableProperty]
    public partial bool Failed { get; set; }
}
