using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Solvia.Data;
using Solvia.Models;
using Solvia.Models.Enums;
using Solvia.Services;

namespace Solvia.Tests;

/// <summary>Real MVC/Razor/CSRF contracts for a policy-only college refresh, without JavaScript or provider HTTP.</summary>
public sealed class CampusPolicyIntegrationTests
{
    private const string Mjc = "Modesto Junior College / Columbia College / YCCD";
    private static string Start => $"{DateTime.Now.Year + 1}-01-20";
    private static string End => $"{DateTime.Now.Year + 1}-02-16";
    private const string Description = "A fictional confirmed Monday, Wednesday and Friday schedule";

    private sealed class Counter : IVisitScheduleReader, IAiExtractor, IPayPalService
    {
        public int ReaderCalls;
        public int ExtractorCalls;
        public int PaymentCalls;

        public Task<VisitScheduleDraft> ReadAsync(string text, CancellationToken cancellationToken = default)
        {
            ReaderCalls++;
            return Task.FromResult(new VisitScheduleDraft(
                [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday], 4, "fallback", "Offline policy-test reader.",
                DateOnly.ParseExact(Start, "yyyy-MM-dd", CultureInfo.InvariantCulture), false,
                DateOnly.ParseExact(End, "yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }

        public Task<ExtractionResult> ExtractAsync(string text, CancellationToken cancellationToken = default)
        {
            ExtractorCalls++;
            throw new InvalidOperationException("A college-information refresh must not extract a vehicle.");
        }

        private Task<T> UnexpectedPayment<T>()
        {
            PaymentCalls++;
            throw new InvalidOperationException("A college-information refresh must not call PayPal.");
        }

        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default) => UnexpectedPayment<string>();
        public Task<PayPalOrderResult> CreateOrderAsync(int amountCents, string reference, string? returnUrl = null,
            string? cancelUrl = null, CancellationToken cancellationToken = default) => UnexpectedPayment<PayPalOrderResult>();
        public Task<PayPalCaptureResult> CaptureOrderAsync(string orderId, CancellationToken cancellationToken = default) =>
            UnexpectedPayment<PayPalCaptureResult>();
        public Task<PayPalOrderResult> GetOrderAsync(string orderId, CancellationToken cancellationToken = default) =>
            UnexpectedPayment<PayPalOrderResult>();
        public Task<bool> VerifyWebhookSignatureAsync(string transmissionId, string transmissionTime, string transmissionSig,
            string certUrl, string authAlgo, string webhookEventJson, CancellationToken cancellationToken = default) =>
            UnexpectedPayment<bool>();
    }

    private sealed class World : IDisposable
    {
        private readonly AppIntegrationTests.DemoFactory _base = new();
        public Counter Calls { get; } = new();
        public WebApplicationFactory<Program> Host { get; }
        public HttpClient Client { get; }

        public World()
        {
            Host = _base.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.AddSingleton<IVisitScheduleReader>(Calls);
                services.AddSingleton<IAiExtractor>(Calls);
                services.AddSingleton<IPayPalService>(Calls);
            }));
            Client = Host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            // Production deliberately has no saved car for Maria. This owned,
            // fictional prerequisite is fixture setup before any state baseline.
            using var scope = Host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SolviaDbContext>();
            db.Vehicles.Add(new Vehicle
            {
                Id = Guid.NewGuid(), StudentId = "maria", Plate = "POLICY1", State = UsState.CA,
                Color = VehicleColor.White, Make = "Volvo", Model = "760"
            });
            db.SaveChanges();
        }

        public async Task<string> Get(string route) => await Read(await Follow(await Client.GetAsync(route)));

        public async Task<HttpResponseMessage> Follow(HttpResponseMessage response)
        {
            for (var hop = 0; response.StatusCode == HttpStatusCode.Redirect && hop < 5; hop++)
                response = await Client.GetAsync(response.Headers.Location!);
            return response;
        }

