using FitCore.Client.Core.Accounts;

namespace FitCore.Client.Tests;

/// <summary>The device apps' sign-up form: the web form's rules and messages, the 18+ gate on the member's local date, and the busy state.</summary>
public sealed class SignUpViewModelTests : IDisposable
{
    // 03:00 UTC on 16 October is still 15 October in Chicago.
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 3, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Chicago = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");

    private readonly FakeAccounts _accounts = new();
    private readonly ScriptedAccounts _auth = new();
    private readonly DeviceSession _session;

    public SignUpViewModelTests()
    {
        _session = new DeviceSession(_auth, new MemoryStore(), new SettableTime());
    }

    public void Dispose() => _session.Dispose();

    [Fact]
    public async Task Valid_details_sign_up_and_show_the_check_email_message()
    {
        var viewModel = Filled();
        viewModel.DisplayName = "  Ada Member ";
        viewModel.Email = " ada@example.test ";

        await viewModel.SignUpCommand.ExecuteAsync(null);

        var request = Assert.Single(_accounts.Requests);
        Assert.Equal(new SignUpRequest("ada@example.test", "correct horse battery staple", "Ada Member", new DateOnly(1990, 4, 1), "America/Chicago"), request);
        Assert.True(viewModel.IsConfirmationSent);
        Assert.False(viewModel.IsFormVisible);
        Assert.Null(viewModel.Error);
        Assert.Equal("", viewModel.Password);
    }

    [Fact]
    public async Task The_form_is_busy_while_the_sign_up_is_sent()
    {
        var viewModel = Filled();

        await viewModel.SignUpCommand.ExecuteAsync(null);

        Assert.Equal([true], _accounts.BusyDuringCalls);
        Assert.False(viewModel.IsBusy);
    }

    public static TheoryData<Action<SignUpViewModel>, string> InvalidDetails => new()
    {
        { vm => vm.DisplayName = " ", "Enter your name." },
        { vm => vm.DisplayName = new string('a', 81), "Use 80 characters or fewer for your name." },
        { vm => vm.Email = "", "Enter your email address." },
        { vm => vm.Email = "ada.example.test", "Enter a valid email address." },
        { vm => vm.Email = new string('a', 245) + "@example.test", "Enter a valid email address." },
        { vm => vm.Password = "", "Choose a password." },
        { vm => vm.Password = new string('p', 14), "Use 15 to 64 characters for your password." },
        { vm => vm.Password = new string('p', 65), "Use 15 to 64 characters for your password." },
        { vm => vm.BirthDate = null, "Enter your date of birth." },
        { vm => vm.BirthDate = new DateTime(2015, 1, 1), "You need to be 18 or older to use FitCore." },
    };

    [Theory]
    [MemberData(nameof(InvalidDetails))]
    public async Task Invalid_details_show_the_web_forms_message_and_send_nothing(Action<SignUpViewModel> change, string message)
    {
        var viewModel = Filled();
        change(viewModel);

        await viewModel.SignUpCommand.ExecuteAsync(null);

        Assert.Equal(message, viewModel.Error);
        Assert.True(viewModel.HasError);
        Assert.Empty(_accounts.Requests);
        Assert.False(viewModel.IsConfirmationSent);
    }

    [Theory]
    [InlineData(2008, 10, 15, true)]
    [InlineData(2008, 10, 16, false)]
    public async Task The_18th_birthday_counts_on_the_members_local_date(int year, int month, int day, bool allowed)
    {
        var viewModel = Filled();
        viewModel.BirthDate = new DateTime(year, month, day);

        await viewModel.SignUpCommand.ExecuteAsync(null);

        Assert.Equal(allowed, _accounts.Requests.Count == 1);
    }

    [Theory]
    [InlineData(SignUpStatus.EmailTaken, "An account with that email address already exists. Sign in instead.")]
    [InlineData(SignUpStatus.WeakPassword, "Choose a longer password, or one that hasn't appeared in a data breach.")]
    [InlineData(SignUpStatus.Underage, "You need to be 18 or older to use FitCore.")]
    [InlineData(SignUpStatus.RateLimited, "Too many attempts. Wait a few minutes and try again.")]
    [InlineData(SignUpStatus.Refused, "FitCore couldn't use those details. Check your email address and try again.")]
    [InlineData(SignUpStatus.Unavailable, "FitCore couldn't reach its sign-in service. Try again in a moment.")]
    public async Task A_failed_sign_up_keeps_the_form_and_says_why(SignUpStatus status, string message)
    {
        _accounts.Status = status;
        var viewModel = Filled();

        await viewModel.SignUpCommand.ExecuteAsync(null);

        Assert.Equal(message, viewModel.Error);
        Assert.True(viewModel.IsFormVisible);
        Assert.Equal("correct horse battery staple", viewModel.Password);
    }

