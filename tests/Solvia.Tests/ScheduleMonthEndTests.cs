using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Solvia.Config;
using Solvia.Services;

namespace Solvia.Tests;

/// <summary>Public reader contract; all HTTP is an in-memory stub, never a hosted model call.</summary>
public sealed class ScheduleMonthEndTests
{
    private const string ReportedSentence =
        "i am coming over monday 10/26/2026 until the end of november 2026, every monday , Wednesday and Friday. ok";
    private static readonly DayOfWeek[] Mwf = [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday];

    private sealed class Handler(string? answer = null) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://api.together.ai/v1/chat/completions", request.RequestUri!.AbsoluteUri);
            if (answer is null) throw new InvalidOperationException("Offline grounding must not send any HTTP request.");
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

    private static TogetherVisitScheduleReader Reader(HttpClient http, bool mockedProvider = false) => new(http,
        Options.Create(new TogetherOptions
        {
            ApiKey = mockedProvider ? "offline-month-end-placeholder-not-a-real-key" : "",
            Model = TogetherOptions.SupportedModel
        }), NullLogger<TogetherVisitScheduleReader>.Instance, selection: Options.Create(new AiOptions { Provider = "together" }));

    private static DateOnly Date(string value) => DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    [Fact]
    public async Task ReportedNamedMonthEnd_PreservesOctober26ThroughNovember30AndSixInclusiveWeeks()
    {
        var handler = new Handler();
        using var http = new HttpClient(handler);
        var result = await Reader(http).ReadAsync(ReportedSentence);
        Assert.Equal("fallback", result.Source);
        Assert.Equal(Mwf, result.Weekdays);
        Assert.Equal(Date("2026-10-26"), result.StartDate);
        Assert.Equal(Date("2026-11-30"), result.EndDate);
        Assert.Equal(6, result.Weeks);
        Assert.False(result.SingleVisit);
        Assert.Null(result.Notice);
        var dates = Enumerable.Range(0, result.EndDate!.Value.DayNumber - result.StartDate!.Value.DayNumber + 1)
            .Select(offset => result.StartDate.Value.AddDays(offset)).Where(date => result.Weekdays.Contains(date.DayOfWeek));
        Assert.Equal(16, dates.Count());
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("Mondays from 10/26/2026 through the end of November 2026", "2026-10-26", "2026-11-30", 6)]
    [InlineData("Mondays from 2026-10-26 to end of NOVEMBER", "2026-10-26", "2026-11-30", 6)]
    [InlineData("Every Monday 10/26/2026 until the end of the month", "2026-10-26", "2026-10-31", 1)]
    [InlineData("Mondays from 2026-09-01 through end of Sept. 2026", "2026-09-01", "2026-09-30", 5)]
    [InlineData("Mondays from 2026-09-01 until end of Sep 2026", "2026-09-01", "2026-09-30", 5)]
    [InlineData("Mondays from 2028-02-01 until the end of February 2028", "2028-02-01", "2028-02-29", 5)]
    [InlineData("Mondays from 2027-02-01 until the end of Feb. 2027", "2027-02-01", "2027-02-28", 4)]
    [InlineData("Mondays from 2100-02-01 until end of February 2100", "2100-02-01", "2100-02-28", 4)]
    [InlineData("Mondays from 2000-02-01 until end of February 2000", "2000-02-01", "2000-02-29", 5)]
    [InlineData("Every Monday from 2026-12-14 until end of January 2027", "2026-12-14", "2027-01-31", 7)]
    [InlineData("Mondays from 2026-12-14 until end of Jan. 2027", "2026-12-14", "2027-01-31", 7)]
    [InlineData("Mondays from 2027-04-01 to end of April for five weeks", "2027-04-01", "2027-04-30", 5)]
    [InlineData("Mondays from 2028-01-01 through end of June 2028", "2028-01-01", "2028-06-30", 26)]
    [InlineData("Mondays from 2026-12-14 until end of January, 2027", "2026-12-14", "2027-01-31", 7)]
    [InlineData("Mondays from 2026-12-14 until end of January of 2027", "2026-12-14", "2027-01-31", 7)]
    public async Task AnchoredMonthEnds_UseCalendarLengthAndTheStatedOrAnchoredYear(
        string sentence, string start, string end, int weeks)
    {
        var handler = new Handler();
        using var http = new HttpClient(handler);
        var result = await Reader(http).ReadAsync(sentence);
        Assert.Equal(Date(start), result.StartDate);
        Assert.Equal(Date(end), result.EndDate);
        Assert.Equal(weeks, result.Weeks);
        Assert.Equal(new[] { DayOfWeek.Monday }, result.Weekdays);
        Assert.False(result.SingleVisit);
        Assert.Null(result.Notice);
        Assert.Equal("fallback", result.Source);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("Mondays until the end of November 2026")]
    [InlineData("Mondays until the end of month")]
    [InlineData("Mondays from 10/26 until the end of November 2026")]
    [InlineData("Mondays from 12/14/2026 until end of January")]
    [InlineData("Mondays from 12/14/2026 until end of January 2026")]
    [InlineData("Mondays from 10/26/2026 until end of November 2026 for five weeks")]
    [InlineData("Mondays from 10/26/2026 until end of November 2026 and end of December 2026")]
    [InlineData("Mondays from 10/26/2026 until end of November 2026 and 12/01/2026")]
    [InlineData("Mondays from 10/26/2026 until end of November 2026 and 11/30")]
    [InlineData("Mondays from 10/26/2026 until end of November 2026.5")]
    [InlineData("Mondays from 10/26/2026 until end of the month 2027")]
    [InlineData("Mondays from 10/26/2026 until end of November, 2027")]
    [InlineData("Mondays from 10/26/2026 until end of November of 2027")]
    [InlineData("Mondays from 10/26/2026 until end of November 2026, 2027")]
    [InlineData("Mondays from 10/26/2026 until end of November 2026 and 2027")]
    [InlineData("Mondays from 10/26/2026 until end of November next year")]
    [InlineData("Mondays from 10/26/2026 until end of November, next year")]
    [InlineData("Mondays from 10/26/2026 until end of November 2026 through December 2026")]
    [InlineData("Mondays from 10/26/2026 until end of Smarch 2026")]
    [InlineData("Mondays from 10/26/2026 until end of November 20270")]
    [InlineData("Mondays from 10/26/2026 until end of November 0000")]
    [InlineData("Mondays from 10/26/2026 until end of November 26")]
    [InlineData("Mondays from 10/26/2026 until end of Novemberish 2026")]
    [InlineData("Mondays from 02/30/2028 until end of March 2028")]
    [InlineData("Mondays from 2028-02-01 until end of February 2027")]
    [InlineData("Mondays from 2026-01-01 until end of July 2026")]
    [InlineData("One visit Monday 10/26/2026 until end of November 2026")]
    [InlineData("Maybe Mondays from 10/26/2026 until end of November 2026")]
    [InlineData("Mondays 10/26/2026 and end of November 2026")]
    public async Task MissingAmbiguousContradictoryOrOverlongMonthRanges_RequireNewConfirmedDates(string sentence)
    {
        var handler = new Handler();
        using var http = new HttpClient(handler);
        var result = await Reader(http).ReadAsync(sentence);
        Assert.Null(result.StartDate);
        Assert.Null(result.EndDate);
        Assert.Null(result.Weeks);
        Assert.Empty(result.Weekdays);
        Assert.False(result.SingleVisit);
        Assert.Equal("fallback", result.Source);
        Assert.NotNull(result.Notice);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task MatchingMockedProvider_UsesOnlyGroundedMonthEndDatesAndHonestSource()
    {
        var handler = new Handler("""{"weekdays":["Monday","Wednesday","Friday"],"weeks":6}""");
        using var http = new HttpClient(handler);
        var result = await Reader(http, mockedProvider: true).ReadAsync(ReportedSentence);
        Assert.Equal("together", result.Source);
        Assert.Equal(Date("2026-10-26"), result.StartDate);
        Assert.Equal(Date("2026-11-30"), result.EndDate);
        Assert.Equal(6, result.Weeks);
        Assert.Equal(Mwf, result.Weekdays);
        Assert.False(result.SingleVisit);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("{\"weekdays\":[\"Monday\",\"Wednesday\",\"Friday\"],\"weeks\":5}")]
    [InlineData("{\"weekdays\":[\"Monday\",\"Wednesday\",\"Friday\"],\"weeks\":6,\"endDate\":\"2099-11-30\"}")]
    public async Task UngroundedMockedProvider_CannotOverrideTheCalendarBound(string answer)
    {
        var handler = new Handler(answer);
        using var http = new HttpClient(handler);
        var result = await Reader(http, mockedProvider: true).ReadAsync(ReportedSentence);
        Assert.Equal("fallback", result.Source);
        Assert.Equal(Date("2026-10-26"), result.StartDate);
        Assert.Equal(Date("2026-11-30"), result.EndDate);
        Assert.Equal(6, result.Weeks);
        Assert.Equal(Mwf, result.Weekdays);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task MockedProvider_CannotInventAStartOrDurationForAnUnanchoredMonthEnd()
    {
        var handler = new Handler("""{"weekdays":["Monday"],"weeks":4}""");
        using var http = new HttpClient(handler);
        var result = await Reader(http, mockedProvider: true).ReadAsync("Every Monday until end of November 2026");
        Assert.Equal("fallback", result.Source);
        Assert.Null(result.StartDate);
        Assert.Null(result.EndDate);
        Assert.Null(result.Weeks);
        Assert.Empty(result.Weekdays);
        Assert.Equal(1, handler.Calls);
    }
}
