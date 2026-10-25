using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Solvia.Data;

namespace Solvia.Tests;

/// <summary>Actual offline reader, MVC/protected drafts and exact prepaid choices; no model/payment HTTP or purchases.</summary>
public sealed class PlannerNaturalLanguageIntegrationTests
{
    private const string Reported = "10/26/2026 , monday to 10/30/2026, Friday. for 3 visits, as well as Wednesday";
    private const string Typos = "Monay, Wendesday and Firday from 10/26/2026 until the end of Novemeber 2026";
    private static readonly string[] Days = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static async Task<HttpResponseMessage> Follow(HttpClient client, HttpResponseMessage response)
    {
        for (var hop = 0; response.StatusCode == HttpStatusCode.Redirect && hop < 5; hop++)
            response = await client.GetAsync(response.Headers.Location!);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return response;
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

    private static async Task<HttpResponseMessage> Post(HttpClient client, string route,
        IEnumerable<KeyValuePair<string, string>> fields)
    {
        var page = await Follow(client, await client.GetAsync("/Planner"));
        var html = await page.Content.ReadAsStringAsync();
        var owner = Regex.Match(html, "data-person=\"([^\"]+)\"");
        Assert.True(owner.Success);
        return await client.PostAsync(route, new FormUrlEncodedContent(fields.Append(
            new KeyValuePair<string, string>("draftOwner", WebUtility.HtmlDecode(owner.Groups[1].Value))).Append(
            new KeyValuePair<string, string>("__RequestVerificationToken", Value(Input(html, "name", "__RequestVerificationToken"))))));
    }

    private static List<KeyValuePair<string, string>> OldPlan(string text = "Previous manually confirmed Tuesday and Thursday plan") =>
    [
        new("Campus", "Fresno City College"), new("StartDate", "2026-10-27"), new("EndDate", "2026-11-01"),
        new("SingleVisit", "false"), new("Term", "Fall"), new("ScheduleText", text),
        new("Weekdays", "Tuesday"), new("Weekdays", "Thursday")
    ];

    private static List<KeyValuePair<string, string>> RenderedPlan(string html, string text)
    {
        // These are exactly the date-only controls a no-JavaScript form sends;
        // no internal Weeks value or reader provenance is supplied by the test.
        var fields = new List<KeyValuePair<string, string>>
        {
            new("Campus", "Fresno City College"), new("StartDate", Value(Input(html, "id", "StartDate"))),
            new("EndDate", Value(Input(html, "id", "EndDate"))), new("SingleVisit", "false"),
            new("Term", "Fall"), new("ScheduleText", text)
        };
        fields.AddRange(Days.Where(day => Checked(Input(html, "id", "day-" + day)))
            .Select(day => new KeyValuePair<string, string>("Weekdays", day)));
        return fields;
    }

    private static void AssertDraft(string html, string text, string start, string end, bool selectedMwf)
    {
        Assert.Equal(start, Value(Input(html, "id", "StartDate")));
        Assert.Equal(end, Value(Input(html, "id", "EndDate")));
        Assert.Contains(text, WebUtility.HtmlDecode(html));
        Assert.Contains("data-source=\"fallback\"", html);
        Assert.True(Checked(Input(html, "id", "visit-repeating")));
        Assert.False(Checked(Input(html, "id", "visit-single")));
        foreach (var day in Days)
            Assert.Equal(selectedMwf && (day is "Monday" or "Wednesday" or "Friday"), Checked(Input(html, "id", "day-" + day)));
        Assert.DoesNotContain("name=\"Weeks\"", html);
    }

    private static async Task<(int Applications, int Purchases, int Memberships, int Orders, int Captures, int Receipts)> State(SolviaDbContext db) =>
        (await db.PermitApplications.CountAsync(), await db.PrepaidPurchases.CountAsync(),
            await db.PrepaidPurchaseItems.CountAsync(),
            await db.PermitApplications.CountAsync(row => row.PayPalOrderId != null) + await db.PrepaidPurchases.CountAsync(row => row.PayPalOrderId != null),
            await db.PermitApplications.CountAsync(row => row.PayPalCaptureId != null) + await db.PrepaidPurchases.CountAsync(row => row.PayPalCaptureId != null),
            await db.PermitApplications.CountAsync(row => row.ReceiptNumber != null));

    [Theory]
    [InlineData(Reported, "2026-10-30", 3)]
    [InlineData(Typos, "2026-11-30", 16)]
    public async Task NaturalLanguageReplacesOldDraftAndOffersOnlyTheExactGroundedDates(string sentence, string end, int count)
    {
        using var factory = new AppIntegrationTests.DemoFactory();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<TimeProvider>(new Clock())));
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SolviaDbContext>();
        var before = await State(db);

