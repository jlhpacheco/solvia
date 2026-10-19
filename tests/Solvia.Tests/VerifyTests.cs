using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Solvia.Controllers;
using Solvia.Data;
using Solvia.Models;
using Solvia.Models.Enums;
using Solvia.Models.ViewModels;
using Solvia.Services;

namespace Solvia.Tests;

/// <summary>
/// R7 public-verify tests (REDO-PLAN items 46–48): the /verify
/// page and its /api/verify JSON twin are driven as controllers
/// over the real EF model, and must agree field-for-field because
/// both serialize the one <see cref="VerifyViewModel"/>. The rate
/// limiter is proven at the unit (a 60-per-minute burst) and at
/// both doors (a tripped limit answers 429).
/// </summary>
public sealed class VerifyTests
{
    private const string TestIp = "203.0.113.7";

    private static Vehicle AddVehicle(
        SolviaDbContext db, string studentId, string plate = "8XYZ123")
    {
        var vehicle = new Vehicle
        {
            Id = Guid.NewGuid(),
            StudentId = studentId,
            Plate = plate,
            State = UsState.CA,
            Color = VehicleColor.Blue,
            Make = "Honda",
            Model = "Civic",
        };
        db.Vehicles.Add(vehicle);
        db.SaveChanges();
        return vehicle;
    }

