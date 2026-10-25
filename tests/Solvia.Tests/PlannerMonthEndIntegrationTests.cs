using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Solvia.Data;

namespace Solvia.Tests;

/// <summary>Actual offline reader, MVC binding and protected month-end draft persistence.</summary>
public sealed class PlannerMonthEndIntegrationTests
{
    private const string Sentence = "i am coming over monday 10/26/2026 until the end of november 2026, every monday , Wednesday and Friday. ok";

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static async Task<HttpResponseMessage> Follow(HttpClient client, HttpResponseMessage response)
    {
        for (var attempt = 0; response.StatusCode == HttpStatusCode.Redirect && attempt < 5; attempt++)
            response = await client.GetAsync(response.Headers.Location!);
        response.EnsureSuccessStatusCode();
        return response;
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string path,
        IEnumerable<KeyValuePair<string, string>> fields)
    {
        var page = await Follow(client, await client.GetAsync("/Planner"));
        var html = await page.Content.ReadAsStringAsync();
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        var owner = Regex.Match(html, "data-person=\"([^\"]+)\"");
        Assert.True(token.Success);
        Assert.True(owner.Success);
        return await client.PostAsync(path, new FormUrlEncodedContent(fields.Append(
            new KeyValuePair<string, string>("draftOwner", owner.Groups[1].Value)).Append(
            new KeyValuePair<string, string>("__RequestVerificationToken", WebUtility.HtmlDecode(token.Groups[1].Value)))));
    }

    [Theory]
    [InlineData("")]
    [InlineData("2026-11-01")]
    public async Task ExactMonthEndSentenceReplacesBlankOrStaleEndAndOffersOnlyIts16Dates(string oldEnd)
    {
        using var factory = new AppIntegrationTests.DemoFactory();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<TimeProvider>(new Clock())));
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SolviaDbContext>();
        var applicationsBefore = await db.PermitApplications.CountAsync();
        var purchasesBefore = await db.PrepaidPurchases.CountAsync();
        var fields = new List<KeyValuePair<string, string>>
        {
            new("Campus", "Fresno City College"), new("StartDate", "2026-10-27"), new("EndDate", oldEnd),
            new("SingleVisit", "false"), new("Term", "Fall"), new("ScheduleText", Sentence),
            new("Weekdays", "Tuesday"), new("Weekdays", "Thursday")
        };
        var read = await Post(client, "/Planner/Read", fields);
        Assert.Equal(HttpStatusCode.Redirect, read.StatusCode);
        var html = await (await Follow(client, read)).Content.ReadAsStringAsync();
        Assert.Matches("id=\"StartDate\"[^>]*value=\"2026-10-26\"", html);
        Assert.Matches("id=\"EndDate\"[^>]*value=\"2026-11-30\"", html);
        Assert.Contains("data-source=\"fallback\"", html);
        Assert.Contains("data-single-visit=\"false\"", html);
        foreach (var day in new[] { "Monday", "Wednesday", "Friday" })
            Assert.Contains($"value=\"{day}\" checked=\"checked\"", html);
        foreach (var day in new[] { "Tuesday", "Thursday", "Saturday", "Sunday" })
            Assert.DoesNotContain($"value=\"{day}\" checked=\"checked\"", html);
        Assert.DoesNotContain("name=\"Weeks\"", html);

        // Submit exactly what a no-JavaScript date-only form can send.
        fields.RemoveAll(field => field.Key is "StartDate" or "EndDate" or "Weekdays");
        fields.Add(new("StartDate", "2026-10-26"));
        fields.Add(new("EndDate", "2026-11-30"));
        foreach (var day in new[] { "Monday", "Wednesday", "Friday" }) fields.Add(new("Weekdays", day));
        var comparison = await Post(client, "/Planner/Compare", fields);
        Assert.Equal(HttpStatusCode.Redirect, comparison.StatusCode);
        html = await (await Follow(client, comparison)).Content.ReadAsStringAsync();
        Assert.Contains("16 visits. Two ways to plan.", html);
        Assert.Contains("$16.00", html);
        Assert.Contains("$30.00", html);
        var comparedPlan = Regex.Match(html, "name=\"comparedPlan\" value=\"([^\"]+)\"");
        Assert.True(comparedPlan.Success);
        fields.Add(new("comparedPlan", comparedPlan.Groups[1].Value));
        fields.Add(new("choice", "Daily"));
        var continuation = await Post(client, "/Planner/Continue", fields);
        Assert.Equal(HttpStatusCode.Redirect, continuation.StatusCode);
        Assert.StartsWith("/Prepaid/Choose?plan=", continuation.Headers.Location!.OriginalString);
        var choices = await client.GetStringAsync(continuation.Headers.Location);
        var expected = new List<string>();
        for (var date = new DateOnly(2026, 10, 26); date <= new DateOnly(2026, 11, 30); date = date.AddDays(1))
            if (date.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Wednesday or DayOfWeek.Friday)
                expected.Add(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Assert.Equal(16, expected.Count);
        var offered = Regex.Matches(choices, "name=\"selectedDates\"[^>]*value=\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value).ToArray();
        Assert.Equal(expected, offered);
        Assert.Equal("2026-11-30", offered[^1]);

        // Repeated independent GETs must keep both grounded bounds and text.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var restored = await client.GetStringAsync("/Planner");
            Assert.Matches("id=\"StartDate\"[^>]*value=\"2026-10-26\"", restored);
            Assert.Matches("id=\"EndDate\"[^>]*value=\"2026-11-30\"", restored);
            Assert.Contains(Sentence, WebUtility.HtmlDecode(restored));
        }
        Assert.Equal(applicationsBefore, await db.PermitApplications.CountAsync());
        Assert.Equal(purchasesBefore, await db.PrepaidPurchases.CountAsync());
    }
}