        public async Task<string> Post(string route, List<KeyValuePair<string, string>> fields, string page)
        {
            var html = await Get(page);
            var values = fields.ToList();
            values.Add(new("__RequestVerificationToken", Token(html)));
            if (page.StartsWith("/Planner", StringComparison.Ordinal) && !values.Any(field => field.Key == "draftOwner"))
                values.Add(new("draftOwner", Attribute(Tag(html, "form", "id", "planner-form"), "data-person")));
            return await Read(await Follow(await Client.PostAsync(route, new FormUrlEncodedContent(values))));
        }

        public async Task<string[]> Campuses()
        {
            using var scope = Host.Services.CreateScope();
            return await Task.FromResult(scope.ServiceProvider.GetRequiredService<IFeeCatalog>().All().Select(row => row.Campus).ToArray());
        }

        public async Task<(Guid Vehicle, int Type)> Choices()
        {
            using var scope = Host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SolviaDbContext>();
            return (await db.Vehicles.Where(vehicle => vehicle.StudentId == "maria").Select(vehicle => vehicle.Id).FirstAsync(),
                await db.PermitTypes.Where(type => type.Active && type.Code == PermitTypeCode.Daily).Select(type => type.Id).FirstAsync());
        }

        public async Task<string> FinancialState()
        {
            using var scope = Host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SolviaDbContext>();
            var applications = await db.PermitApplications.AsNoTracking().OrderBy(row => row.Id).Select(row => new
            {
                row.Id, row.StudentId, row.VehicleId, row.Campus, row.StartDate, row.EndDate, row.PriceCents,
                row.PaymentStatus, row.PayPalOrderId, row.PayPalCaptureId, row.ReceiptNumber, row.VerifyToken, row.PaidAtUtc
            }).ToListAsync();
            var purchases = await db.PrepaidPurchases.AsNoTracking().OrderBy(row => row.Id).Select(row => new
            {
                row.Id, row.StudentId, row.VehicleId, row.Campus, row.TotalCents, row.PaymentStatus,
                row.PayPalOrderId, row.PayPalCaptureId, row.PaidAtUtc
            }).ToListAsync();
            return JsonSerializer.Serialize(new
            {
                applications, purchases,
                memberships = await db.PrepaidPurchaseItems.CountAsync(), vehicles = await db.Vehicles.CountAsync()
            });
        }

        public void AssertNoExternalWork(int readerCalls = 0)
        {
            Assert.Equal(readerCalls, Calls.ReaderCalls);
            Assert.Equal(0, Calls.ExtractorCalls);
            Assert.Equal(0, Calls.PaymentCalls);
        }

        public void Dispose()
        {
            Client.Dispose();
            Host.Dispose();
            _base.Dispose();
        }
    }

