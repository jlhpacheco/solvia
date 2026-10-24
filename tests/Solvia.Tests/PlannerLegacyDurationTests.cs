using System.IO.Compression;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Solvia.Config;
using Solvia.Controllers;
using Solvia.Data;
using Solvia.Models.Enums;
using Solvia.Models.ViewModels;
using Solvia.Services;

namespace Solvia.Tests;

/// <summary>Offline conversion of legacy durations to date-only planning inputs.</summary>
public sealed class PlannerLegacyDurationTests
{
    private static readonly DateOnly Start = new(2026, 10, 26);

    private sealed class Reader : IVisitScheduleReader
    {
        public int Calls;
        public VisitScheduleDraft Result = new([DayOfWeek.Tuesday], 3, "fallback", null);
        public Task<VisitScheduleDraft> ReadAsync(string text, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(Result); }
    }
    private sealed class NoNetwork : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; throw new InvalidOperationException("This offline test must not contact a provider."); }
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
    private static PlannerViewModel Legacy(int? weeks = 3) => new()
    {
        Campus = "Fresno City College", StartDate = Start, Weeks = weeks,
        Weekdays = [DayOfWeek.Tuesday], Term = TermKind.Fall
    };
    private static async Task<PlannerViewModel> Restored(PlannerController controller) =>
        Assert.IsType<PlannerViewModel>(Assert.IsType<ViewResult>(await controller.Index(null, default)).Model);

    private static async Task<VisitScheduleDraft> ReadOffline(string text, NoNetwork handler) =>
        await new TogetherVisitScheduleReader(new HttpClient(handler), Options.Create(new TogetherOptions
        {
            ApiKey = "", Model = TogetherOptions.SupportedModel
        }), NullLogger<TogetherVisitScheduleReader>.Instance).ReadAsync(text);

    [Fact]
    public async Task WeeksOnlyReaderAnswerReplacesAnOldRangeWithItsActualDerivedEnd()
    {
        using var world = new World();
        var network = new NoNetwork();
        world.Reader.Result = await ReadOffline("Tuesdays for three weeks", network);
        var model = Legacy(); model.EndDate = new(2026, 12, 31); model.ScheduleText = "Tuesdays for three weeks";
        Assert.IsType<RedirectToActionResult>(await world.Controller.Read(model, "maria", default));
        var restored = await Restored(world.Controller);
        Assert.Equal(Start, restored.StartDate);
        Assert.Equal(Start.AddDays(20), restored.EndDate);
        Assert.Equal(3, restored.Weeks);
        Assert.Equal(new[] { DayOfWeek.Tuesday }, restored.Weekdays);
        Assert.Contains("start and end dates", restored.ReaderNote);
        Assert.DoesNotContain("week count", restored.ReaderNote);
        Assert.Equal(0, network.Calls);
        Assert.Equal(1, world.Reader.Calls);
        Assert.Empty(world.Db.PermitApplications);
    }

    [Fact]
    public async Task OldProtectedWeeksOnlyDraftIsPresentedWithAnEndDate()
    {
        using var world = new World();
        using var compressed = new MemoryStream();
        using (var writer = new BrotliStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            JsonSerializer.Serialize(writer, new
            {
                Owner = "maria", SavedAt = DateTimeOffset.UtcNow, Campus = "Fresno City College",
                StartDate = Start, Weeks = 3, Weekdays = new[] { DayOfWeek.Tuesday }, Term = TermKind.Fall,
                ScheduleText = "Tuesdays for three weeks", SingleVisit = false
                // The old snapshot deliberately has no EndDate property.
            });
        world.Controller.TempData["Solvia.PlannerDraft"] = Convert.ToBase64String(compressed.ToArray());
        var restored = await Restored(world.Controller);
        Assert.Equal(Start, restored.StartDate);
        Assert.Equal(Start.AddDays(20), restored.EndDate);
        Assert.Equal(3, restored.Weeks);
        Assert.False(restored.SingleVisit);
        Assert.Equal(0, world.Reader.Calls);
    }

    [Fact]
    public async Task DefaultPlanningPeriodHasAnEndDateWithoutInference()
    {
        using var world = new World();
        var model = await Restored(world.Controller);
        Assert.Equal(model.StartDate.AddDays(20), model.EndDate);
        Assert.Equal(3, model.Weeks);
        Assert.Equal(0, world.Reader.Calls);
    }

    [Fact]
    public async Task ExactEndRemainsAuthoritativeOverLegacyWeeksAndTheirBindingError()
    {
        using var world = new World();
        var model = Legacy(26); model.EndDate = Start.AddDays(8);
        world.Controller.ModelState.AddModelError("Weeks", "Old duration is invalid.");
        Assert.IsType<RedirectToActionResult>(await world.Controller.Compare(model, "maria", default));
        Assert.Equal(Start.AddDays(8), model.EndDate);
        Assert.Equal(2, model.Weeks);
        Assert.False(world.Controller.ModelState.ContainsKey("Weeks"));
        Assert.Equal(2, model.Result!.DayCount);
        Assert.Equal(0, world.Reader.Calls);
    }

    [Fact]
    public async Task WeeksOnlyCalendarOverflowRequiresAnEditableDateCorrection()
    {
        using var world = new World();
        var model = Legacy(1); model.StartDate = new(9999, 12, 30);
        Assert.IsType<ViewResult>(await world.Controller.Compare(model, "maria", default));
        Assert.Null(model.EndDate);
        Assert.Null(model.Result);
        Assert.Contains(world.Controller.ModelState["EndDate"]!.Errors,
            error => error.ErrorMessage.Contains("supported calendar dates"));
        Assert.Equal(0, world.Reader.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(27)]
    public async Task MissingOrInvalidLegacyDurationDoesNotSupplyAnEndDate(int? weeks)
    {
        using var world = new World();
        var model = Legacy(weeks);
        Assert.IsType<ViewResult>(await world.Controller.Compare(model, "maria", default));
        Assert.Null(model.EndDate);
        Assert.NotNull(model.Result);
        Assert.False(model.Result.IsValid);
        Assert.Equal(0, world.Reader.Calls);
    }

    [Theory]
    [InlineData("StartDate")]
    [InlineData("EndDate")]
    public async Task MalformedDateBindingCannotBeReplacedWithADerivedRange(string field)
    {
        using var world = new World();
        var model = Legacy();
        world.Controller.ModelState.AddModelError(field, "The submitted date could not be read.");
        Assert.IsType<ViewResult>(await world.Controller.Compare(model, "maria", default));
        Assert.Null(model.EndDate);
        Assert.Null(model.Result);
        Assert.Contains(world.Controller.ModelState[field]!.Errors, error => error.ErrorMessage.Contains("could not be read"));
    }

    [Fact]
    public async Task CurrentDateOnlyPostCannotRecoverAClearedEndFromPreviousServerState()
    {
        using var world = new World();
        Assert.IsType<RedirectToActionResult>(await world.Controller.Compare(Legacy(), "maria", default));
        var currentPost = Legacy(null); // The new form submits neither Weeks nor an end date.
        Assert.IsType<ViewResult>(await world.Controller.Compare(currentPost, "maria", default));
        Assert.Null(currentPost.EndDate);
        Assert.False(currentPost.Result!.IsValid);
        Assert.Null((await Restored(world.Controller)).EndDate);
        Assert.Equal(0, world.Reader.Calls);
    }

    [Theory]
    [InlineData("Tuesdays for three weeks and four weeks")]
    [InlineData("Maybe Tuesdays for three weeks")]
    public async Task AmbiguousNewReadingClearsOldEndAndDurationInsteadOfGuessing(string sentence)
    {
        using var world = new World();
        var network = new NoNetwork();
        world.Reader.Result = await ReadOffline(sentence, network);
        var model = Legacy(); model.EndDate = Start.AddDays(20); model.ScheduleText = sentence;
        Assert.IsType<RedirectToActionResult>(await world.Controller.Read(model, "maria", default));
        var restored = await Restored(world.Controller);
        Assert.Null(restored.EndDate);
        Assert.Null(restored.Weeks);
        Assert.NotNull(restored.ReaderNote);
        Assert.Contains("start and end dates", restored.ReaderNote);
        Assert.DoesNotContain("week count", restored.ReaderNote);
        Assert.Equal(0, network.Calls);
    }

    [Fact]
    public async Task SingleVisitNeverAcquiresALegacyRecurringEndDate()
    {
        using var world = new World();
        var model = Legacy(26); model.SingleVisit = true;
        Assert.IsType<RedirectToActionResult>(await world.Controller.Compare(model, "maria", default));
        Assert.Null(model.EndDate);
        Assert.Equal(1, model.Weeks);
        Assert.Equal(new[] { Start.DayOfWeek }, model.Weekdays);
        Assert.Equal(1, model.Result!.DayCount);
    }
}
