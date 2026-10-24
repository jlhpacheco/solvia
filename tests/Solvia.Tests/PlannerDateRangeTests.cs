using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Solvia.Config;
using Solvia.Controllers;
using Solvia.Data;
using Solvia.Models.Enums;
using Solvia.Models.ViewModels;
using Solvia.Services;

namespace Solvia.Tests;

/// <summary>Offline range persistence, authoritative bounds and ordinary HTML posts.</summary>
public sealed class PlannerDateRangeTests
{
    private sealed class Reader : IVisitScheduleReader
    {
        public int Calls;
        public VisitScheduleDraft Result = new([DayOfWeek.Tuesday], 3, "fallback", null);
        public Task<VisitScheduleDraft> ReadAsync(string text, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(Result); }
    }
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
    private sealed class TempDataStub : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
    private sealed class World : IDisposable
    {
        public SolviaDbContext Db { get; }
        private readonly SqliteConnection _connection;
        public Reader Reader { get; } = new();
        public PlannerController Controller { get; }
        public World()
        {
            (Db, _connection) = RedoTestDb.Create();
            var catalog = new FeeCatalogService(RedoTestDb.FindFeesCsv());
            var http = new DefaultHttpContext(); http.Request.Headers.Cookie = "solvia_student=maria";
            Controller = new PlannerController(Db, catalog, new VisitPlannerService(catalog, new PricingService(catalog),
                new TermDatesService(catalog, Db), new Clock()), Reader,
                new UserCategoryResolver(Options.Create(new CategoryOptions())), new AiRequestLimiter(new Clock()))
            {
                ControllerContext = new ControllerContext { HttpContext = http },
                TempData = new TempDataDictionary(http, new TempDataStub())
            };
        }
        public void Dispose() { Db.Dispose(); _connection.Dispose(); }
    }
    private static PlannerViewModel Range() => new()
    {
        Campus = "Fresno City College", StartDate = new(2026, 10, 26), EndDate = new(2026, 11, 3),
        Weeks = 26, Weekdays = [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday], Term = TermKind.Fall
    };
    private static async Task<PlannerViewModel> Restored(PlannerController controller) =>
        Assert.IsType<PlannerViewModel>(Assert.IsType<ViewResult>(await controller.Index(null, default)).Model);

    [Fact]
    public async Task GroundedRangeReplacesBothAttemptedDatesAndSurvivesRepeatedProtectedGets()
    {
        using var world = new World();
        world.Reader.Result = new([DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday], 9, "fallback", null,
            new(2026, 11, 1), false, new(2026, 12, 31));
        var model = Range(); model.ScheduleText = "MWF from 11/01/2026 to 12/31/2026";
        world.Controller.ModelState.AddModelError("StartDate", "Old invalid date");
        world.Controller.ModelState.AddModelError("EndDate", "Old invalid end");
        Assert.IsType<RedirectToActionResult>(await world.Controller.Read(model, "maria", default));
        for (var index = 0; index < 2; index++)
        {
            var kept = await Restored(world.Controller);
            Assert.Equal(new DateOnly(2026, 11, 1), kept.StartDate);
            Assert.Equal(new DateOnly(2026, 12, 31), kept.EndDate);
            Assert.Equal(9, kept.Weeks);
            Assert.False(kept.SingleVisit);
            Assert.Equal(model.Weekdays, kept.Weekdays);
            Assert.Equal(model.ScheduleText, kept.ScheduleText);
        }
        Assert.True(world.Controller.ModelState.IsValid);
        Assert.Equal(1, world.Reader.Calls);
        Assert.Empty(world.Db.PermitApplications);
    }

    [Fact]
    public async Task NewWeeksOnlyReadingClearsTheOldEndBoundWithoutErasingAnUnrelatedStartDateError()
    {
        using var world = new World();
        var model = Range(); model.ScheduleText = "Tuesdays for three weeks";
        world.Controller.ModelState.AddModelError("StartDate", "Keep this date error");
        world.Controller.ModelState.AddModelError("EndDate", "Old end bound error");
        Assert.IsType<ViewResult>(await world.Controller.Read(model, "maria", default));
        Assert.Null(model.EndDate);
        Assert.False(world.Controller.ModelState.ContainsKey("EndDate"));
        Assert.Contains(world.Controller.ModelState["StartDate"]!.Errors, error => error.ErrorMessage == "Keep this date error");
        Assert.Equal(3, model.Weeks);
        Assert.Equal(1, world.Reader.Calls);
    }

    [Fact]
    public async Task BlankOptionalReadKeepsExplicitDatesAndDerivesTheirPartialWeekCountWithoutInference()
    {
        using var world = new World();
        var model = Range(); model.ScheduleText = " ";
        Assert.IsType<RedirectToActionResult>(await world.Controller.Read(model, "maria", default));
        var kept = await Restored(world.Controller);
        Assert.Equal(model.StartDate, kept.StartDate);
        Assert.Equal(model.EndDate, kept.EndDate);
        Assert.Equal(2, kept.Weeks);
        Assert.Equal("manual", kept.ReaderSource);
        Assert.Equal(0, world.Reader.Calls);
    }

