using System.IO.Compression;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Solvia.Config;
using Solvia.Controllers;
using Solvia.Data;
using Solvia.Models.Enums;
using Solvia.Models.ViewModels;
using Solvia.Services;

namespace Solvia.Tests;

/// <summary>Offline mode, owner, PRG and request-binding contracts. No provider calls.</summary>
public sealed class PlannerSingleVisitTests
{
    private static readonly DateOnly VisitDate = new(2026, 10, 26);
    private const string DraftKey = "Solvia.PlannerDraft";

    private sealed class Reader : IVisitScheduleReader
    {
        public int Calls;
        public VisitScheduleDraft Result = new([], null, "fallback", "Confirm your schedule.");
        public Task<VisitScheduleDraft> ReadAsync(string text, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(Result); }
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
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

    private static PlannerViewModel Model(bool single = true) => new()
    {
        Campus = "Fresno City College", StartDate = VisitDate, SingleVisit = single,
        Weeks = 26, Weekdays = Enum.GetValues<DayOfWeek>().ToList(), Term = TermKind.Fall
    };

    private static async Task<PlannerViewModel> Restored(PlannerController controller) =>
        Assert.IsType<PlannerViewModel>(Assert.IsType<ViewResult>(await controller.Index(null, default)).Model);

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(26)]
    public void SingleVisitRequest_DerivesExactlyOneDate_RegardlessOfRecurrenceInputs(int? weeks)
    {
        var model = Model(); model.Weeks = weeks;
        var request = model.ToRequest();
        Assert.Equal(1, request.Weeks);
        Assert.Equal(VisitDate, request.StartDate);
        Assert.Equal(new[] { DayOfWeek.Monday }, request.Weekdays);
    }

    [Theory]
    [InlineData("Compare")]
    [InlineData("Continue")]
    [InlineData("SetVisitType")]
    public async Task SingleMode_NormalizesForgedFieldsAndIndexedErrors_BeforeEveryAction(string action)
    {
        using var world = new World();
        var model = Model(); model.PersonId = "maria";
        var key = model.ComparisonKey;
        foreach (var field in new[] { "Weeks", "weeks[0]", "Weekdays", "weekdays[2]" })
            world.Controller.ModelState.AddModelError(field, "Forged recurrence value.");
        var result = action switch
        {
            "Compare" => await world.Controller.Compare(model, "maria", default),
            "Continue" => await world.Controller.Continue(model, "Daily", key, "maria", default),
            _ => await world.Controller.SetVisitType(model, "maria", default)
        };
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(action == "Continue" ? "Questions" : "Index", redirect.ActionName);
        Assert.Equal(1, model.Weeks);
        Assert.Equal(new[] { DayOfWeek.Monday }, model.Weekdays);
        Assert.True(world.Controller.ModelState.IsValid);
        var restored = await Restored(world.Controller);
        Assert.True(restored.SingleVisit);
        Assert.Equal(1, restored.Weeks);
        Assert.Equal(new[] { DayOfWeek.Monday }, restored.Weekdays);
        Assert.Equal(0, world.Reader.Calls);
        Assert.Empty(world.Db.PermitApplications);
    }

    [Theory]
    [InlineData("Compare")]
    [InlineData("Continue")]
    [InlineData("SetVisitType")]
    public async Task SingleMode_DoesNotEraseInvalidDateBinding(string action)
    {
        using var world = new World();
        var model = Model();
        world.Controller.ModelState.AddModelError("StartDate", "The date could not be parsed.");
        var result = action switch
        {
            "Compare" => await world.Controller.Compare(model, "maria", default),
            "Continue" => await world.Controller.Continue(model, "Daily", null, "maria", default),
            _ => await world.Controller.SetVisitType(model, "maria", default)
        };
        Assert.IsType<ViewResult>(result);
        Assert.Contains(world.Controller.ModelState["StartDate"]!.Errors,
            error => error.ErrorMessage == "The date could not be parsed.");
        Assert.Equal(0, world.Reader.Calls);
        Assert.Empty(world.Db.PermitApplications);
    }

