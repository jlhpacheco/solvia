using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Solvia.Data;

namespace Solvia.Tests;

/// <summary>Real offline schedule reading/MVC at a UTC-versus-Pacific date boundary. No purchases or provider calls.</summary>
public sealed class PlannerRelativeDateIntegrationTests
{
    private const string Reported = "I want a parking permit today and tomorrow as well on wednesday. please. this is going to be a big week for me";
    private static readonly string[] Days = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

    private sealed class Clock : TimeProvider
    {
        // October26 on this UTC host is still October25,23:59 in California.
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 26, 6, 59, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class World : IDisposable
    {
        private readonly AppIntegrationTests.DemoFactory _base = new();
        private readonly IServiceScope _scope;
        private WebApplicationFactory<Program> Host { get; }
        public HttpClient Client { get; }
        public SolviaDbContext Db { get; }

        public World()
        {
            Host = _base.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
                services.AddSingleton<TimeProvider>(new Clock())));
            Client = Host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            _scope = Host.Services.CreateScope();
            Db = _scope.ServiceProvider.GetRequiredService<SolviaDbContext>();
        }

        public async Task<string> Get(string path = "/Planner") =>
            await (await Follow(await Client.GetAsync(path))).Content.ReadAsStringAsync();

        public async Task<HttpResponseMessage> Follow(HttpResponseMessage response)
        {
            for (var hop = 0; response.StatusCode == HttpStatusCode.Redirect && hop < 5; hop++)
                response = await Client.GetAsync(response.Headers.Location!);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return response;
        }

        public async Task<HttpResponseMessage> Post(string route, IEnumerable<KeyValuePair<string, string>> fields)
        {
            var html = await Get();
            var owner = Regex.Match(html, "data-person=\"([^\"]+)\"");
            Assert.True(owner.Success);
            return await Client.PostAsync(route, new FormUrlEncodedContent(fields.Append(
                new KeyValuePair<string, string>("draftOwner", WebUtility.HtmlDecode(owner.Groups[1].Value))).Append(
                new KeyValuePair<string, string>("__RequestVerificationToken", Value(Input(html, "name", "__RequestVerificationToken"))))));
        }

        public async Task SeedOldComparison()
        {
            var response = await Post("/Planner/Compare", OldPlan());
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            var html = await (await Follow(response)).Content.ReadAsStringAsync();
            Assert.Contains("2 visits. Two ways to plan.", html);
            Assert.Equal("2026-11-03", Value(Input(html, "id", "StartDate")));
            Assert.Equal("2026-11-08", Value(Input(html, "id", "EndDate")));
        }

        public async Task<string> Read(string text)
        {
            var response = await Post("/Planner/Read", OldPlan(text));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            return await (await Follow(response)).Content.ReadAsStringAsync();
        }

        public async Task<string> Compare(List<KeyValuePair<string, string>> fields)
        {
            var response = await Post("/Planner/Compare", fields);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            return await (await Follow(response)).Content.ReadAsStringAsync();
        }

