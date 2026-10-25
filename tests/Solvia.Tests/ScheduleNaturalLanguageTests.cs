using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Solvia.Config;
using Solvia.Services;

namespace Solvia.Tests;

/// <summary>Deterministic natural-language boundaries; no network, model or payment calls.</summary>
public sealed class ScheduleNaturalLanguageTests
{
    private static readonly DayOfWeek[] Mwf = [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday];

    private static DateOnly Date(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static void AssertRange(VisitScheduleDraft draft, string start, string end,
        int weeks, IReadOnlyList<DayOfWeek> days, int visits)
    {
        Assert.Equal("fallback", draft.Source);
        Assert.Equal(Date(start), draft.StartDate);
        Assert.Equal(Date(end), draft.EndDate);
        Assert.Equal(weeks, draft.Weeks);
        Assert.Equal(days, draft.Weekdays);
        Assert.False(draft.SingleVisit);
        Assert.Null(draft.Notice);
        var actual = Enumerable.Range(0, Date(end).DayNumber - Date(start).DayNumber + 1)
            .Select(offset => Date(start).AddDays(offset)).Count(date => days.Contains(date.DayOfWeek));
        Assert.Equal(visits, actual);
    }

    [Theory]
    [InlineData("10/26/2026 , monday to 10/30/2026, Friday. for 3 visits, as well as Wednesday")]
    [InlineData("from 10/26/2026, Monday to 10/30/2026, Friday. for three visits, as well as Wednesday")]
    [InlineData("10/26/2026, Monday through 10/30/2026, Friday, and Wednesday. Total 3 visits.")]
    [InlineData("10/26/2026, Monday until 10/30/2026, Friday, with Wednesday, for 3 visits")]
    [InlineData("Monday, Wednesday and Friday 10/26/2026 to 10/30/2026 for 3 visits")]
    [InlineData("Every Monday, Wednesday and Friday from 2026-10-26 through 2026-10-30, for 3 visits")]
    [InlineData("Monday, Wednesday and Friday from October 26, 2026 to October 30, 2026, for 3 visits")]
    [InlineData("Monday, Wednesday and Friday from Oct 26, 2026 to Oct 30, 2026, for three visits")]
    [InlineData("Monday, Wednesday and Friday from 26 October 2026 through 30 October 2026, for 3 visits")]
    [InlineData("Monday, Wednesday and Friday from October 26th, 2026 to October 30th, 2026, for 3 visits")]
    [InlineData("Monday, Wednesday and Friday from Monday 10/26/2026 to Friday 10/30/2026 for 3 visits")]
    [InlineData("Monday 10/26/2026 to Friday 10/30/2026, as well as Wednesday, for 3 visits")]
    public void NaturalOrderedRangesAndMatchingTotal_PreserveExactThreeVisits(string sentence) =>
        AssertRange(ScheduleGrounding.Read(sentence), "2026-10-26", "2026-10-30", 1, Mwf, 3);

    [Theory]
    [InlineData("Monday, Wednesday and Friday from 10/26/2026 to 11/06/2026 for 6 visits")]
    [InlineData("Monday, Wednesday and Friday from 10/26/2026 through 11/06/2026, total six visits")]
    public void TotalVisitsAreCountedAcrossTheWholeRangeRatherThanConfusedWithWeeks(string sentence) =>
        AssertRange(ScheduleGrounding.Read(sentence), "2026-10-26", "2026-11-06", 2, Mwf, 6);

    [Fact]
    public void WeeklyVisitFrequencyIsNotMistakenForTheWholeRangesTotal()
    {
        var draft = ScheduleGrounding.Read(
            "Tuesdays and Thursdays from 10/26/2026 through 11/08/2026, 2 visits per week");
        AssertRange(draft, "2026-10-26", "2026-11-08", 2,
            [DayOfWeek.Tuesday, DayOfWeek.Thursday], 4);
    }

    [Theory]
    [InlineData("thirty", "2026-11-24", 30)]
    [InlineData("one hundred", "2027-02-02", 100)]
    [InlineData("one hundred and three", "2027-02-05", 103)]
    [InlineData("one hundred eighty-two", "2027-04-25", 182)]
    public void WrittenVisitTotalsAboveTwentySixAreValidatedWithinThePlannerBound(string words, string end, int visits)
    {
        var draft = ScheduleGrounding.Read($"Every day from 10/26/2026 to {end} for {words} visits");
        Assert.Equal(Date("2026-10-26"), draft.StartDate);
        Assert.Equal(Date(end), draft.EndDate);
        Assert.Equal(7, draft.Weekdays.Count);
        Assert.Null(draft.Notice);
        Assert.Equal(visits, Date(end).DayNumber - Date("2026-10-26").DayNumber + 1);
    }

    [Fact]
    public void UnannotatedEndpointsNeedNotBeSelectedVisitWeekdays()
    {
        var draft = ScheduleGrounding.Read(
            "Wednesdays from 10/26/2026 to 10/30/2026 for 1 visit");
        AssertRange(draft, "2026-10-26", "2026-10-30", 1, [DayOfWeek.Wednesday], 1);
    }

    [Theory]
    [InlineData("10/26/2026, Monay to 10/30/2026, Firday. for 3 visits, as well as Wendesday")]
    [InlineData("Monay, Wendesday and Firday from 10/26/2026 through 10/30/2026 for three visits")]
    public void SupportedWeekdayMisspellingsStillProduceOnlyGroundedRangeDates(string sentence) =>
        AssertRange(ScheduleGrounding.Read(sentence), "2026-10-26", "2026-10-30", 1, Mwf, 3);

    [Fact]
    public void SupportedNovemberMisspellingKeepsTheActualMonthEnd()
    {
        var draft = ScheduleGrounding.Read(
            "Monay, Wendesday and Firday from 10/26/2026 until the end of Novemeber 2026");
        AssertRange(draft, "2026-10-26", "2026-11-30", 6, Mwf, 16);
    }

    [Fact]
    public void SupportedFebruaryMisspellingUsesLeapYearLengthRatherThanThirtyDays()
    {
        var draft = ScheduleGrounding.Read("Mondays from 02/01/2028 until end of Febuary 2028");
        AssertRange(draft, "2028-02-01", "2028-02-29", 5, [DayOfWeek.Monday], 4);
    }

    [Fact]
    public void SupportedWeekMisspellingDoesNotSupplyUnstatedDates()
    {
        var draft = ScheduleGrounding.Read("Monay, Wendesday and Firday for 1 weeeks");
        Assert.Equal(Mwf, draft.Weekdays);
        Assert.Equal(1, draft.Weeks);
        Assert.Null(draft.StartDate);
        Assert.Null(draft.EndDate);
        Assert.False(draft.SingleVisit);
        Assert.Null(draft.Notice);
    }

    [Theory]
    [InlineData("Monday, Wednesday, as well as Friday for one week")]
    [InlineData("I work on Monday, Wednesday and Friday for 1 week")]
    [InlineData("I need Monday, Wednesday and Friday for 1 week")]
    [InlineData("Each Monday, Wednesday and Friday for 1 week")]
    [InlineData("Monday, Wednesday and Friday weekly for 1 week")]
    public void OrdinaryGrammarWordsDoNotBecomeUnrecognizedCalendarClaims(string sentence)
    {
        var draft = ScheduleGrounding.Read(sentence);
        Assert.Equal("fallback", draft.Source);
        Assert.Equal(Mwf, draft.Weekdays);
        Assert.Equal(1, draft.Weeks);
        Assert.Null(draft.StartDate);
        Assert.Null(draft.EndDate);
        Assert.False(draft.SingleVisit);
        Assert.Null(draft.Notice);
    }

    [Theory]
    [InlineData("Monday, Wednesday and Friday from 10/26/2026 to 10/30/2026 for 4 visits")]
    [InlineData("Monday, Wednesday and Friday from 10/26/2026 to 10/30/2026 for 3 visits and 4 visits")]
    [InlineData("Monday, Wednesday and Friday from 10/26/2026 to 10/30/2026 for 0 visits")]
    [InlineData("Monday, Wednesday and Friday from 10/26/2026 to 10/30/2026 for -3 visits")]
    [InlineData("Monday, Wednesday and Friday from 10/26/2026 to 10/30/2026 for +3 visits")]
    [InlineData("Monday, Wednesday and Friday from 10/26/2026 to 10/30/2026 for 3.5 visits")]
    [InlineData("Monday, Wednesday and Friday from 10/26/2026 to 10/30/2026 for 30 visits")]
    [InlineData("Monday, Wednesday and Friday from 10/26/2026 to 11/30/2026 for thirty visits")]
    [InlineData("Monday, Wednesday and Friday from 10/26/2026 to 10/30/2026 for two hundred and three visits")]
    [InlineData("Monday, Wednesday and Friday from 10/26/2026 to 10/30/2026 for minus three visits")]
    [InlineData("Tuesdays and Thursdays from 10/26/2026 to 11/08/2026 for 3 visits per week")]
    [InlineData("Monday, Wednesday and Friday from 10/26/2026 to 11/08/2026 for 2 visits per week")]
    [InlineData("10/26/2026, Tuesday to 10/30/2026, Friday, as well as Wednesday for 3 visits")]
    [InlineData("10/26/2026, Monday to 10/30/2026, Thursday, as well as Wednesday for 3 visits")]
    [InlineData("10/26/2026, Monay to 10/30/2026, Thursday, as well as Wendesday for 3 visits")]
    [InlineData("Monday, Wednesday and Friday on 10/26/2026 and 10/30/2026 for 3 visits")]
    [InlineData("Monday, Wednesday and Friday from 10/30/2026 to 10/26/2026 for 3 visits")]
    [InlineData("Monday, Wednesday and Friday from 10/26/2026 to 10/30/2026 and 11/02/2026 for 3 visits")]
    [InlineData("Monday, Wednesday and Friday from 10/26/2026 to 10/30/2026 except Wednesday for 3 visits")]
    [InlineData("Monday, Wednesday and Friday from 10/26/2026 to 10/30/2026; I do not visit Wednesday")]
    [InlineData("Monday, Wednesday and Friday from 10/26/2026 to 10/30/2026; I don't visit Wednesday")]
    [InlineData("Monday, Wednesday and Friday from 10/26/2026 to 10/30/2026; I cannot attend Wednesday")]
    [InlineData("Monday, Wednesday and Friday from 10/26/2026 to 10/30/2026; it doesn't include Wednesday")]
    [InlineData("Monday, Wednesday and Friday from 10/26/2026 to 10/30/2026; it doesn’t include Wednesday")]
    [InlineData("Monday, Wednesday and Friday from 10/26/2026 to 10/30/2026; I didn't include Wednesday")]
    [InlineData("Maybe Monday, Wednesday and Friday from 10/26/2026 to 10/30/2026 for 3 visits")]
    [InlineData("Monday, Wednesday and Friday from October 26 to October 30, for 3 visits")]
    [InlineData("Monday, Wednesday and Friday from October 26, 2026 to November 30, 2026 for 3 visits")]
    [InlineData("Monday, Wednesday and Friday for 1 week from October 026, 2026 to October 030, 2026")]
    [InlineData("Monday, Wednesday and Friday for 1 week from October 026, 2026 to October 30, 2026")]
    [InlineData("Monday, Wednesday and Friday for 1 week from October 26, 2026 to October 030, 2026")]
    [InlineData("Monday, Wednesday and Friday from Tuesday 10/26/2026 to 10/30/2026")]
    [InlineData("Monday, Wednesday and Friday from Monday 10/26/2026 to Thursday 10/30/2026")]
    public void ContradictoryCountsAnnotationsNegationOrUnboundDatesRequireReview(string sentence)
    {
        var draft = ScheduleGrounding.Read(sentence);
        Assert.Empty(draft.Weekdays);
        Assert.Null(draft.Weeks);
        Assert.Null(draft.StartDate);
        Assert.Null(draft.EndDate);
        Assert.False(draft.SingleVisit);
        Assert.NotNull(draft.Notice);
    }

    [Theory]
    [InlineData("M/T from 10/26/2026 to 10/30/2026")]
    [InlineData("My report uses M/T and W/F for 1 week")]
    [InlineData("money, sundae and sundry from 10/26/2026 to 10/30/2026")]
    [InlineData("weak, seek, peek, meek, leek and weed for 1 week")]
    public void ShortUnknownLabelsAndUnrelatedWordsCannotInventAVisitWeekday(string sentence)
    {
        var draft = ScheduleGrounding.Read(sentence);
        Assert.Empty(draft.Weekdays);
        Assert.NotNull(draft.Notice);
        Assert.False(draft.SingleVisit);
    }

    [Theory]
    [InlineData("Maybe Monday, not sure when", "{\"weekdays\":[],\"weeks\":null}")]
    [InlineData("Monday and Wednesday", "{\"weekdays\":[\"Monday\",\"Wednesday\"],\"weeks\":null}")]
    [InlineData("From 10/26/2026 to 10/30/2026", "{\"weekdays\":[],\"weeks\":1}")]
    public async Task AnIncompleteMockedModelResponseIsLabeledAsNeedingReviewRatherThanSuccessfulExtraction(
        string sentence, string answer)
    {
        var handler = new AnswerHandler(answer);
        using var http = new HttpClient(handler);
        var reader = new TogetherVisitScheduleReader(http, Options.Create(new TogetherOptions
        {
            ApiKey = "offline-natural-language-placeholder-not-a-real-key",
            Model = TogetherOptions.SupportedModel
        }), NullLogger<TogetherVisitScheduleReader>.Instance,
            selection: Options.Create(new AiOptions { Provider = "together" }));
        var draft = await reader.ReadAsync(sentence);
        Assert.Equal(1, handler.Calls);
        Assert.Equal("together", draft.Source);
        Assert.NotNull(draft.Notice);
        Assert.Contains("Together AI responded", draft.SourceNote);
        Assert.Contains("needs your review", draft.SourceNote);
        Assert.DoesNotContain("Read by Together AI.", draft.SourceNote);
    }

    private sealed class AnswerHandler(string answer) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal("https://api.together.ai/v1/chat/completions", request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    model = TogetherOptions.SupportedModel,
                    choices = new[] { new { finish_reason = "stop", message = new { content = answer } } }
                }), Encoding.UTF8, "application/json")
            });
        }
    }
}
