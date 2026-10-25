namespace Solvia.Services;

/// <summary>All catalog campuses use California's calendar, independently of the host timezone.</summary>
internal static class CampusCalendar
{
    private static readonly TimeZoneInfo Pacific = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");

    public static DateOnly Today(TimeProvider clock) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Pacific).DateTime);
}