        var prior = await Post(client, "/Planner/Compare", OldPlan());
        var oldHtml = await (await Follow(client, prior)).Content.ReadAsStringAsync();
        Assert.Contains("2 visits. Two ways to plan.", oldHtml);
        var read = await Post(client, "/Planner/Read", OldPlan(sentence));
        Assert.Equal(HttpStatusCode.Redirect, read.StatusCode);
        var html = await (await Follow(client, read)).Content.ReadAsStringAsync();
        AssertDraft(html, sentence, "2026-10-26", end, selectedMwf: true);
        Assert.DoesNotContain("data-testid=\"plan-result\"", html);

        var fields = RenderedPlan(html, sentence);
        var comparison = await Post(client, "/Planner/Compare", fields);
        Assert.Equal(HttpStatusCode.Redirect, comparison.StatusCode);
        html = await (await Follow(client, comparison)).Content.ReadAsStringAsync();
        AssertDraft(html, sentence, "2026-10-26", end, selectedMwf: true);
        Assert.Contains($"{count} visits. Two ways to plan.", html);
        Assert.Contains($"${count}.00", html);
        Assert.Contains("$30.00", html);
        Assert.DoesNotContain("id=\"planner-errors\"", html);

        fields.Add(new("comparedPlan", Value(Input(html, "name", "comparedPlan"))));
        fields.Add(new("choice", "Daily"));
        var continuation = await Post(client, "/Planner/Continue", fields);
        Assert.Equal(HttpStatusCode.Redirect, continuation.StatusCode);
        Assert.StartsWith("/Prepaid/Choose?plan=", continuation.Headers.Location!.OriginalString);
        var choices = await client.GetStringAsync(continuation.Headers.Location);
        var expected = new List<string>();
        for (var date = new DateOnly(2026, 10, 26); date <= DateOnly.ParseExact(end, "yyyy-MM-dd", CultureInfo.InvariantCulture); date = date.AddDays(1))
            if (date.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Wednesday or DayOfWeek.Friday)
                expected.Add(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Assert.Equal(count, expected.Count);
        if (count == 3) Assert.Equal(new[] { "2026-10-26", "2026-10-28", "2026-10-30" }, expected);
        var offered = Regex.Matches(choices, "name=\"selectedDates\"[^>]*value=\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value).ToArray();
        Assert.Equal(expected, offered);

        for (var refresh = 0; refresh < 2; refresh++)
        {
            var restored = await client.GetStringAsync("/Planner");
            AssertDraft(restored, sentence, "2026-10-26", end, selectedMwf: true);
        }
        Assert.Equal(before, await State(db));
    }

    [Fact]
    public async Task ConflictingVisitCountClearsTheOldEndAndWeekdaysAndCannotReuseItsSuccessfulComparison()
    {
        const string sentence = "10/26/2026 , monday to 10/30/2026, Friday. for 4 visits, as well as Wednesday";
        using var factory = new AppIntegrationTests.DemoFactory();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<TimeProvider>(new Clock())));
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SolviaDbContext>();
        var before = await State(db);
        var prior = await Post(client, "/Planner/Compare", OldPlan());
        var oldHtml = await (await Follow(client, prior)).Content.ReadAsStringAsync();
        Assert.Contains("2 visits. Two ways to plan.", oldHtml);

        var read = await Post(client, "/Planner/Read", OldPlan(sentence));
        Assert.Equal(HttpStatusCode.Redirect, read.StatusCode);
        var html = await (await Follow(client, read)).Content.ReadAsStringAsync();
        AssertDraft(html, sentence, "2026-10-27", "", selectedMwf: false);
        var note = Regex.Match(html, "<p[^>]*data-testid=\"schedule-reader\"[^>]*>(.*?)</p>", RegexOptions.Singleline);
        Assert.True(note.Success);
        Assert.Contains("needs your review", WebUtility.HtmlDecode(note.Groups[1].Value));
        Assert.DoesNotContain("Read by", WebUtility.HtmlDecode(note.Groups[1].Value));
        Assert.DoesNotContain("data-testid=\"plan-result\"", html);

        // Even a client bypassing native required-field checks cannot compare
        // the old duration or weekdays: the server must reject these blanks.
        var rejected = await Post(client, "/Planner/Compare", RenderedPlan(html, sentence));
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        html = await rejected.Content.ReadAsStringAsync();
        Assert.Contains("id=\"planner-errors\"", html);
        Assert.DoesNotContain("data-testid=\"plan-result\"", html);
        AssertDraft(html, sentence, "2026-10-27", "", selectedMwf: false);
        var restored = await client.GetStringAsync("/Planner");
        AssertDraft(restored, sentence, "2026-10-27", "", selectedMwf: false);
        Assert.Equal(before, await State(db));
    }
}
