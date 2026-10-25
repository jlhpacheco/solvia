using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Solvia.Config;
using Solvia.Services;

namespace Solvia.Tests;

/// <summary>Deterministic relative-date grounding; no provider or payment calls.</summary>
public sealed class RelativeVisitScheduleTests
{
    private static readonly DateOnly Sunday = new(2026, 10, 25);
    private const string ReportedSentence =
        "I want a parking permit today and tomorrow as well on wednesday. please. this is going to be a big week for me";

    [Fact]
    public void ReportedList_ResolvesTheExactThreeDatesFromThePacificAnchor()
    {
        var result = ScheduleGrounding.Read(ReportedSentence, Sunday);

        Assert.Equal("fallback", result.Source);
        Assert.Equal(new DateOnly(2026, 10, 25), result.StartDate);
        Assert.Equal(new DateOnly(2026, 10, 28), result.EndDate);
        Assert.Equal(1, result.Weeks);
        Assert.False(result.SingleVisit);
        Assert.Equal(new[] { DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Wednesday }, result.Weekdays);
        AssertResolvedNotice(result, "2026-10-25", "2026-10-26", "2026-10-28");
        var actualDates = Enumerable.Range(0, result.EndDate!.Value.DayNumber - result.StartDate!.Value.DayNumber + 1)
            .Select(offset => result.StartDate.Value.AddDays(offset))
            .Where(date => result.Weekdays.Contains(date.DayOfWeek)).ToArray();
        Assert.Equal(new[] { new DateOnly(2026, 10, 25), new DateOnly(2026, 10, 26), new DateOnly(2026, 10, 28) }, actualDates);
    }

    [Theory]
    [InlineData("today", "2026-10-25", DayOfWeek.Sunday)]
    [InlineData("tomorrow", "2026-10-26", DayOfWeek.Monday)]
    [InlineData("day after tomorrow", "2026-10-27", DayOfWeek.Tuesday)]
    [InlineData("I want one parking permit tomorrow please", "2026-10-26", DayOfWeek.Monday)]
    [InlineData("today and today", "2026-10-25", DayOfWeek.Sunday)]
    [InlineData("tomorrow and tomorrow", "2026-10-26", DayOfWeek.Monday)]
    [InlineData("tomorrow and Monday", "2026-10-26", DayOfWeek.Monday)]
    public void OneDistinctRelativeDate_IsOneVisit(string text, string expected, DayOfWeek day)
    {
        var date = Date(expected);
        var result = ScheduleGrounding.Read(text, Sunday);
        Assert.Equal(date, result.StartDate);
        Assert.Null(result.EndDate);
        Assert.True(result.SingleVisit);
        Assert.Equal(1, result.Weeks);
        Assert.Equal(new[] { day }, result.Weekdays);
        Assert.Equal("fallback", result.Source);
        AssertResolvedNotice(result, expected);
    }

    [Fact]
    public void TodayAndTheSameNamedWeekday_DoNotCreateADuplicateVisit()
    {
        var result = ScheduleGrounding.Read("today and Wednesday", new DateOnly(2026, 10, 28));
        Assert.Equal(new DateOnly(2026, 10, 28), result.StartDate);
        Assert.Null(result.EndDate);
        Assert.True(result.SingleVisit);
        Assert.Equal(1, result.Weeks);
        Assert.Equal(new[] { DayOfWeek.Wednesday }, result.Weekdays);
        AssertResolvedNotice(result, "2026-10-28");
    }

    [Fact]
    public void TodayAndTomorrow_KeepTwoDatesAndDoNotInferAllDaysOfAWeek()
    {
        var result = ScheduleGrounding.Read("today and tomorrow", Sunday);
        Assert.Equal(Sunday, result.StartDate);
        Assert.Equal(Sunday.AddDays(1), result.EndDate);
        Assert.False(result.SingleVisit);
        Assert.Equal(1, result.Weeks);
        Assert.Equal(new[] { DayOfWeek.Sunday, DayOfWeek.Monday }, result.Weekdays);
        AssertResolvedNotice(result, "2026-10-25", "2026-10-26");
    }

    [Fact]
    public void TomorrowAcrossYearEnd_UsesTheActualNextCalendarDate()
    {
        var result = ScheduleGrounding.Read("tomorrow", new DateOnly(2026, 12, 31));
        Assert.Equal(new DateOnly(2027, 1, 1), result.StartDate);
        Assert.Null(result.EndDate);
        Assert.True(result.SingleVisit);
        Assert.Equal(new[] { DayOfWeek.Friday }, result.Weekdays);
        AssertResolvedNotice(result, "2027-01-01");
    }

