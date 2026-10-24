using FitCore.Client.Core.Accounts;

namespace FitCore.Client.Tests;

/// <summary>The device apps' sign-in and sign-out screens: the web's rules and messages, and the busy state.</summary>
public sealed class SignInViewModelTests : IDisposable
{
    private readonly ScriptedAccounts _accounts = new();
    private readonly MemoryStore _store = new();
    private readonly DeviceSession _session;

    public SignInViewModelTests()
    {
        _session = new DeviceSession(_accounts, _store, new SettableTime());
    }

    public void Dispose() => _session.Dispose();

    [Fact]
    public async Task Valid_details_sign_in_and_forget_the_password()
    {
        var viewModel = Filled();
        viewModel.Email = " ada@example.test ";

        await viewModel.SignInCommand.ExecuteAsync(null);

        Assert.Equal(("ada@example.test", "correct horse battery staple"), Assert.Single(_accounts.SignIns));
        Assert.True(_session.IsSignedIn);
        Assert.Null(viewModel.Error);
        Assert.Equal("", viewModel.Password);
    }

    [Fact]
    public async Task The_form_is_busy_while_the_sign_in_is_sent()
    {
        var viewModel = Filled();

        await viewModel.SignInCommand.ExecuteAsync(null);

        Assert.Equal([true], _accounts.BusyDuringCalls);
        Assert.False(viewModel.IsBusy);
    }

    [Theory]
    [InlineData("", "correct horse battery staple", "Enter your email address.")]
    [InlineData("ada.example.test", "correct horse battery staple", "Enter a valid email address.")]
    [InlineData("ada@example.test", "", "Enter your password.")]
    public async Task Missing_details_show_the_web_forms_message_and_send_nothing(string email, string password, string message)
    {
        var viewModel = Filled();
        viewModel.Email = email;
        viewModel.Password = password;

        await viewModel.SignInCommand.ExecuteAsync(null);

        Assert.Equal(message, viewModel.Error);
        Assert.True(viewModel.HasError);
        Assert.Empty(_accounts.SignIns);
    }

    [Fact]
    public async Task A_short_password_from_before_the_length_rule_is_still_sent()
    {
        var viewModel = Filled();
        viewModel.Password = "short";

        await viewModel.SignInCommand.ExecuteAsync(null);

        Assert.Single(_accounts.SignIns);
    }

    [Theory]
    [InlineData(SignInStatus.InvalidCredentials, "That email address and password don't match an account.")]
    [InlineData(SignInStatus.EmailNotConfirmed, "Confirm your email address first: open the link we sent you. If it has expired, sign up again with the same address and we'll send a new one.")]
    [InlineData(SignInStatus.RateLimited, "Too many attempts. Wait a few minutes and try again.")]
    [InlineData(SignInStatus.Unavailable, "FitCore couldn't reach its sign-in service. Try again in a moment.")]
    public async Task A_failed_sign_in_keeps_the_form_and_says_why(SignInStatus status, string message)
    {
        _accounts.SignIn = new(status);
        var viewModel = Filled();

        await viewModel.SignInCommand.ExecuteAsync(null);

        Assert.Equal(message, viewModel.Error);
        Assert.False(_session.IsSignedIn);
        Assert.Equal("correct horse battery staple", viewModel.Password);
    }

    [Fact]
    public async Task Signing_out_clears_the_sign_in_form_for_the_next_person()
    {
        _accounts.SignIn = new(SignInStatus.InvalidCredentials);
        var viewModel = Filled();
        await viewModel.SignInCommand.ExecuteAsync(null);
        _accounts.SignIn = new(SignInStatus.SignedIn, SessionFakes.Session("first"));
        await _session.SignInAsync("ada@example.test", "correct horse battery staple", TestContext.Current.CancellationToken);
        viewModel.Email = "ada@example.test";

        await _session.SignOutAsync(TestContext.Current.CancellationToken);

        Assert.Equal("", viewModel.Email);
        Assert.Equal("", viewModel.Password);
        Assert.Null(viewModel.Error);
    }

    [Fact]
    public async Task Signing_out_from_home_ends_the_session()
    {
        await _session.SignInAsync("ada@example.test", "correct horse battery staple", TestContext.Current.CancellationToken);
        var home = new HomeViewModel(_session);
        _accounts.Observe = () => home.IsBusy;

        await home.SignOutCommand.ExecuteAsync(null);

        Assert.False(_session.IsSignedIn);
        Assert.Null(_store.Saved);
        Assert.Null(home.Error);
        Assert.Equal([true], _accounts.BusyDuringCalls[1..]);
        Assert.False(home.IsBusy);
    }

    [Fact]
    public async Task A_sign_out_Auth_cant_confirm_says_the_member_is_still_signed_in()
    {
        await _session.SignInAsync("ada@example.test", "correct horse battery staple", TestContext.Current.CancellationToken);
        _accounts.SignOut = SignOutStatus.Unavailable;
        var home = new HomeViewModel(_session);

        await home.SignOutCommand.ExecuteAsync(null);

        Assert.True(_session.IsSignedIn);
        Assert.Equal("FitCore couldn't reach its sign-in service to end your session. Try again in a moment.", home.Error);
        Assert.True(home.HasError);
    }

    private SignInViewModel Filled()
    {
        SignInViewModel? viewModel = null;
        _accounts.Observe = () => viewModel!.IsBusy;
        viewModel = new SignInViewModel(_session)
        {
            Email = "ada@example.test",
            Password = "correct horse battery staple",
        };
        return viewModel;
    }
}
