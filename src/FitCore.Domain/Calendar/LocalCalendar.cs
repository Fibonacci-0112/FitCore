namespace FitCore.Domain.Calendar;

/// <summary>Dates on the member's local calendar (BRD §6.1, NFR-L10N-3).</summary>
public static class LocalCalendar
{
    /// <summary>The youngest age FitCore serves (BRD §6.2).</summary>
    public const int MinimumAgeYears = 18;

    /// <summary>The member's calendar date at <paramref name="instant"/> in their time zone.</summary>
    public static DateOnly DateAt(DateTimeOffset instant, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
    }

    /// <summary>Completed years of age on <paramref name="today"/>. Someone born on 29 February turns a year older on 1 March in non-leap years.</summary>
    public static int AgeOn(DateOnly birthDate, DateOnly today)
    {
        var age = today.Year - birthDate.Year;
        return (today.Month, today.Day).CompareTo((birthDate.Month, birthDate.Day)) < 0 ? age - 1 : age;
    }

    /// <summary>Whether someone born on <paramref name="birthDate"/> is old enough to use FitCore on <paramref name="today"/>.</summary>
    public static bool IsOldEnough(DateOnly birthDate, DateOnly today) => AgeOn(birthDate, today) >= MinimumAgeYears;

    /// <summary>Whole days from <paramref name="from"/> to <paramref name="to"/>; negative if <paramref name="to"/> is earlier.</summary>
    public static int DaysBetween(DateOnly from, DateOnly to) => to.DayNumber - from.DayNumber;
}
