using System.ComponentModel;
using FitCore.Client.Core.Accounts;

namespace FitCore.Maui.Pages;

public partial class SignInPage : ContentPage
{
    private readonly SignInViewModel _viewModel;

    public SignInPage(SignInViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _viewModel.PropertyChanged += OnViewModelChanged;
    }

    // Screen readers hear an error as it appears (NFR-ACC-2).
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SignInViewModel.Error) && _viewModel.Error is { } error)
        {
            SemanticScreenReader.Announce(error);
        }
    }

    private async void OnCreateAccountClicked(object? sender, EventArgs e) => await Shell.Current.GoToAsync(AppShell.SignUpRoute);
}
