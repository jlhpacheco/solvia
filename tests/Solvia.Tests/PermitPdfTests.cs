using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Solvia.Controllers;
using Solvia.Data;
using Solvia.Models;
using Solvia.Models.Enums;
using Solvia.Services;

namespace Solvia.Tests;

/// <summary>
/// The printable permit PDF (the Duvara-mirrored receipt PDF).
/// Two proofs: PermitPdfService turns a live permit into real PDF
/// bytes (%PDF magic, non-trivial size), and PermitsController
/// serves those bytes ONLY for Paid / NotRequired applications —
/// an unpaid application gets a redirect back to its receipt
/// screen, never a PDF, and an unknown id is a 404.
/// </summary>
public sealed class PermitPdfTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<SolviaDbContext> _options;
    private readonly FeeCatalogService _catalog = new(FindFeesCsv());

    public PermitPdfTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<SolviaDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var db = new SolviaDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private SolviaDbContext NewDb() => new(_options);

    private static PermitPdfService NewPdfService() =>
        new(new HttpContextAccessor());

    private PermitsController NewController(SolviaDbContext db)
    {
        var controller = new PermitsController(
            db,
            _catalog,
            new UnusedExtractor(),
            new NullTermDatesService(),
            new BuiltinCollegeSearch(_catalog),
            NewPdfService());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };
        return controller;
    }

    private static string FindFeesCsv()
    {
        // Anchored on THIS assembly's location first (the runner's
        // own base directory is not inside the repo) — the same
        // pattern as FlowFixTests.
        var starts = new[]
        {
            Path.GetDirectoryName(typeof(PermitPdfTests).Assembly.Location),
            AppContext.BaseDirectory,
        };

        foreach (var start in starts)
        {
            var dir = start is null ? null : new DirectoryInfo(start);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "data", "fees.csv");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }
        }

        throw new FileNotFoundException("data/fees.csv not found above the test assembly.");
    }

    /// <summary>A complete in-memory permit graph for the PDF service.</summary>
    private static PermitApplication SampleApplication(PaymentStatus status, int priceCents) => new()
    {
        Id = Guid.NewGuid(),
        StudentId = "priya",
        Student = new Student { Id = "priya", DisplayName = "Priya", Email = "priya@student.example" },
        Campus = "Modesto Junior College / Columbia College / YCCD",
        VehicleId = Guid.NewGuid(),
        Vehicle = new Vehicle
        {
            Id = Guid.NewGuid(),
            StudentId = "priya",
            Plate = "9ABC123",
            State = UsState.CA,
            Color = VehicleColor.Blue,
            Make = "Toyota",
            Model = "Corolla",
        },
        PermitTypeId = 1,
        PermitType = new PermitType { Id = 1, Code = PermitTypeCode.Term, Name = "Term permit", Active = true },
        Term = TermKind.Fall,
        StartDate = new DateOnly(2026, 8, 24),
        EndDate = new DateOnly(2026, 12, 22),
        PriceCents = priceCents,
        PaymentStatus = status,
        ReceiptNumber = "SOL-2026-000101",
        VerifyToken = "test-verify-token-123",
        PaidAtUtc = status == PaymentStatus.Paid ? DateTime.UtcNow : null,
        SubmittedAtUtc = DateTime.UtcNow,
    };

    private static void AssertRealPdf(byte[] bytes)
    {
        Assert.True(bytes.Length > 1000, $"PDF too small: {bytes.Length} bytes");
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
    }

    // ------------------------------------------------------------------
    // The service: real PDF bytes for live permits
    // ------------------------------------------------------------------

    [Fact]
    public void Service_PaidApplication_ReturnsRealPdfBytes()
    {
        var pdf = NewPdfService().BuildPermitPdf(
            SampleApplication(PaymentStatus.Paid, 3000));
        AssertRealPdf(pdf);
    }

    [Fact]
    public void Service_FreeApplication_ReturnsRealPdfBytes()
    {
        var pdf = NewPdfService().BuildPermitPdf(
            SampleApplication(PaymentStatus.NotRequired, 0));
        AssertRealPdf(pdf);
    }

    // ------------------------------------------------------------------
    // The controller gate: PDF only when Paid / NotRequired
    // ------------------------------------------------------------------

    private async Task<Guid> SeedApplicationAsync(
        SolviaDbContext db, PaymentStatus status, int priceCents)
    {
        var vehicle = new Vehicle
        {
            Id = Guid.NewGuid(),
            StudentId = "priya",
            Plate = "9ABC123",
            State = UsState.CA,
            Color = VehicleColor.Blue,
            Make = "Toyota",
            Model = "Corolla",
        };
        db.Vehicles.Add(vehicle);

        var application = new PermitApplication
        {
            Id = Guid.NewGuid(),
            StudentId = "priya",
            Campus = "Modesto Junior College / Columbia College / YCCD",
            VehicleId = vehicle.Id,
            PermitTypeId = db.PermitTypes.First(t => t.Code == PermitTypeCode.Term).Id,
            Term = TermKind.Fall,
            StartDate = new DateOnly(2026, 8, 24),
            EndDate = new DateOnly(2026, 12, 22),
            PriceCents = priceCents,
            PaymentStatus = status,
            ReceiptNumber = status is PaymentStatus.Paid or PaymentStatus.NotRequired
                ? "SOL-2026-000101"
                : null,
            VerifyToken = "test-verify-token-123",
            PaidAtUtc = status == PaymentStatus.Paid ? DateTime.UtcNow : null,
            SubmittedAtUtc = DateTime.UtcNow,
        };
        db.PermitApplications.Add(application);
        await db.SaveChangesAsync();
        return application.Id;
    }

    [Fact]
    public async Task Controller_PaidApplication_ServesThePdf()
    {
        using var db = NewDb();
        var id = await SeedApplicationAsync(db, PaymentStatus.Paid, 3000);
        var controller = NewController(db);

        var result = await controller.PermitPdf(id, default);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal("Solvia-Permit-SOL-2026-000101.pdf", file.FileDownloadName);
        AssertRealPdf(file.FileContents);
    }

    [Fact]
    public async Task Controller_FreeApplication_ServesThePdf()
    {
        using var db = NewDb();
        var id = await SeedApplicationAsync(db, PaymentStatus.NotRequired, 0);
        var controller = NewController(db);

        var result = await controller.PermitPdf(id, default);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
        AssertRealPdf(file.FileContents);
    }

    [Fact]
    public async Task Controller_PendingApplication_GetsNoPdf_RedirectsToReceipt()
    {
        using var db = NewDb();
        var id = await SeedApplicationAsync(db, PaymentStatus.Pending, 3000);
        var controller = NewController(db);

        var result = await controller.PermitPdf(id, default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Receipt", redirect.ActionName);
    }

    [Fact]
    public async Task Controller_FailedApplication_GetsNoPdf_RedirectsToReceipt()
    {
        using var db = NewDb();
        var id = await SeedApplicationAsync(db, PaymentStatus.Failed, 3000);
        var controller = NewController(db);

        var result = await controller.PermitPdf(id, default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Receipt", redirect.ActionName);
    }

    [Fact]
    public async Task Controller_UnknownId_Returns404()
    {
        using var db = NewDb();
        var controller = NewController(db);

        var result = await controller.PermitPdf(Guid.NewGuid(), default);

        Assert.IsType<NotFoundResult>(result);
    }

    // ------------------------------------------------------------------
    // Constructor fill-ins the PermitPdf action never calls
    // ------------------------------------------------------------------

    private sealed class UnusedExtractor : IAiExtractor
    {
        public Task<ExtractionResult> ExtractAsync(
            string text, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not used by the PermitPdf action.");
    }

    private sealed class NullTermDatesService : ITermDatesService
    {
        public TermDates? GetTermDates(string campus, TermKind term, int year) => null;
    }
}
