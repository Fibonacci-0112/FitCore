using System.ComponentModel;
using FitCore.Client.Core.Accounts;
using FitCore.Client.Core.Resources;

namespace FitCore.Maui.Pages;

public partial class SignUpPage : ContentPage
{
    private readonly SignUpViewModel _viewModel;

    public SignUpPage(SignUpViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _viewModel.PropertyChanged += OnViewModelChanged;
    }

    // Screen readers hear an error and the check-email message as they appear (NFR-ACC-2).
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SignUpViewModel.Error) && _viewModel.Error is { } error)
        {
            SemanticScreenReader.Announce(error);
        }
        else if (e.PropertyName == nameof(SignUpViewModel.IsConfirmationSent) && _viewModel.IsConfirmationSent)
        {
            SemanticScreenReader.Announce(AccountStrings.CheckEmailTitle + ". " + AccountStrings.CheckEmailBody);
        }
    }
}