    private static async Task<string> Read(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static string Tag(string html, string element, string attribute, string value)
    {
        var match = Regex.Match(html, $"<{element}\\b[^>]*\\b{attribute}=\"{Regex.Escape(value)}\"[^>]*>", RegexOptions.IgnoreCase);
        Assert.True(match.Success, $"Expected a {element} with {attribute}={value}.");
        return match.Value;
    }

    private static string Attribute(string tag, string name)
    {
        var match = Regex.Match(tag, $"\\b{Regex.Escape(name)}=\"([^\"]*)\"", RegexOptions.IgnoreCase);
        Assert.True(match.Success, $"Expected the {name} attribute.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static string Token(string html) => Attribute(Tag(html, "input", "name", "__RequestVerificationToken"), "value");

    private static string SelectValue(string html, string id)
    {
        var select = Regex.Match(html, $"<select\\b[^>]*\\bid=\"{id}\"[^>]*>(.*?)</select>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        Assert.True(select.Success);
        var options = Regex.Matches(select.Groups[1].Value, "<option\\b[^>]*>", RegexOptions.IgnoreCase);
        var selected = options.Cast<Match>().Where(option => Regex.IsMatch(option.Value, @"\bselected(?:\s|=|>)", RegexOptions.IgnoreCase)).ToArray();
        Assert.True(selected.Length <= 1, $"The {id} select has more than one selected option.");
        return Attribute(selected.Length == 1 ? selected[0].Value : options[0].Value, "value");
    }

    private static void AssertPolicy(string html, string campus, string route)
    {
        var wrapper = Tag(html, "div", "id", "selected-campus-policy");
        Assert.Matches(@"\bdata-campus-policy(?:\s|=|>)", wrapper);
        Assert.Equal("Campus", Attribute(wrapper, "data-campus-select"));
        Assert.Equal(Mjc, Attribute(wrapper, "data-policy-campus"));
        Assert.Equal(campus, SelectValue(html, "Campus"));
        var message = Regex.Match(html, @"<div\b[^>]*\bdata-campus-policy-message(?=\s|=|>)[^>]*>", RegexOptions.IgnoreCase);
        Assert.True(message.Success);
        Assert.Equal(campus != Mjc, Regex.IsMatch(message.Value, @"\bhidden(?:\s|=|>)", RegexOptions.IgnoreCase));
        var update = Regex.Match(html, $"<button\\b[^>]*\\bformaction=\"{Regex.Escape(route)}\"[^>]*>\\s*Update college information\\s*</button>", RegexOptions.IgnoreCase);
        Assert.True(update.Success);
        Assert.Matches(@"\bformnovalidate(?:\s|=|>)", update.Value);
    }

    private static List<KeyValuePair<string, string>> Plan(string campus = "CSU East Bay") =>
    [
        new("Campus", campus), new("StartDate", Start), new("EndDate", End), new("SingleVisit", "false"),
        new("Term", "Spring"), new("ScheduleText", Description),
        new("Weekdays", "Monday"), new("Weekdays", "Wednesday"), new("Weekdays", "Friday")
    ];

    private static List<KeyValuePair<string, string>> Apply(string campus, Guid vehicle, int type) =>
    [
        new("Campus", campus), new("VehicleId", vehicle.ToString()), new("PermitTypeId", type.ToString()),
        new("Term", "Summer"), new("StartDate", Start), new("StudentName", "Forged policy refresh identity"),
        new("CategoryLabel", "Forged category")
    ];

    private static void AssertPlanFields(string html)
    {
        Assert.Equal(Start, Attribute(Tag(html, "input", "id", "StartDate"), "value"));
        Assert.Equal(End, Attribute(Tag(html, "input", "id", "EndDate"), "value"));
        Assert.Contains(Description, WebUtility.HtmlDecode(html));
        foreach (var day in new[] { "Monday", "Wednesday", "Friday" })
            Assert.Matches(@"\bchecked(?:\s|=|>)", Tag(html, "input", "id", "day-" + day));
        Assert.Matches(@"\bchecked(?:\s|=|>)", Tag(html, "input", "id", "plan-term-Spring"));
        Assert.Matches(@"\bchecked(?:\s|=|>)", Tag(html, "input", "id", "visit-repeating"));
        Assert.DoesNotContain("data-testid=\"plan-result\"", html);
    }

    [Theory]
    [InlineData("/Planner", "Fresno City College", "/Planner/UpdateCampus")]
    [InlineData("/Permits/Apply", "", "/Permits/UpdateCampus")]
    public async Task InitialForms_HideTheUnrelatedMJCExemptionAndOfferANoScriptRefresh(string page, string campus, string route)
    {
        using var world = new World();
        var before = await world.FinancialState();
        AssertPolicy(await world.Get(page), campus, route);
        Assert.Equal(before, await world.FinancialState());
        world.AssertNoExternalWork();
    }

    [Fact]
    public async Task Planner_NoScriptRefreshAcrossEveryCatalogCampus_PreservesPicksWithoutReadingOrCreatingAnything()
    {
        using var world = new World();
        var before = await world.FinancialState();
        var campuses = await world.Campuses();
        Assert.Contains(Mjc, campuses);
        Assert.True(campuses.Length > 1);
        foreach (var campus in campuses)
        {
            var html = await world.Post("/Planner/UpdateCampus", Plan(campus), "/Planner");
            AssertPolicy(html, campus, "/Planner/UpdateCampus");
            AssertPlanFields(html);
            Assert.DoesNotContain("data-testid=\"schedule-reader\"", html);
        }
        Assert.Equal(before, await world.FinancialState());
        world.AssertNoExternalWork();
    }

    [Fact]
    public async Task Apply_NoScriptRefreshAcrossEveryCatalogCampus_PreservesPicksAndServerIdentityWithoutSubmitting()
    {
        using var world = new World();
        var before = await world.FinancialState();
        var (vehicle, type) = await world.Choices();
        foreach (var campus in await world.Campuses())
        {
            var html = await world.Post("/Permits/UpdateCampus", Apply(campus, vehicle, type), "/Permits/Apply");
            AssertPolicy(html, campus, "/Permits/UpdateCampus");
            Assert.Equal(vehicle.ToString(), SelectValue(html, "VehicleId"));
            Assert.Equal(type.ToString(), SelectValue(html, "PermitTypeId"));
            Assert.Equal("Summer", SelectValue(html, "Term"));
            Assert.Equal(Start, Attribute(Tag(html, "input", "id", "StartDate"), "value"));
            Assert.Contains("<strong>Maria</strong>", html);
            Assert.DoesNotContain("Forged policy refresh identity", html);
            Assert.DoesNotContain("Forged category", html);
        }
        Assert.Equal(before, await world.FinancialState());
        world.AssertNoExternalWork();
    }

    [Fact]
    public async Task Planner_CollegeRefreshKeepsOnlyThePreviouslyRecordedReaderProvenanceWithoutCallingItAgain()
    {
        using var world = new World();
        var before = await world.FinancialState();
        var read = await world.Post("/Planner/Read", Plan(), "/Planner");
        Assert.Contains("data-source=\"fallback\"", read);
        Assert.Equal(1, world.Calls.ReaderCalls);
        var fields = Plan(Mjc);
        fields.Add(new("ReaderSource", "together"));
        fields.Add(new("ReaderNote", "Forged hosted reader success"));
        var html = await world.Post("/Planner/UpdateCampus", fields, "/Planner");
        AssertPolicy(html, Mjc, "/Planner/UpdateCampus");
        AssertPlanFields(html);
        Assert.Contains("data-source=\"fallback\"", html);
        Assert.Contains("Offline policy-test reader.", html);
        Assert.DoesNotContain("Forged hosted reader success", html);
        var restored = await world.Get("/Planner");
        AssertPlanFields(restored);
        Assert.Contains("data-source=\"fallback\"", restored);
        Assert.Equal(before, await world.FinancialState());
        world.AssertNoExternalWork(readerCalls: 1);
    }

    [Theory]
    [InlineData("StartDate")]
    [InlineData("EndDate")]
    public async Task Planner_CollegeRefreshKeepsInvalidDateAttemptsAndOtherFields(string invalidField)
    {
        using var world = new World();
        var before = await world.FinancialState();
        var fields = Plan(Mjc);
        fields.RemoveAll(field => field.Key == invalidField);
        fields.Add(new(invalidField, "not-a-date"));
        var html = await world.Post("/Planner/UpdateCampus", fields, "/Planner");
        AssertPolicy(html, Mjc, "/Planner/UpdateCampus");
        Assert.Equal("not-a-date", Attribute(Tag(html, "input", "id", invalidField), "value"));
        Assert.Contains("id=\"planner-errors\"", html);
        Assert.Contains(Description, WebUtility.HtmlDecode(html));
        Assert.Matches(@"\bchecked(?:\s|=|>)", Tag(html, "input", "id", "day-Wednesday"));
        Assert.DoesNotContain("data-testid=\"plan-result\"", html);
        Assert.Equal(before, await world.FinancialState());
        world.AssertNoExternalWork();
    }

    [Fact]
    public async Task Apply_CollegeRefreshKeepsAnInvalidDateAttemptInsteadOfInventingAReplacement()
    {
        using var world = new World();
        var before = await world.FinancialState();
        var (vehicle, type) = await world.Choices();
        var fields = Apply(Mjc, vehicle, type);
        fields.RemoveAll(field => field.Key == "StartDate");
        fields.Add(new("StartDate", "not-a-date"));
        var html = await world.Post("/Permits/UpdateCampus", fields, "/Permits/Apply");
        AssertPolicy(html, Mjc, "/Permits/UpdateCampus");
        Assert.Equal("not-a-date", Attribute(Tag(html, "input", "id", "StartDate"), "value"));
        Assert.Contains("role=\"alert\"", html);
        Assert.Equal(vehicle.ToString(), SelectValue(html, "VehicleId"));
        Assert.Equal(type.ToString(), SelectValue(html, "PermitTypeId"));
        Assert.Equal("Summer", SelectValue(html, "Term"));
        Assert.Equal(before, await world.FinancialState());
        world.AssertNoExternalWork();
    }

    [Theory]
    [InlineData("/Planner/UpdateCampus", "/Planner")]
    [InlineData("/Permits/UpdateCampus", "/Permits/Apply")]
    public async Task PolicyRefreshRequiresAntiforgery(string route, string page)
    {
        using var world = new World();
        var before = await world.FinancialState();
        await world.Get(page);
        var response = await world.Client.PostAsync(route, new FormUrlEncodedContent(new Dictionary<string, string> { ["Campus"] = Mjc }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await world.FinancialState());
        world.AssertNoExternalWork();
    }

    [Theory]
    [InlineData("/Planner/UpdateCampus", "/Planner", "")]
    [InlineData("/Planner/UpdateCampus", "/Planner", "Unknown fictional college")]
    [InlineData("/Permits/UpdateCampus", "/Permits/Apply", "")]
    [InlineData("/Permits/UpdateCampus", "/Permits/Apply", "Unknown fictional college")]
    public async Task InvalidCampusRefreshShowsValidationWithoutAdoptingAnUnrelatedPolicyOrCreatingAnything(string route, string page, string campus)
    {
        using var world = new World();
        var before = await world.FinancialState();
        var (vehicle, type) = await world.Choices();
        var html = await world.Post(route, page == "/Planner" ? Plan(campus) : Apply(campus, vehicle, type), page);
        Assert.Contains("role=\"alert\"", html);
        var message = Regex.Match(html, @"<div\b[^>]*\bdata-campus-policy-message(?=\s|=|>)[^>]*>", RegexOptions.IgnoreCase);
        Assert.True(message.Success);
        Assert.Matches(@"\bhidden(?:\s|=|>)", message.Value);
        Assert.DoesNotContain("data-testid=\"plan-result\"", html);
        if (page == "/Planner")
            Assert.DoesNotContain(Description, WebUtility.HtmlDecode(await world.Get("/Planner")));
        Assert.Equal(before, await world.FinancialState());
        world.AssertNoExternalWork();
    }

    [Fact]
    public async Task StalePlannerOwnerCannotCopyThePreviousPersonsScheduleThroughAPolicyRefresh()
    {
        using var world = new World();
        var before = await world.FinancialState();
        var old = await world.Get("/Planner");
        var fields = Plan(Mjc);
        fields.Add(new("draftOwner", Attribute(Tag(old, "form", "id", "planner-form"), "data-person")));
        var home = await world.Get("/");
        var switched = await world.Client.PostAsync("/Home/SwitchStudent", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["id"] = "elena", ["__RequestVerificationToken"] = Token(home)
        }));
        Assert.Equal(HttpStatusCode.Redirect, switched.StatusCode);
        var html = await world.Post("/Planner/UpdateCampus", fields, "/Planner");
        Assert.Contains("The demo person changed", html);
        Assert.Contains("Elena", html);
        Assert.DoesNotContain(Description, WebUtility.HtmlDecode(html));
        Assert.DoesNotContain(Description, WebUtility.HtmlDecode(await world.Get("/Planner")));
        Assert.Equal(before, await world.FinancialState());
        world.AssertNoExternalWork();
    }
}