    [Fact]
    public async Task Signing_up_again_returns_to_the_form_with_the_details_but_no_password()
    {
        var viewModel = Filled();
        await viewModel.SignUpCommand.ExecuteAsync(null);

        viewModel.SignUpAgainCommand.Execute(null);

        Assert.True(viewModel.IsFormVisible);
        Assert.Equal("ada@example.test", viewModel.Email);
        Assert.Equal("", viewModel.Password);
    }

    [Fact]
    public async Task A_Windows_time_zone_is_sent_by_its_IANA_name()
    {
        var viewModel = Filled(TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time"));

        await viewModel.SignUpCommand.ExecuteAsync(null);

        Assert.Equal("America/Chicago", Assert.Single(_accounts.Requests).TimeZone);
    }

    [Fact]
    public async Task A_time_zone_without_an_IANA_name_is_sent_as_UTC()
    {
        var custom = TimeZoneInfo.CreateCustomTimeZone("FitCore test zone", TimeSpan.FromHours(5), "FitCore test zone", "FitCore test zone");
        var viewModel = Filled(custom);

        await viewModel.SignUpCommand.ExecuteAsync(null);

        Assert.Equal("UTC", Assert.Single(_accounts.Requests).TimeZone);
    }

    [Fact]
    public void The_date_picker_stops_at_the_members_today()
    {
        Assert.Equal(new DateTime(2026, 10, 15), Filled().Today);
    }

    [Fact]
    public async Task Signing_out_clears_what_the_last_person_typed_into_the_sign_up_form()
    {
        var viewModel = Filled();
        await viewModel.SignUpCommand.ExecuteAsync(null);
        await _session.SignInAsync("ada@example.test", "correct horse battery staple", TestContext.Current.CancellationToken);

        await _session.SignOutAsync(TestContext.Current.CancellationToken);

        Assert.Equal("", viewModel.DisplayName);
        Assert.Equal("", viewModel.Email);
        Assert.Equal("", viewModel.Password);
        Assert.Null(viewModel.BirthDate);
        Assert.Null(viewModel.Error);
        Assert.True(viewModel.IsFormVisible);
    }

    [Fact]
    public void A_sign_up_request_never_prints_its_password()
    {
        var request = new SignUpRequest("ada@example.test", "correct horse battery staple", "Ada", new DateOnly(1990, 4, 1), "UTC");

        Assert.DoesNotContain("correct horse", request.ToString(), StringComparison.Ordinal);
    }

    private SignUpViewModel Filled(TimeZoneInfo? zone = null)
    {
        SignUpViewModel? viewModel = null;
        _accounts.Observe = () => viewModel!.IsBusy;
        viewModel = new SignUpViewModel(_accounts, _session, new FixedTime(Now, zone ?? Chicago))
        {
            DisplayName = "Ada Member",
            Email = "ada@example.test",
            Password = "correct horse battery staple",
            BirthDate = new DateTime(1990, 4, 1),
        };
        return viewModel;
    }

    private sealed class FakeAccounts : IMemberAccounts
    {
        public SignUpStatus Status { get; set; } = SignUpStatus.ConfirmationSent;

        public Func<bool> Observe { get; set; } = () => false;

        public List<SignUpRequest> Requests { get; } = [];

        public List<bool> BusyDuringCalls { get; } = [];

        public Task<SignUpStatus> SignUpAsync(SignUpRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            BusyDuringCalls.Add(Observe());
            return Task.FromResult(Status);
        }

        public Task<SignInResult> SignInAsync(string email, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<RefreshResult> RefreshAsync(MemberSession session, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<SignOutStatus> SignOutAsync(MemberSession session, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FixedTime(DateTimeOffset now, TimeZoneInfo zone) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => zone;
    }
}
