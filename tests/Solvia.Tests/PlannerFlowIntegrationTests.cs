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
    public async Task Comparison_ExplainsSixVisitsAndSingleDayContinuationWithoutCreatingApplication()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SolviaDbContext>();
        var count = await db.PermitApplications.CountAsync();
        var response = await Post("/Planner/Compare", PlanFields());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("6 visits. Two ways to plan.", html);
        Assert.Contains("$6.00", html);
        Assert.Contains("$30.00", html);
        Assert.Contains("only the first planned day", html);
        Assert.Contains("not a live campus feed", html);
        Assert.Equal(count, await db.PermitApplications.CountAsync());
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
        var fields = PlanFields(); fields.Add(new("choice", "Daily"));
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
