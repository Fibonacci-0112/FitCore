using FitCore.Domain.Calendar;

namespace FitCore.Domain.Tests;

public sealed class CalendarTests
{
    [Fact]
    public void The_diary_date_is_the_members_local_date()
    {
        var instant = new DateTimeOffset(2026, 10, 14, 3, 30, 0, TimeSpan.Zero);
        var losAngeles = TimeZoneInfo.CreateCustomTimeZone("UTC-07", TimeSpan.FromHours(-7), "UTC-07", "UTC-07");
        var tokyo = TimeZoneInfo.CreateCustomTimeZone("UTC+09", TimeSpan.FromHours(9), "UTC+09", "UTC+09");

        Assert.Equal(new DateOnly(2026, 10, 13), LocalCalendar.DateAt(instant, losAngeles));
        Assert.Equal(new DateOnly(2026, 10, 14), LocalCalendar.DateAt(instant, tokyo));
        Assert.Equal(new DateOnly(2026, 10, 14), LocalCalendar.DateAt(instant, TimeZoneInfo.Utc));
    }

    [Theory]
    [InlineData("2026-03-08T07:30:00Z", "2026-03-07")] // 23:30 PST, the night before DST starts
    [InlineData("2026-03-08T10:30:00Z", "2026-03-08")] // 03:30 PDT, just after the clocks go forward
    [InlineData("2026-11-01T06:59:00Z", "2026-10-31")] // 23:59 PDT
    [InlineData("2026-11-01T07:30:00Z", "2026-11-01")] // 00:30 PDT, before the clocks go back
    [InlineData("2026-11-02T07:59:00Z", "2026-11-01")] // 23:59 PST, after the clocks went back
    public void The_local_date_follows_daylight_saving_time(string instant, string date)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");

        Assert.Equal(
            DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture),
            LocalCalendar.DateAt(DateTimeOffset.Parse(instant, System.Globalization.CultureInfo.InvariantCulture), zone));
    }

    [Theory]
    [InlineData("2008-10-14", "2026-10-14", 18)]
    [InlineData("2008-10-15", "2026-10-14", 17)]
    [InlineData("2008-11-01", "2026-10-14", 17)]
    [InlineData("2008-02-29", "2026-02-28", 17)]
    [InlineData("2008-02-29", "2026-03-01", 18)]
    [InlineData("1990-01-01", "2026-10-14", 36)]
    public void Age_counts_completed_years(string birth, string today, int age)
    {
        Assert.Equal(age, LocalCalendar.AgeOn(DateOnly.Parse(birth, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(today, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Theory]
    [InlineData("2008-10-14", true)]
    [InlineData("2008-10-15", false)]
    [InlineData("2015-01-01", false)]
    public void Only_adults_may_use_FitCore(string birth, bool allowed)
    {
        Assert.Equal(allowed, LocalCalendar.IsOldEnough(DateOnly.Parse(birth, System.Globalization.CultureInfo.InvariantCulture), new DateOnly(2026, 10, 14)));
    }

    [Fact]
    public void Days_between_is_signed()
    {
        Assert.Equal(7, LocalCalendar.DaysBetween(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 8)));
        Assert.Equal(-7, LocalCalendar.DaysBetween(new DateOnly(2026, 10, 8), new DateOnly(2026, 10, 1)));
    }
}