    [Fact]
    public async Task SetVisitType_WithoutDateDoesNotSaveOrRedirectTheDefaultDate()
    {
        using var world = new World();
        var model = Model(); model.StartDate = default;
        Assert.IsType<ViewResult>(await world.Controller.SetVisitType(model, "maria", default));
        Assert.False(world.Controller.ModelState.IsValid);
        Assert.False(world.Controller.TempData.ContainsKey(DraftKey));
        Assert.Equal(0, world.Reader.Calls);
    }

    [Fact]
    public async Task ModeSwitchToRepeating_WithDisabledFieldsOmitted_RequiresManualRecurrenceChoices()
    {
        using var world = new World();
        Assert.IsType<RedirectToActionResult>(await world.Controller.SetVisitType(Model(), "maria", default));
        var repeating = Model(false); repeating.Weeks = null; repeating.Weekdays = [];
        Assert.IsType<RedirectToActionResult>(await world.Controller.SetVisitType(repeating, "maria", default));
        var restored = await Restored(world.Controller);
        Assert.False(restored.SingleVisit); Assert.Null(restored.Weeks); Assert.Empty(restored.Weekdays);
        Assert.Equal(0, world.Reader.Calls);
    }

    [Fact]
    public async Task BlankRead_PreservesSingleMode_WithoutInference()
    {
        using var world = new World();
        var model = Model(); model.ScheduleText = " ";
        Assert.IsType<RedirectToActionResult>(await world.Controller.Read(model, "maria", default));
        var restored = await Restored(world.Controller);
        Assert.True(restored.SingleVisit); Assert.Equal(VisitDate, restored.StartDate);
        Assert.Equal(1, restored.Weeks); Assert.Equal(new[] { DayOfWeek.Monday }, restored.Weekdays);
        Assert.Equal("manual", restored.ReaderSource); Assert.Equal(0, world.Reader.Calls);
    }

