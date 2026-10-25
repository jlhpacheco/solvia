using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Solvia.Data;

namespace Solvia.Tests;

public sealed class PlannerFlowIntegrationTests : IClassFixture<AppIntegrationTests.DemoFactory>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    public PlannerFlowIntegrationTests(AppIntegrationTests.DemoFactory factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<TimeProvider>(new FixedClock())));
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 20, 16, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private async Task<HttpResponseMessage> Post(string route, IEnumerable<KeyValuePair<string, string>> fields, string tokenPage = "/Planner")
    {
        var page = await _client.GetAsync(tokenPage);
        for (var attempt = 0; page.StatusCode == HttpStatusCode.Redirect && attempt < 5; attempt++)
            page = await _client.GetAsync(page.Headers.Location!);
        page.EnsureSuccessStatusCode();
        var html = await page.Content.ReadAsStringAsync();
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success);
        var values = fields.ToList();
        var flow = Regex.Match(html, "name=\"flowId\"[^>]*value=\"([^\"]+)\"", RegexOptions.IgnoreCase);
        if (flow.Success && !values.Any(field => field.Key.Equals("FlowId", StringComparison.OrdinalIgnoreCase)))
            values.Add(new("FlowId", flow.Groups[1].Value));
        var response = await _client.PostAsync(route, new FormUrlEncodedContent(values.Append(
            new KeyValuePair<string, string>("draftOwner", Regex.Match(html, "data-person=\"([^\"]+)\"").Groups[1].Value)).Append(
            new KeyValuePair<string, string>("__RequestVerificationToken", WebUtility.HtmlDecode(match.Groups[1].Value)))));
        if (route is "/Permits/Understand" or "/Permits/Questions" or "/Planner/Read" or "/Planner/Compare" ||
            (route == "/Planner/Continue" && response.Headers.Location?.OriginalString.StartsWith("/Planner", StringComparison.Ordinal) == true))
            for (var attempt = 0; response.StatusCode == HttpStatusCode.Redirect && attempt < 5; attempt++)
                response = await _client.GetAsync(response.Headers.Location!);
        return response;
    }

    private static List<KeyValuePair<string, string>> PlanFields() =>
    [
        new("Campus", "Fresno City College"), new("StartDate", "2026-10-20"),
        new("Weeks", "3"), new("Weekdays", "Tuesday"), new("Weekdays", "Thursday"), new("Term", "Fall")
    ];

    private async Task<HttpResponseMessage> CompareAndContinue(List<KeyValuePair<string, string>> fields)
    {
        var comparison = await Post("/Planner/Compare", fields);
        var html = await comparison.Content.ReadAsStringAsync();
        var key = Regex.Match(html, "name=\"comparedPlan\" value=\"([^\"]+)\"");
        Assert.True(key.Success);
        fields.Add(new("comparedPlan", key.Groups[1].Value));
        return await Post("/Planner/Continue", fields);
    }

    [Fact]
    public async Task Comparison_ExplainsSixPrepaidDatesWithoutCreatingApplicationOrPurchase()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SolviaDbContext>();
        var count = await db.PermitApplications.CountAsync();
        var purchases = await db.PrepaidPurchases.CountAsync();
        var response = await Post("/Planner/Compare", PlanFields());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("6 visits. Two ways to plan.", html);
        Assert.Contains("$6.00", html);
        Assert.Contains("$30.00", html);
        Assert.Contains("Pay once for the dates you choose", html);
        Assert.Contains("Each date gets its own one-day permit", html);
        Assert.Contains("future passes become valid on their printed dates", html);
        Assert.Contains("Buy these dates", html);
        Assert.DoesNotContain("Start with one day pass", html);
        Assert.Contains("not a live campus feed", html);
        Assert.Equal(count, await db.PermitApplications.CountAsync());
        Assert.Equal(purchases, await db.PrepaidPurchases.CountAsync());
    }

    [Theory]
    [InlineData("i am going 1 day 10/26/2026, monday. all day. only", "1", "Monday", 1)]
    [InlineData("One day per week on Monday for three weeks starting 10/26/2026", "3", "Monday", 3)]
    public async Task DatedDescriptionReplacesTodayAndKeepsOneVisitOrExplicitRecurrence(
        string description, string weeks, string weekday, int visits)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SolviaDbContext>();
        var before = await db.PermitApplications.CountAsync();
        var purchasesBefore = await db.PrepaidPurchases.CountAsync();
        var fields = PlanFields();
        fields.RemoveAll(pair => pair.Key == "Weeks");
        fields.Add(new("Weeks", ""));
        fields.Add(new("ScheduleText", description));
        var read = await Post("/Planner/Read", fields);
        var html = await read.Content.ReadAsStringAsync();
        Assert.Contains("value=\"2026-10-26\"", html);
        Assert.DoesNotContain("name=\"Weeks\"", html);
        if (visits == 1) Assert.Contains("data-single-visit=\"true\"", html);
        else Assert.Matches("id=\"EndDate\"[^>]*value=\"2026-11-15\"", html);
        Assert.Contains($"value=\"{weekday}\" checked=\"checked\"", html);
        Assert.Contains("data-source=\"fallback\"", html);

        fields.RemoveAll(pair => pair.Key is "Weeks" or "Weekdays" or "StartDate" or "SingleVisit");
        fields.Add(new("Weeks", weeks));
        fields.Add(new("Weekdays", weekday));
        fields.Add(new("StartDate", "2026-10-26"));
        fields.Add(new("SingleVisit", visits == 1 ? "true" : "false"));
        var comparison = await Post("/Planner/Compare", fields);
        html = await comparison.Content.ReadAsStringAsync();
        Assert.Contains($"{visits} {(visits == 1 ? "visit" : "visits")}. Two ways to plan.", html);
        Assert.Contains($"${visits}.00", html);
        var key = Regex.Match(html, "name=\"comparedPlan\" value=\"([^\"]+)\"");
        Assert.True(key.Success);
        fields.Add(new("comparedPlan", key.Groups[1].Value));
        fields.Add(new("choice", "Daily"));
        var continuation = await Post("/Planner/Continue", fields);
        Assert.Equal(HttpStatusCode.Redirect, continuation.StatusCode);
        var target = continuation.Headers.Location!.OriginalString;
        if (visits == 1)
        {
            Assert.StartsWith("/Permits/Questions", target);
            Assert.Contains("startDate=2026-10-26", target);
            var questionsResponse = await _client.GetAsync(continuation.Headers.Location);
            for (var attempt = 0; questionsResponse.StatusCode == HttpStatusCode.Redirect && attempt < 5; attempt++)
                questionsResponse = await _client.GetAsync(questionsResponse.Headers.Location!);
            questionsResponse.EnsureSuccessStatusCode();
            Assert.Contains("name=\"startDate\" value=\"2026-10-26\"", await questionsResponse.Content.ReadAsStringAsync());
        }
        else
        {
            Assert.StartsWith("/Prepaid/Choose?plan=", target);
            var choices = await _client.GetAsync(continuation.Headers.Location);
            Assert.Equal(HttpStatusCode.OK, choices.StatusCode);
            var choicesHtml = await choices.Content.ReadAsStringAsync();
            Assert.Contains("Choose your prepaid dates", choicesHtml);
            var offered = Regex.Matches(choicesHtml, "name=\"selectedDates\"[^>]*value=\"([^\"]+)\"")
                .Select(match => match.Groups[1].Value).ToArray();
            Assert.Equal(new[] { "2026-10-26", "2026-11-02", "2026-11-09" }, offered);
            Assert.Contains("Revise my schedule", choicesHtml);
        }
        var restored = await _client.GetStringAsync("/Planner");
        Assert.Contains("value=\"2026-10-26\"", restored);
        Assert.DoesNotContain("name=\"Weeks\"", restored);
        if (visits > 1) Assert.Matches("id=\"EndDate\"[^>]*value=\"2026-11-15\"", restored);
        Assert.Equal(before, await db.PermitApplications.CountAsync());
        Assert.Equal(purchasesBefore, await db.PrepaidPurchases.CountAsync());
    }

    [Theory]
    [InlineData("/Planner/Read")]
    [InlineData("/Planner/Compare")]
    [InlineData("/Planner/Continue")]
    public async Task AllPlannerPosts_RejectMissingAntiforgery(string route)
    {
        var response = await _client.PostAsync(route, new FormUrlEncodedContent(PlanFields()));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ChoiceCarriesConfirmedDateThroughVehicleReadFixItAndSavedSingleDay()
    {
        var fields = PlanFields();
        fields.RemoveAll(pair => pair.Key is "Weeks" or "Weekdays");
        fields.Add(new("SingleVisit", "true"));
        fields.Add(new("choice", "Daily"));
        var continueResponse = await CompareAndContinue(fields);
        Assert.Equal(HttpStatusCode.Redirect, continueResponse.StatusCode);
        var target = continueResponse.Headers.Location!.OriginalString;
        Assert.Contains("startDate=2026-10-20", target);
        Assert.Contains("permitType=Daily", target);
        var vehicleFields = new Dictionary<string, string>
        {
            ["college"] = "Fresno City College", ["carText"] = "blue Honda Civic license plate PLAN432",
            ["permitType"] = "Daily", ["term"] = "Fall", ["startDate"] = "2026-10-20"
        };
        var read = await Post("/Permits/Understand", vehicleFields, target);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var readHtml = await read.Content.ReadAsStringAsync();
        Assert.Contains("name=\"StartDate\" value=\"2026-10-20\"", readHtml);
        var reviewFields = new Dictionary<string, string>
        {
            ["College"] = "Fresno City College", ["Plate"] = "PLAN432", ["CarState"] = "CA",
            ["CarMake"] = "Honda", ["CarModel"] = "Civic", ["CarColor"] = "Blue",
            ["PermitType"] = "Daily", ["Term"] = "Fall", ["StartDate"] = "2026-10-20"
        };
        reviewFields["FlowId"] = Regex.Match(readHtml, "name=\"FlowId\"[^>]*value=\"([^\"]+)\"", RegexOptions.IgnoreCase).Groups[1].Value;
        var fix = await Post("/Permits/Questions", reviewFields);
        Assert.Contains("name=\"startDate\" value=\"2026-10-20\"", await fix.Content.ReadAsStringAsync());
        var review = await Post("/Permits/Review", reviewFields);
        Assert.Equal(HttpStatusCode.Redirect, review.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SolviaDbContext>();
        var application = await db.PermitApplications.Include(a => a.Vehicle).SingleAsync(a => a.Vehicle!.Plate == "PLAN432");
        Assert.Equal(new DateOnly(2026, 10, 20), application.StartDate);
        Assert.Equal(application.StartDate, application.EndDate);
        Assert.Equal(100, application.PriceCents);
        Assert.Null(application.PayPalOrderId);
    }

    [Fact]
    public async Task InvalidPlanAndForgedChoiceStayOnPlanner()
    {
        var fields = PlanFields(); fields.Add(new("choice", "Employee"));
        var response = await CompareAndContinue(fields);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Choose an available permit option", await response.Content.ReadAsStringAsync());
        fields = PlanFields(); fields.RemoveAll(x => x.Key == "Weekdays");
        response = await Post("/Planner/Compare", fields);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("data-testid=\"plan-result\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ForgedTermStartOutsideWindowCannotPersistVehicleOrPermit()
    {
        var fields = new Dictionary<string, string>
        {
            ["College"] = "Fresno City College", ["Plate"] = "BADPLAN1", ["CarState"] = "CA",
            ["PermitType"] = "Term", ["Term"] = "Fall", ["StartDate"] = "2026-07-01"
        };
        var response = await Post("/Permits/Review", fields, "/Permits/Questions?college=Fresno%20City%20College");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("planned start must fall inside", await response.Content.ReadAsStringAsync());
        using var scope = _factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<SolviaDbContext>().Vehicles.AnyAsync(v => v.Plate == "BADPLAN1"));
    }
}
