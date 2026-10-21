using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Solvia.Services;

namespace Solvia.Tests;

public sealed class PlannerDraftRegressionTests : IClassFixture<AppIntegrationTests.DemoFactory>
{
    private readonly HttpClient _client;
    private readonly CountingReader _reader = new();

    public PlannerDraftRegressionTests(AppIntegrationTests.DemoFactory factory)
    {
        var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<IVisitScheduleReader>(_reader)));
        _client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    private sealed class CountingReader : IVisitScheduleReader
    {
        public int Calls;
        public Task<VisitScheduleDraft> ReadAsync(string text, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new VisitScheduleDraft([DayOfWeek.Tuesday], 4, "fallback", null));
        }
    }

    private static Dictionary<string, string> Fields(string text = "") => new()
    {
        ["Campus"] = "CSU East Bay", ["StartDate"] = $"{DateTime.Now.Year + 1}-01-20",
        ["Weeks"] = "4", ["Weekdays"] = "Wednesday", ["Term"] = "Spring", ["ScheduleText"] = text
    };

    private async Task<string> Post(string route, Dictionary<string, string> fields, string page = "/Planner")
    {
        var html = await _client.GetStringAsync(page);
        fields["__RequestVerificationToken"] = WebUtility.HtmlDecode(Regex.Match(html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        fields.TryAdd("draftOwner", Regex.Match(html, "data-person=\"([^\"]+)\"").Groups[1].Value);
        var response = await _client.PostAsync(route, new FormUrlEncodedContent(fields));
        if (response.StatusCode == HttpStatusCode.Redirect)
        {
            Assert.StartsWith("/Planner", response.Headers.Location!.OriginalString);
            response = await _client.GetAsync(response.Headers.Location);
        }
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankOptionalReadPreservesManualPicksWithoutCallingReader(string text)
    {
        var html = await Post("/Planner/Read", Fields(text));
        Assert.Equal(0, _reader.Calls);
        Assert.Contains("No description is needed", html);
        Assert.DoesNotContain("planner-errors", html);
        Assert.Contains("value=\"CSU East Bay\" selected=\"selected\"", html);
        Assert.Contains("value=\"Wednesday\" checked=\"checked\"", html);
        Assert.Contains("value=\"4\"", html);
        var comparison = await Post("/Planner/Compare", Fields(text));
        Assert.Contains("data-testid=\"plan-result\"", comparison);
        Assert.Equal(0, _reader.Calls);
    }

    [Fact]
    public async Task ReturningToPlannerKeepsSubmittedDescriptionAndConfirmedSchedule()
    {
        await Post("/Planner/Compare", Fields("Wednesdays for four weeks"));
        var html = await _client.GetStringAsync("/Planner");
        Assert.Contains("Wednesdays for four weeks</textarea>", html);
        Assert.Contains("value=\"CSU East Bay\" selected=\"selected\"", html);
        Assert.Contains("value=\"Wednesday\" checked=\"checked\"", html);
        Assert.Contains($"value=\"{DateTime.Now.Year + 1}-01-20\"", html);
        Assert.DoesNotContain("data-testid=\"plan-result\"", html);
        // Reading the draft must not consume it on the first return.
        Assert.Contains("Wednesdays for four weeks</textarea>", await _client.GetStringAsync("/Planner"));
    }

    [Fact]
    public async Task MaximumLengthUnicodeDescriptionRoundTripsThroughTheProtectedDraft()
    {
        var text = string.Concat(Enumerable.Range(0x400, 500).Select(value => (char)value));
        await Post("/Planner/Compare", Fields(text));
        Assert.Contains(text, WebUtility.HtmlDecode(await _client.GetStringAsync("/Planner")));
        Assert.Equal(0, _reader.Calls);
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("Tuesdays for four weeks", 1)]
    public async Task ReadingRedirectsToAGetThatCanBeRefreshedWithoutRepeatingInference(string text, int calls)
    {
        var form = await _client.GetStringAsync("/Planner");
        var fields = Fields(text);
        fields["draftOwner"] = Regex.Match(form, "data-person=\"([^\"]+)\"").Groups[1].Value;
        fields["__RequestVerificationToken"] = WebUtility.HtmlDecode(Regex.Match(form,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        var response = await _client.PostAsync("/Planner/Read", new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(calls, _reader.Calls);
        for (var refresh = 0; refresh < 2; refresh++)
        {
            var html = await _client.GetStringAsync(response.Headers.Location!);
            Assert.Contains("data-testid=\"schedule-reader\"", html);
            Assert.Contains("value=\"4\"", html);
            Assert.Equal(calls, _reader.Calls);
        }
    }

    [Fact]
    public async Task ComparisonRetainsProtectedReaderStatusOnlyForTheUnchangedSentence()
    {
        await Post("/Planner/Read", Fields("Tuesdays for four weeks"));
        var html = await Post("/Planner/Compare", Fields("Tuesdays for four weeks"));
        Assert.Contains("data-source=\"fallback\"", html);
        Assert.Equal(1, _reader.Calls);
        var changed = Fields("A different sentence that has not been read");
        changed["ReaderSource"] = "together";
        html = await Post("/Planner/Compare", changed);
        Assert.DoesNotContain("data-testid=\"schedule-reader\"", html);
        Assert.DoesNotContain("data-source=\"together\"", html);
        Assert.Equal(1, _reader.Calls);
    }

    [Fact]
    public async Task PlannerDraftDoesNotTransferToAnotherDemoPerson()
    {
        await Post("/Planner/Compare", Fields("Private fictional schedule for Maria"));
        var home = await _client.GetStringAsync("/");
        var token = WebUtility.HtmlDecode(Regex.Match(home,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        var switched = await _client.PostAsync("/Home/SwitchStudent", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["id"] = "elena", ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, switched.StatusCode);
        var html = await _client.GetStringAsync("/Planner");
        Assert.DoesNotContain("Private fictional schedule for Maria", html);
        Assert.Contains("Elena", html);
    }

    [Fact]
    public async Task ReadingASentenceDoesNotClearUnrelatedDateValidation()
    {
        var fields = Fields("Tuesdays for four weeks");
        fields["StartDate"] = "not-a-date";
        var html = await Post("/Planner/Read", fields);
        Assert.Equal(1, _reader.Calls);
        Assert.Contains("not-a-date", html);
        Assert.Contains("planner-errors", html);
        Assert.Contains("value=\"Tuesday\" checked=\"checked\"", html);
    }

    [Fact]
    public async Task ChangedOrMissingComparisonCannotContinueAnOldSnapshot()
    {
        var fields = Fields("Wednesdays for four weeks");
        var comparison = await Post("/Planner/Compare", fields);
        fields["comparedPlan"] = Regex.Match(comparison, "name=\"comparedPlan\" value=\"([^\"]+)\"").Groups[1].Value;
        fields["Weeks"] = "5";
        fields["choice"] = "Daily";
        var html = await Post("/Planner/Continue", fields);
        Assert.Contains("Your plan changed", html);
        Assert.Contains("5 visits. Two ways to plan.", html);
        Assert.Contains("Wednesdays for four weeks</textarea>", html);
        fields.Remove("comparedPlan");
        Assert.Contains("Your plan changed", await Post("/Planner/Continue", fields));
    }

    [Theory]
    [InlineData("/Planner/Read")]
    [InlineData("/Planner/Compare")]
    [InlineData("/Planner/Continue")]
    public async Task EmptyCampusProducesValidationInsteadOfAnException(string route)
    {
        var fields = Fields();
        fields["Campus"] = "";
        var html = await Post(route, fields);
        Assert.Contains("planner-errors", html);
        Assert.DoesNotContain("unhandled exception", html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/Planner/Read")]
    [InlineData("/Planner/Compare")]
    [InlineData("/Planner/Continue")]
    public async Task StalePersonPostCannotSaveThePreviousPersonsEntries(string route)
    {
        var html = await _client.GetStringAsync("/Planner");
        var fields = Fields("This must not become the next person's draft");
        fields["draftOwner"] = "elena"; // Form owner differs from current Maria.
        fields["__RequestVerificationToken"] = WebUtility.HtmlDecode(Regex.Match(html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        var response = await _client.PostAsync(route, new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        html = await _client.GetStringAsync(response.Headers.Location!);
        Assert.Contains("The demo person changed", html);
        Assert.DoesNotContain("This must not become", html);
        Assert.Equal(0, _reader.Calls);
    }
}
