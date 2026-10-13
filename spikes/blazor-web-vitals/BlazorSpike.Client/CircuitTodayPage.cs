using System.Collections.Specialized;
using System.ComponentModel;
using BlazorSpike.Core;
using Microsoft.AspNetCore.Components;

namespace BlazorSpike.Client;

/// <summary>
/// Base for the pages that can run on a circuit (/server and /auto). Like /wasm, it carries the prerendered day in
/// [PersistentState]. It also keeps that state current as rows are saved, because .NET 10 persists the same property
/// when a circuit is evicted or paused, and restores it into the new circuit. Only saved rows go into it: the view
/// model treats every entry in the day as saved, so a pending or failed row must not come back as saved.
/// </summary>
public abstract class CircuitTodayPage : ComponentBase
{
    [Inject] private IDiaryClient Diary { get; set; } = default!;

    [PersistentState]
    public DiaryDayDto? Day { get; set; }

    protected TodayViewModel? Model { get; private set; }

    protected override async Task OnInitializedAsync()
    {
        Day ??= await Diary.GetTodayAsync();
        Model = new TodayViewModel(Day, Diary);
        Model.Rows.CollectionChanged += OnRowsChanged;
    }

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var row in e.NewItems?.OfType<EntryRow>() ?? [])
        {
            row.PropertyChanged += OnRowChanged;
        }

        PersistSavedRows();
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EntryRow.Saved))
        {
            PersistSavedRows();
        }
    }

    private void PersistSavedRows() =>
        Day = Day! with { Entries = [.. Model!.Rows.Where(r => r.Saved).Select(r => r.Entry)] };
}