    private static PermitApplication AddApplication(
        SolviaDbContext db,
        string token,
        UserCategory category = UserCategory.Student,
        PaymentStatus payment = PaymentStatus.Paid,
        bool issued = true,
        DateOnly? start = null,
        DateOnly? end = null)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var type = db.PermitTypes.Single(t => t.Code == PermitTypeCode.Term);
        var issuedAt = new DateTime(2026, 10, 19, 15, 30, 0, DateTimeKind.Utc);
        var application = new PermitApplication
        {
            Id = Guid.NewGuid(),
            StudentId = "maria",
            Campus = "Fresno City College",
            Category = category,
            VehicleId = AddVehicle(db, "maria").Id,
            PermitTypeId = type.Id,
            Term = TermKind.Fall,
            Source = "form",
            StartDate = start ?? today.AddDays(-1),
            EndDate = end ?? today.AddDays(30),
            Status = ApplicationStatus.Pending,
            PriceCents = 3000,
            PaymentStatus = payment,
            PayPalOrderId = "ORDER-1",
            PayPalCaptureId = "CAP-1",
            PaidAtUtc = payment == PaymentStatus.Paid ? issuedAt : null,
            ReceiptNumber = issued ? "SOL-2026-123456" : null,
            ReceiptIssuedAtUtc = issued ? issuedAt : null,
            VerifyToken = token,
            SubmittedAtUtc = issuedAt.AddDays(-1),
            ConcurrencyToken = Guid.NewGuid(),
        };
        db.PermitApplications.Add(application);
        db.SaveChanges();
        return application;
    }

    private static VerifyController PageController(
        SolviaDbContext db, VerifyRateLimiter limiter, string ip = TestIp)
    {
        var controller = new VerifyController(db, limiter);
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    private static VerifyApiController ApiController(
        SolviaDbContext db, VerifyRateLimiter limiter, string ip = TestIp)
    {
        var controller = new VerifyApiController(db, limiter);
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    private static VerifyViewModel PageModel(IActionResult result, string viewName)
    {
        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal(viewName, view.ViewName);
        return Assert.IsType<VerifyViewModel>(view.Model);
    }

    private static VerifyViewModel ApiModel(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return Assert.IsType<VerifyViewModel>(ok.Value);
    }

    // ------------------------------------------------------ the page

    [Fact]
    public async Task Page_ValidPermit_ShowsTheFullItem46FieldSet()
    {
        var (db, connection) = RedoTestDb.Create();
        using var _ = connection;
        using var __ = db;
        var app = AddApplication(db, "tok-valid");
        var typeName = db.PermitTypes.Single(t => t.Code == PermitTypeCode.Term).Name;

        var model = PageModel(
            await PageController(db, new VerifyRateLimiter()).Index("tok-valid"),
            "Verify");

        Assert.True(model.Found);
        Assert.True(model.IsValid);
        Assert.Equal("SOL-2026-123456", model.PermitNumber);
        Assert.Equal("Fresno City College", model.Campus);
        Assert.Equal(typeName, model.PermitType);
        Assert.Equal("CA", model.PlateState);
        Assert.Equal("Student", model.Category);
        Assert.Equal("Student/Public Parking Eligible", model.Privilege);
        Assert.Equal("Paid", model.PaymentStatus);
        Assert.Equal(app.StartDate, model.StartDate);
        Assert.Equal(app.EndDate, model.EndDate);
        Assert.Equal(app.ReceiptIssuedAtUtc, model.LastUpdatedUtc);
    }

    [Fact]
    public async Task Page_EmployeePermit_StaffPrivilegeAndNotRequiredSpelledOut()
    {
        var (db, connection) = RedoTestDb.Create();
        using var _ = connection;
        using var __ = db;
        AddApplication(
            db, "tok-elena", category: UserCategory.Employee,
            payment: PaymentStatus.NotRequired);

        var model = PageModel(
            await PageController(db, new VerifyRateLimiter()).Index("tok-elena"),
            "Verify");

        Assert.True(model.IsValid);
        Assert.Equal("Employee", model.Category);
        Assert.Equal("Staff Parking Eligible", model.Privilege);
        Assert.Equal("Not Required", model.PaymentStatus);
    }

    [Fact]
    public async Task Page_ExpiredPermit_NotValidWithReason()
    {
        var (db, connection) = RedoTestDb.Create();
        using var _ = connection;
        using var __ = db;
        var today = DateOnly.FromDateTime(DateTime.Today);
        AddApplication(db, "tok-old", start: today.AddDays(-60), end: today.AddDays(-1));

        var model = PageModel(
            await PageController(db, new VerifyRateLimiter()).Index("tok-old"),
            "Invalid");

        Assert.True(model.Found);
        Assert.False(model.IsValid);
        Assert.Contains("ended on", model.Reason);
    }

    [Fact]
    public async Task TokenWithoutNumber_ModelSaysNotIssued_AndViewsCarryTheWords()
    {
        var (db, connection) = RedoTestDb.Create();
        using var _ = connection;
        using var __ = db;
        AddApplication(db, "tok-pending-issue", issued: false);

        var model = PageModel(
            await PageController(db, new VerifyRateLimiter()).Index("tok-pending-issue"),
            "Verify");

        // Defensive state (item 46): a token exists without a
        // number — the model carries no number and both views print
        // the "(not issued)" words in that case.
        Assert.True(model.Found);
        Assert.Null(model.PermitNumber);
        var viewsDir = FindViewsDir();
        Assert.Contains("(not issued)",
            File.ReadAllText(Path.Combine(viewsDir, "Verify.cshtml")));
        Assert.Contains("(not issued)",
            File.ReadAllText(Path.Combine(viewsDir, "Invalid.cshtml")));
    }

    // -------------------------------------------------- the JSON twin

    [Fact]
    public async Task Api_SameToken_SameVerdictAndFieldsAsPage()
    {
        var (db, connection) = RedoTestDb.Create();
        using var _ = connection;
        using var __ = db;
        AddApplication(db, "tok-valid");

        var page = PageModel(
            await PageController(db, new VerifyRateLimiter()).Index("tok-valid"),
            "Verify");
        var api = ApiModel(
            await ApiController(db, new VerifyRateLimiter()).Get("tok-valid"));

        Assert.Equal(page.Found, api.Found);
        Assert.Equal(page.IsValid, api.IsValid);
        Assert.Equal(page.Reason, api.Reason);
        Assert.Equal(page.PermitNumber, api.PermitNumber);
        Assert.Equal(page.Campus, api.Campus);
        Assert.Equal(page.PermitType, api.PermitType);
        Assert.Equal(page.PlateState, api.PlateState);
        Assert.Equal(page.Category, api.Category);
        Assert.Equal(page.Privilege, api.Privilege);
        Assert.Equal(page.PaymentStatus, api.PaymentStatus);
        Assert.Equal(page.StartDate, api.StartDate);
        Assert.Equal(page.EndDate, api.EndDate);
        Assert.Equal(page.LastUpdatedUtc, api.LastUpdatedUtc);
    }

    [Fact]
    public async Task Api_SerializedJson_NeverCarriesPlateNameOrEmail()
    {
        var (db, connection) = RedoTestDb.Create();
        using var _ = connection;
        using var __ = db;
        AddApplication(db, "tok-valid");

        var api = ApiModel(
            await ApiController(db, new VerifyRateLimiter()).Get("tok-valid"));
        var json = JsonSerializer.Serialize(
            api, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"plateState\":\"CA\"", json);
        Assert.DoesNotContain("8XYZ123", json);
        Assert.DoesNotContain("Maria", json);
        Assert.DoesNotContain("student.example", json);
    }

    [Fact]
    public async Task UnknownOrMissingToken_NeutralAnswer_BothDoors()
    {
        var (db, connection) = RedoTestDb.Create();
        using var _ = connection;
        using var __ = db;
        AddApplication(db, "tok-valid");

        var pageUnknown = PageModel(
            await PageController(db, new VerifyRateLimiter()).Index("tok-nope"),
            "Invalid");
        var pageMissing = PageModel(
            await PageController(db, new VerifyRateLimiter()).Index(null),
            "Invalid");
        var apiUnknown = ApiModel(
            await ApiController(db, new VerifyRateLimiter()).Get("tok-nope"));

        foreach (var model in new[] { pageUnknown, pageMissing, apiUnknown })
        {
            Assert.False(model.Found);
            Assert.False(model.IsValid);
            Assert.Null(model.PermitNumber);
            Assert.Null(model.Campus);
            Assert.Null(model.PlateState);
        }
    }

    // --------------------------------------------------- rate limit

    [Fact]
    public void Limiter_AllowsSixtyPerMinutePerIp_ThenTrips_ThenResets()
    {
        var limiter = new VerifyRateLimiter();
        var t0 = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < VerifyRateLimiter.MaxRequests; i++)
        {
            Assert.True(limiter.TryAcquire(TestIp, t0), $"request {i + 1} should pass");
        }

        Assert.False(limiter.TryAcquire(TestIp, t0.AddSeconds(30)));
        // A different IP has its own window.
        Assert.True(limiter.TryAcquire("198.51.100.9", t0.AddSeconds(30)));
        // The window resets once it has fully elapsed.
        Assert.True(limiter.TryAcquire(TestIp, t0.AddSeconds(61)));
    }

    [Fact]
    public async Task Page_WhenLimiterTripped_Answers429()
    {
        var (db, connection) = RedoTestDb.Create();
        using var _ = connection;
        using var __ = db;
        AddApplication(db, "tok-valid");
        var limiter = new VerifyRateLimiter();
        for (var i = 0; i < VerifyRateLimiter.MaxRequests; i++)
        {
            Assert.True(limiter.TryAcquire(TestIp));
        }

        var result = await PageController(db, limiter).Index("tok-valid");

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(429, status.StatusCode);
    }

    [Fact]
    public async Task Api_WhenLimiterTripped_Answers429()
    {
        var (db, connection) = RedoTestDb.Create();
        using var _ = connection;
        using var __ = db;
        AddApplication(db, "tok-valid");
        var limiter = new VerifyRateLimiter();
        for (var i = 0; i < VerifyRateLimiter.MaxRequests; i++)
        {
            Assert.True(limiter.TryAcquire(TestIp));
        }

        var result = await ApiController(db, limiter).Get("tok-valid");

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(429, status.StatusCode);
    }

    private static string FindViewsDir()
    {
        var start = Path.GetDirectoryName(typeof(VerifyTests).Assembly.Location);
        var dir = start is null ? null : new DirectoryInfo(start);
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName, "src", "Solvia", "Views", "Verify");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Views/Verify above the test assembly.");
    }
}