    [Fact]
    public async Task EndDateChangeInvalidatesAnOldComparisonEvenWhenItStaysInTheSameCalendarWeekCount()
    {
        using var world = new World();
        var original = Range();
        Assert.IsType<RedirectToActionResult>(await world.Controller.Compare(original, "maria", default));
        var oldKey = original.ComparisonKey;
        var changed = Range(); changed.EndDate = new(2026, 11, 4);
        var held = Assert.IsType<RedirectToActionResult>(await world.Controller.Continue(changed, "Daily", oldKey, "maria", default));
        Assert.Equal("Index", held.ActionName);
        Assert.Equal(2, changed.Weeks);
        Assert.Contains("Your plan changed", world.Controller.TempData["PlannerComparisonNotice"]!.ToString());
        Assert.Equal(0, world.Reader.Calls);
        Assert.Empty(world.Db.PermitApplications);
    }

    [Theory]
    [InlineData("Compare")]
    [InlineData("SetVisitType")]
    public async Task SingleVisitClearsTheEndBoundAndItsIrrelevantBindingErrors(string action)
    {
        using var world = new World();
        var model = Range(); model.SingleVisit = true;
        world.Controller.ModelState.AddModelError("EndDate", "Forged hidden end date");
        var result = action == "Compare"
            ? await world.Controller.Compare(model, "maria", default)
            : await world.Controller.SetVisitType(model, "maria", default);
        Assert.IsType<RedirectToActionResult>(result);
        Assert.Null(model.EndDate);
        Assert.Null(model.ToRequest().EndDate);
        Assert.True(world.Controller.ModelState.IsValid);
        Assert.Equal(1, model.Weeks);
        Assert.Null((await Restored(world.Controller)).EndDate);
    }

    [Theory]
    [InlineData("2026-10-25")]
    [InlineData("2027-04-26")]
    public async Task ReversedOrOverlongRangeRemainsEditableWithoutAComparison(string end)
    {
        using var world = new World();
        var model = Range(); model.EndDate = DateOnly.ParseExact(end, "yyyy-MM-dd");
        Assert.IsType<ViewResult>(await world.Controller.Compare(model, "maria", default));
        Assert.NotEmpty(world.Controller.ModelState["EndDate"]!.Errors);
        Assert.Null(model.Result);
        Assert.Equal(DateOnly.ParseExact(end, "yyyy-MM-dd"), model.EndDate);
        Assert.Equal(0, world.Reader.Calls);
    }

    [Fact]
    public async Task StaleOwnerCannotReadOrReplaceTheCurrentPersonsDateRange()
    {
        using var world = new World();
        var model = Range(); model.ScheduleText = "Previous person's fictional range";
        Assert.IsType<RedirectToActionResult>(await world.Controller.Read(model, "elena", default));
        Assert.Null((await Restored(world.Controller)).EndDate);
        Assert.Equal(0, world.Reader.Calls);
    }

    private static async Task<Dictionary<string, string>> HttpFields(HttpClient client)
    {
        var html = await client.GetStringAsync("/Planner");
        return new()
        {
            ["Campus"] = "Fresno City College", ["StartDate"] = "2026-10-26", ["EndDate"] = "2026-11-03",
            ["SingleVisit"] = "false", ["Term"] = "Fall", ["Weeks"] = "not-an-integer", ["Weekdays"] = "Monday",
            ["draftOwner"] = Regex.Match(html, "data-person=\"([^\"]+)\"").Groups[1].Value,
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(Regex.Match(html,
                "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value)
        };
    }

    [Fact]
    public async Task OrdinaryHtmlPostUsesTheExplicitEndWithoutJavascriptOrAValidSubmittedWeekCount()
    {
        using var factory = new AppIntegrationTests.DemoFactory();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<TimeProvider>(new Clock())));
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var fields = await HttpFields(client);
        var response = await client.PostAsync("/Planner/Compare", new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var html = await client.GetStringAsync(response.Headers.Location!);
        Assert.Contains("2 visits. Two ways to plan.", html);
        Assert.Contains("$2.00", html);
        Assert.Matches("id=\"Weeks\"[^>]*value=\"2\"", html);
        var restored = await client.GetStringAsync("/Planner");
        Assert.Matches("id=\"EndDate\"[^>]*value=\"2026-11-03\"", restored);
    }

    [Fact]
    public async Task InvalidEndDateBindingIsNotReplacedByAWeeksOnlyComparison()
    {
        using var factory = new AppIntegrationTests.DemoFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var fields = await HttpFields(client); fields["EndDate"] = "not-a-date"; fields["Weeks"] = "2";
        var response = await client.PostAsync("/Planner/Compare", new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("planner-errors", html);
        Assert.Contains("not-a-date", html);
        Assert.DoesNotContain("data-testid=\"plan-result\"", html);
    }
}