    [Fact]
    public async Task GroundedRead_ReplacesAttemptedModeAndKeepsItAcrossProtectedGets()
    {
        using var world = new World();
        world.Reader.Result = new([DayOfWeek.Monday], 1, "fallback", null, VisitDate, true);
        var model = Model(false); model.ScheduleText = "One day Monday 10/26/2026 only";
        world.Controller.ModelState.SetModelValue("SingleVisit", "false", "false");
        Assert.IsType<RedirectToActionResult>(await world.Controller.Read(model, "maria", default));
        Assert.False(world.Controller.ModelState.ContainsKey("SingleVisit"));
        for (var index = 0; index < 2; index++)
        {
            var restored = await Restored(world.Controller);
            Assert.True(restored.SingleVisit); Assert.Equal(model.ScheduleText, restored.ScheduleText);
            Assert.Equal(VisitDate, restored.StartDate);
        }
        Assert.Equal(1, world.Reader.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UncertainOrOverlongRead_DoesNotKeepAnOldSingleMode(bool oversized)
    {
        using var world = new World();
        var model = Model(); model.ScheduleText = oversized ? new string('x', 501) : "Maybe one day Monday";
        var result = await world.Controller.Read(model, "maria", default);
        Assert.False(model.SingleVisit);
        if (oversized) Assert.IsType<ViewResult>(result);
        else
        {
            Assert.IsType<RedirectToActionResult>(result);
            var restored = await Restored(world.Controller);
            Assert.False(restored.SingleVisit); Assert.Null(restored.Weeks); Assert.Empty(restored.Weekdays);
        }
        Assert.Equal(oversized ? 0 : 1, world.Reader.Calls);
    }

    [Fact]
    public async Task ModeIsPartOfTheComparisonSnapshot_EvenWhenItsEffectiveDateIsUnchanged()
    {
        using var world = new World();
        var repeating = Model(false); repeating.Weeks = 1; repeating.Weekdays = [DayOfWeek.Monday];
        Assert.IsType<RedirectToActionResult>(await world.Controller.Compare(repeating, "maria", default));
        var oldKey = repeating.ComparisonKey;
        var single = Model(); single.PersonId = "maria";
        Assert.NotEqual(oldKey, single.ComparisonKey);
        var held = Assert.IsType<RedirectToActionResult>(await world.Controller.Continue(single, "Daily", oldKey, "maria", default));
        Assert.Equal("Index", held.ActionName);
        Assert.Contains("Your plan changed", world.Controller.TempData["PlannerComparisonNotice"]!.ToString());
        Assert.Equal(0, world.Reader.Calls); Assert.Empty(world.Db.PermitApplications);
    }

    [Theory]
    [InlineData(null, 1, true, true)]
    [InlineData(null, 2, true, false)]
    [InlineData(null, 1, false, false)]
    [InlineData(false, 1, true, false)]
    [InlineData(true, 26, false, true)]
    public async Task LegacyProtectedDraftsInferOnlyMissingMode_ExplicitFalseRemainsFalse(
        bool? flag, int weeks, bool matchingDay, bool expected)
    {
        using var world = new World();
        var snapshot = new Dictionary<string, object?>
        {
            ["Owner"] = "maria", ["SavedAt"] = DateTimeOffset.UtcNow, ["Campus"] = "Fresno City College",
            ["StartDate"] = VisitDate, ["Weeks"] = weeks,
            ["Weekdays"] = new[] { matchingDay ? DayOfWeek.Monday : DayOfWeek.Tuesday },
            ["Term"] = TermKind.Fall, ["ScheduleText"] = "Kept fictional wording"
        };
        if (flag is not null) snapshot["SingleVisit"] = flag;
        using var output = new MemoryStream();
        using (var compressor = new BrotliStream(output, CompressionLevel.Fastest, leaveOpen: true))
            JsonSerializer.Serialize(compressor, snapshot);
        world.Controller.TempData[DraftKey] = Convert.ToBase64String(output.ToArray());
        var restored = await Restored(world.Controller);
        Assert.Equal(expected, restored.SingleVisit);
        Assert.Equal("Kept fictional wording", restored.ScheduleText);
        Assert.Equal(0, world.Reader.Calls);
    }

    private static async Task<Dictionary<string, string>> HttpFields(HttpClient client)
    {
        var html = await client.GetStringAsync("/Planner");
        return new()
        {
            ["Campus"] = "Fresno City College", ["StartDate"] = "2026-10-26", ["SingleVisit"] = "true",
            ["Term"] = "Fall", ["Weeks"] = "not-an-integer", ["Weekdays[0]"] = "not-a-weekday",
            ["draftOwner"] = Regex.Match(html, "data-person=\"([^\"]+)\"").Groups[1].Value,
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(Regex.Match(html,
                "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value)
        };
    }

    [Fact]
    public async Task NoJavaScriptModePost_RequiresCsrfAndCanBindWithoutRecurrenceFields()
    {
        using var factory = new AppIntegrationTests.DemoFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var fields = await HttpFields(client);
        var token = fields["__RequestVerificationToken"]; fields.Remove("__RequestVerificationToken");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Planner/SetVisitType", new FormUrlEncodedContent(fields))).StatusCode);
        fields["__RequestVerificationToken"] = token;
        var saved = await client.PostAsync("/Planner/SetVisitType", new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.Equal("/Planner", saved.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(saved.Headers.Location)).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-date")]
    public async Task NoJavaScriptModePost_RequiresAValidDate(string? date)
    {
        using var factory = new AppIntegrationTests.DemoFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var fields = await HttpFields(client);
        if (date is null) fields.Remove("StartDate"); else fields["StartDate"] = date;
        var held = await client.PostAsync("/Planner/SetVisitType", new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.OK, held.StatusCode);
        Assert.Contains("planner-errors", await held.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task NoJavaScriptModePost_RejectsPreviousPersonWithoutSavingTheirDraft()
    {
        using var factory = new AppIntegrationTests.DemoFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var fields = await HttpFields(client); fields["draftOwner"] = "elena";
        fields["ScheduleText"] = "Do not transfer this fictional draft";
        var held = await client.PostAsync("/Planner/SetVisitType", new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, held.StatusCode);
        var html = await client.GetStringAsync(held.Headers.Location!);
        Assert.Contains("The demo person changed", html);
        Assert.DoesNotContain("Do not transfer this fictional draft", html);
    }
}