        public void Dispose()
        {
            _scope.Dispose();
            Client.Dispose();
            Host.Dispose();
            _base.Dispose();
        }
    }

    private static string Input(string html, string attribute, string value)
    {
        var tag = Regex.Match(html, $"<input\\b[^>]*\\b{attribute}=\"{Regex.Escape(value)}\"[^>]*>", RegexOptions.IgnoreCase);
        Assert.True(tag.Success, $"Expected input {attribute}={value}.");
        return tag.Value;
    }

    private static string Value(string input)
    {
        var value = Regex.Match(input, "\\bvalue=\"([^\"]*)\"", RegexOptions.IgnoreCase);
        Assert.True(value.Success);
        return WebUtility.HtmlDecode(value.Groups[1].Value);
    }

    private static bool Checked(string input) => Regex.IsMatch(input, @"\bchecked(?:\s|=|>)", RegexOptions.IgnoreCase);

    private static List<KeyValuePair<string, string>> OldPlan(string text = "Old manually confirmed Tuesday and Thursday visits") =>
    [
        new("Campus", "Fresno City College"), new("StartDate", "2026-11-03"), new("EndDate", "2026-11-08"),
        new("SingleVisit", "false"), new("Term", "Fall"), new("ScheduleText", text),
        new("Weekdays", "Tuesday"), new("Weekdays", "Thursday")
    ];

    private static List<KeyValuePair<string, string>> RenderedPlan(string html, string text, bool single = false)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("Campus", "Fresno City College"), new("StartDate", Value(Input(html, "id", "StartDate"))),
            new("SingleVisit", single ? "true" : "false"), new("Term", "Fall"), new("ScheduleText", text)
        };
        if (!single)
        {
            fields.Add(new("EndDate", Value(Input(html, "id", "EndDate"))));
            fields.AddRange(Days.Where(day => Checked(Input(html, "id", "day-" + day)))
                .Select(day => new KeyValuePair<string, string>("Weekdays", day)));
        }
        return fields;
    }

    private static void AssertDraft(string html, string text, string start, string end, bool single, params string[] selected)
    {
        Assert.Equal(start, Value(Input(html, "id", "StartDate")));
        Assert.Equal(end, Value(Input(html, "id", "EndDate")));
        Assert.Contains(text, WebUtility.HtmlDecode(html));
        Assert.Contains("data-source=\"fallback\"", html);
        Assert.Equal(single, Checked(Input(html, "id", "visit-single")));
        Assert.Equal(!single, Checked(Input(html, "id", "visit-repeating")));
        foreach (var day in Days)
            Assert.Equal(selected.Contains(day), Checked(Input(html, "id", "day-" + day)));
        Assert.DoesNotContain("name=\"Weeks\"", html);
    }

    private static async Task<(int Applications, int Purchases, int Memberships, int Orders, int Captures, int Receipts)> State(SolviaDbContext db) =>
        (await db.PermitApplications.CountAsync(), await db.PrepaidPurchases.CountAsync(),
            await db.PrepaidPurchaseItems.CountAsync(),
            await db.PermitApplications.CountAsync(row => row.PayPalOrderId != null) + await db.PrepaidPurchases.CountAsync(row => row.PayPalOrderId != null),
            await db.PermitApplications.CountAsync(row => row.PayPalCaptureId != null) + await db.PrepaidPurchases.CountAsync(row => row.PayPalCaptureId != null),
            await db.PermitApplications.CountAsync(row => row.ReceiptNumber != null));

    [Fact]
    public async Task PacificTodayAtUtcMidnightReadsAndOffersOnlySunday25Monday26AndWednesday28()
    {
        using var world = new World();
        var before = await State(world.Db);
        var initial = await world.Get();
        Assert.Equal("2026-10-25", Value(Input(initial, "id", "StartDate")));
        await world.SeedOldComparison();
        var html = await world.Read(Reported);
        AssertDraft(html, Reported, "2026-10-25", "2026-10-28", false, "Sunday", "Monday", "Wednesday");
        Assert.DoesNotContain("data-testid=\"plan-result\"", html);
        var fields = RenderedPlan(html, Reported);
        html = await world.Compare(fields);
        Assert.Contains("3 visits. Two ways to plan.", html);
        Assert.Contains("$3.00", html);
        Assert.Contains("$30.00", html);
        Assert.DoesNotContain("id=\"planner-errors\"", html);
        foreach (var label in new[] { "Sun, Oct 25, 2026", "Mon, Oct 26, 2026", "Wed, Oct 28, 2026" })
            Assert.Contains(label, html);

        fields.Add(new("comparedPlan", Value(Input(html, "name", "comparedPlan"))));
        fields.Add(new("choice", "Daily"));
        var continuation = await world.Post("/Planner/Continue", fields);
        Assert.Equal(HttpStatusCode.Redirect, continuation.StatusCode);
        Assert.StartsWith("/Prepaid/Choose?plan=", continuation.Headers.Location!.OriginalString);
        var choices = await world.Get(continuation.Headers.Location.OriginalString);
        var offered = Regex.Matches(choices, "name=\"selectedDates\"[^>]*value=\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value).ToArray();
        Assert.Equal(new[] { "2026-10-25", "2026-10-26", "2026-10-28" }, offered);
        for (var refresh = 0; refresh < 2; refresh++)
            AssertDraft(await world.Get(), Reported, "2026-10-25", "2026-10-28", false, "Sunday", "Monday", "Wednesday");
        Assert.Equal(before, await State(world.Db));
    }

    [Fact]
    public async Task TomorrowAloneReplacesOldRepeatingDraftWithOneMonday26VisitAndPreservesItsContinuation()
    {
        const string sentence = "tomorrow";
        using var world = new World();
        var before = await State(world.Db);
        await world.SeedOldComparison();
        var html = await world.Read(sentence);
        AssertDraft(html, sentence, "2026-10-26", "", true, "Monday");
        Assert.DoesNotContain("data-testid=\"plan-result\"", html);
        var fields = RenderedPlan(html, sentence, single: true);
        html = await world.Compare(fields);
        Assert.Contains("1 visit. Two ways to plan.", html);
        Assert.Contains("$1.00", html);
        Assert.Contains("$30.00", html);
        Assert.DoesNotContain("id=\"planner-errors\"", html);
        AssertDraft(html, sentence, "2026-10-26", "", true, "Monday");
        fields.Add(new("comparedPlan", Value(Input(html, "name", "comparedPlan"))));
        fields.Add(new("choice", "Daily"));
        var continuation = await world.Post("/Planner/Continue", fields);
        Assert.Equal(HttpStatusCode.Redirect, continuation.StatusCode);
        Assert.StartsWith("/Permits/Questions?", continuation.Headers.Location!.OriginalString);
        Assert.Contains("startDate=2026-10-26", continuation.Headers.Location.OriginalString);
        Assert.Contains("permitType=Daily", continuation.Headers.Location.OriginalString);
        var questions = await world.Get(continuation.Headers.Location.OriginalString);
        Assert.Equal("2026-10-26", Value(Input(questions, "name", "startDate")));
        for (var refresh = 0; refresh < 2; refresh++)
            AssertDraft(await world.Get(), sentence, "2026-10-26", "", true, "Monday");
        Assert.Equal(before, await State(world.Db));
    }

    [Fact]
    public async Task ContradictoryThreeVisitClaimForTodayAndTomorrowClearsOldSuggestionsAndCannotCompareThem()
    {
        const string sentence = "today and tomorrow for 3 visits";
        using var world = new World();
        var before = await State(world.Db);
        await world.SeedOldComparison();
        var html = await world.Read(sentence);
        AssertDraft(html, sentence, "2026-11-03", "", false);
        var note = Regex.Match(html, "<p[^>]*data-testid=\"schedule-reader\"[^>]*>(.*?)</p>", RegexOptions.Singleline);
        Assert.True(note.Success);
        Assert.Contains("needs your review", WebUtility.HtmlDecode(note.Groups[1].Value));
        Assert.DoesNotContain("Read by", WebUtility.HtmlDecode(note.Groups[1].Value));
        Assert.DoesNotContain("data-testid=\"plan-result\"", html);
        // Server validation must still hold when native required fields are bypassed.
        var rejected = await world.Post("/Planner/Compare", RenderedPlan(html, sentence));
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        html = await rejected.Content.ReadAsStringAsync();
        Assert.Contains("id=\"planner-errors\"", html);
        Assert.DoesNotContain("data-testid=\"plan-result\"", html);
        AssertDraft(html, sentence, "2026-11-03", "", false);
        AssertDraft(await world.Get(), sentence, "2026-11-03", "", false);
        Assert.Equal(before, await State(world.Db));
    }
}