    [Theory]
    [InlineData("maybe today and tomorrow")]
    [InlineData("today or tomorrow")]
    [InlineData("not today but tomorrow")]
    [InlineData("today except Wednesday")]
    [InlineData("today (Monday)")]
    [InlineData("tomorrow (Tuesday)")]
    [InlineData("today and next Wednesday")]
    [InlineData("today and last Wednesday")]
    [InlineData("today and Wednesday this week")]
    [InlineData("today and Wednesday next week")]
    [InlineData("today and every Wednesday")]
    [InlineData("today and Wednesdays")]
    [InlineData("today and tomorrow weekly")]
    [InlineData("today and tomorrow for three weeks")]
    [InlineData("today and Wednesday in November")]
    [InlineData("today and Wednesday November 2026")]
    [InlineData("today to Wednesday")]
    [InlineData("today through Wednesday")]
    [InlineData("today until Wednesday")]
    [InlineData("today up to Wednesday")]
    [InlineData("today and two days after tomorrow")]
    [InlineData("today and two day after tomorrow")]
    [InlineData("today and the day before tomorrow")]
    [InlineData("today and tomorrow 10/26/2026")]
    [InlineData("today and tomorrow 2026-10-26")]
    public void AmbiguousConflictingRecurringOrMixedClaims_RequireReviewWithoutPartialDates(string text) =>
        AssertReview(ScheduleGrounding.Read(text, Sunday));

    [Fact]
    public void RelativeTextWithoutAnExplicitServerAnchor_DoesNotUseTheMachineClock() =>
        AssertReview(ScheduleGrounding.Read("today and tomorrow"));

    [Theory]
    [InlineData("tomorrow")]
    [InlineData("day after tomorrow")]
    public void UnsupportedCalendarOverflow_RequiresReviewInsteadOfThrowing(string text) =>
        AssertReview(ScheduleGrounding.Read(text, DateOnly.MaxValue));

    [Theory]
    [InlineData("2026-10-26T06:59:00Z", "2026-10-25")]
    [InlineData("2026-10-26T07:00:00Z", "2026-10-26")]
    [InlineData("2026-11-01T06:59:00Z", "2026-10-31")]
    [InlineData("2026-11-01T07:00:00Z", "2026-11-01")]
    [InlineData("2026-11-01T08:59:00Z", "2026-11-01")]
    [InlineData("2026-11-01T09:00:00Z", "2026-11-01")]
    [InlineData("2026-11-02T07:59:00Z", "2026-11-01")]
    [InlineData("2026-11-02T08:00:00Z", "2026-11-02")]
    public void PacificCalendar_IgnoresTheHostsUtcZoneIncludingTheDstBoundary(string utc, string expected)
    {
        var clock = new Clock(DateTimeOffset.Parse(utc, CultureInfo.InvariantCulture));
        Assert.Equal(TimeZoneInfo.Utc, clock.LocalTimeZone);
        Assert.Equal(Date(expected), CampusCalendar.Today(clock));
        Assert.Equal(1, clock.UtcReads);
    }

    [Fact]
    public async Task AdapterCapturesOnePacificDateEvenWhenTheClockCrossesMidnight()
    {
        var clock = new Clock(new DateTimeOffset(2026, 10, 26, 6, 59, 59, TimeSpan.Zero), advanceAfterRead: true);
        var handler = new NoHttp();
        using var http = new HttpClient(handler);
        var reader = new TogetherVisitScheduleReader(http,
            Options.Create(new TogetherOptions { ApiKey = "", Model = TogetherOptions.SupportedModel }),
            NullLogger<TogetherVisitScheduleReader>.Instance, clock,
            Options.Create(new AiOptions { Provider = "together" }));
        var result = await reader.ReadAsync("today and tomorrow");
        Assert.Equal(new DateOnly(2026, 10, 25), result.StartDate);
        Assert.Equal(new DateOnly(2026, 10, 26), result.EndDate);
        Assert.Equal("fallback", result.Source);
        Assert.Equal(1, clock.UtcReads);
        Assert.Equal(0, handler.Calls);
    }

    private static DateOnly Date(string value) => DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static void AssertResolvedNotice(VisitScheduleDraft result, params string[] dates)
    {
        Assert.NotNull(result.Notice);
        Assert.Contains("Pacific", result.Notice, StringComparison.OrdinalIgnoreCase);
        foreach (var date in dates) Assert.Contains(date, result.Notice);
        Assert.DoesNotContain("needs your review", result.SourceNote, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("need your review", result.Notice, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertReview(VisitScheduleDraft result)
    {
        Assert.Equal("fallback", result.Source);
        Assert.Empty(result.Weekdays);
        Assert.Null(result.Weeks);
        Assert.Null(result.StartDate);
        Assert.Null(result.EndDate);
        Assert.False(result.SingleVisit);
        Assert.NotNull(result.Notice);
        Assert.Contains("review", result.SourceNote, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Clock(DateTimeOffset utc, bool advanceAfterRead = false) : TimeProvider
    {
        public int UtcReads;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public override DateTimeOffset GetUtcNow()
        {
            var reads = UtcReads++;
            return advanceAfterRead && reads > 0 ? utc.AddSeconds(2) : utc;
        }
    }

    private sealed class NoHttp : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("This offline test must never make an HTTP request.");
        }
    }
}
