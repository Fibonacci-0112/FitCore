using System.ComponentModel;
using FitCore.Client.Core.Accounts;

namespace FitCore.Maui.Pages;

public partial class HomePage : ContentPage
{
    private readonly HomeViewModel _viewModel;

    public HomePage(HomeViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _viewModel.PropertyChanged += OnViewModelChanged;
    }

    // Screen readers hear a failed sign-out as it appears (NFR-ACC-2).
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HomeViewModel.Error) && _viewModel.Error is { } error)
        {
            SemanticScreenReader.Announce(error);
        }
    }
}
