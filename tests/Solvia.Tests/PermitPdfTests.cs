using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Solvia.Config;
using Solvia.Controllers;
using Solvia.Data;
using Solvia.Models;
using Solvia.Models.Enums;
using Solvia.Services;

namespace Solvia.Tests;

/// <summary>
/// R5 Permit PDF tests (REDO-PLAN §3 R5, checklist items 43–45):
/// the gate over HTTP-shaped calls (unknown 404, non-owner 403,
/// owned-but-unpaid 400 in plain words, live 200 %PDF), the
/// document's contents read back out of the real bytes with
/// pdftotext (permit number, plate + state, PayPal ids, category,
/// the privilege line, EXEMPT (Employee) on Elena's permit), and
/// regeneration — two consecutive calls, two fresh PDFs.
/// </summary>
public sealed class PermitPdfTests
{
    // ------------------------------------------------------- harness

    private sealed record World(
        SolviaDbContext Db,
        SqliteConnection Connection,
        IFeeCatalog Catalog,
        ApplicationService Applications,
        IIssuanceService Issuance);

    private static World NewWorld()
    {
        var (db, connection) = RedoTestDb.Create();
        var fees = new FeeCatalogService(RedoTestDb.FindFeesCsv());
        var numbers = new PermitNumberService(db);
        var issuance = new IssuanceService(numbers);
        var applications = new ApplicationService(
            db,
            fees,
            new PricingService(fees),
            new UserCategoryResolver(Options.Create(new CategoryOptions())),
            new TermDatesService(fees, db),
            issuance);
        return new World(db, connection, fees, applications, issuance);
    }

    private static MyPermitsController NewController(World w, string asStudent)
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("solvia.test");
        http.Request.Headers.Cookie = $"solvia_student={asStudent}";
        return new MyPermitsController(w.Db, new PermitPdfService())
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
    }

    private static Vehicle AddVehicle(
        SolviaDbContext db, string studentId, string plate)
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

    private static async Task<PermitApplication> ApplyAsync(
        World w, string studentId, Guid vehicleId, string campus,
        PermitTypeCode type, DateOnly start)
    {
        var result = await w.Applications.CreateAsync(new ApplicationInput(
            studentId, vehicleId, campus, type, TermKind.Fall, start, "form"));
        Assert.Equal(CreateApplicationStatus.Created, result.Status);
        return result.Application!;
    }

    /// <summary>Same stand-in for R6's capture write as in MyPermitsTests.</summary>
    private static async Task MarkPaidAsCaptureWouldAsync(
        World w, PermitApplication app, string orderId, string captureId)
    {
        app.PaymentStatus = PaymentStatus.Paid;
        app.PayPalOrderId = orderId;
        app.PayPalCaptureId = captureId;
        app.PaidAtUtc = DateTime.UtcNow;
        await w.Issuance.IssueIfAbsentAsync(app);
        app.ConcurrencyToken = Guid.NewGuid();
        await w.Db.SaveChangesAsync();
    }

    /// <summary>Maria's PAID Fresno Term permit (the $30 demo scene, paid).</summary>
    private static async Task<PermitApplication> MariaPaidAsync(World w)
    {
        var vehicle = AddVehicle(w.Db, "maria", "8XYZ123");
        var app = await ApplyAsync(w, "maria", vehicle.Id, "Fresno City College",
            PermitTypeCode.Term, new DateOnly(2026, 8, 24));
        await MarkPaidAsCaptureWouldAsync(w, app, "ORDER-R5PDF", "CAPTURE-R5PDF");
        return app;
    }

    /// <summary>
    /// Reads the text layer out of real PDF bytes (poppler's
    /// pdftotext — the house verification tool for generated PDFs).
    /// </summary>
    private static string PdfText(byte[] bytes)
    {
        var pdfPath = Path.Combine(
            Path.GetTempPath(), $"solvia-r5-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(pdfPath, bytes);
        try
        {
            var psi = new ProcessStartInfo("pdftotext", $"\"{pdfPath}\" -")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("pdftotext did not start");
            var text = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0,
                "pdftotext failed — PDF text assertions need poppler-utils.");
            return text;
        }
        finally
        {
            File.Delete(pdfPath);
        }
    }

    private static PermitApplication LoadForPdf(World w, Guid id) =>
        w.Db.PermitApplications
            .Include(a => a.Vehicle)
            .Include(a => a.PermitType)
            .Include(a => a.Student)
            .Single(a => a.Id == id);

    // ----------------------------------------------------- the gates

    [Fact]
    public async Task Pdf_UnknownId_Is404()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;

        var result = await NewController(w, "maria")
            .PermitPdf(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Pdf_NonOwner_Is403()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var app = await MariaPaidAsync(w);

        var result = await NewController(w, "diego")
            .PermitPdf(app.Id, CancellationToken.None);
        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, status.StatusCode);
    }

    [Fact]
    public async Task Pdf_OwnedButUnpaid_Is400InPlainWords()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var vehicle = AddVehicle(w.Db, "maria", "8XYZ123");
        var pending = await ApplyAsync(w, "maria", vehicle.Id,
            "Fresno City College", PermitTypeCode.Term, new DateOnly(2026, 8, 24));

        var result = await NewController(w, "maria")
            .PermitPdf(pending.Id, CancellationToken.None);
        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, content.StatusCode);
        Assert.Contains("only after payment", content.Content);
    }

    [Fact]
    public async Task Pdf_LivePermit_ServesPdfNamedFromReceiptNumber()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var app = await MariaPaidAsync(w);

        var result = await NewController(w, "maria")
            .PermitPdf(app.Id, CancellationToken.None);
        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal($"{app.ReceiptNumber}.pdf", file.FileDownloadName);
        Assert.True(file.FileContents.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(file.FileContents, 0, 4));
    }

    [Fact]
    public async Task Pdf_Regenerates_TwoConsecutiveCallsBothServe()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var app = await MariaPaidAsync(w);
        var controller = NewController(w, "maria");

        var first = Assert.IsType<FileContentResult>(
            await controller.PermitPdf(app.Id, CancellationToken.None));
        var second = Assert.IsType<FileContentResult>(
            await controller.PermitPdf(app.Id, CancellationToken.None));

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(first.FileContents, 0, 4));
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(second.FileContents, 0, 4));
        Assert.True(second.FileContents.Length > 1000);
    }

    // -------------------------------------------------- the document

    [Fact]
    public async Task Pdf_PaidPermit_TextCarriesTheFacts()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var app = await MariaPaidAsync(w);

        var bytes = new PermitPdfService()
            .BuildPermitPdf(LoadForPdf(w, app.Id), "https://solvia.test");
        var text = PdfText(bytes);

        Assert.Contains(app.ReceiptNumber!, text);          // permit number
        Assert.Contains("8XYZ123", text);                   // plate
        Assert.Contains("(CA)", text);                      // plate state
        Assert.Contains("ORDER-R5PDF", text);               // PayPal order id
        Assert.Contains("CAPTURE-R5PDF", text);             // PayPal capture id
        Assert.Contains("Student", text);                   // category
        Assert.Contains("Student/Public Parking Eligible", text); // privilege line
        Assert.Contains("PAID $30.00 USD", text);           // payment line
        Assert.Contains("Proof of Parking (For Enforcement)", text);
        Assert.Contains("solvia.test/verify?t=", text);     // verify URL
    }

    [Fact]
    public async Task Pdf_ElenaEmployeePermit_SaysExemptAndStaff()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var vehicle = AddVehicle(w.Db, "elena", "7ELN777");
        // Elena (Employee) at a PAID campus: the category snapshot
        // resolves the price to $0, so her permit issues free at
        // creation — NotRequired, never near a checkout.
        var app = await ApplyAsync(w, "elena", vehicle.Id, "Fresno City College",
            PermitTypeCode.Term, new DateOnly(2026, 8, 24));
        Assert.Equal(PaymentStatus.NotRequired, app.PaymentStatus);
        Assert.Equal(UserCategory.Employee, app.Category);
        Assert.NotNull(app.ReceiptNumber);

        // …and her own controller call passes the live gate.
        var served = await NewController(w, "elena")
            .PermitPdf(app.Id, CancellationToken.None);
        Assert.IsType<FileContentResult>(served);

        var text = PdfText(new PermitPdfService()
            .BuildPermitPdf(LoadForPdf(w, app.Id), "https://solvia.test"));
        Assert.Contains("EXEMPT (Employee)", text);         // item 44 payment line
        Assert.Contains("Staff Parking Eligible", text);    // privilege line
        Assert.Contains("Employee", text);                  // category row
        Assert.Contains("7ELN777", text);
    }

    [Fact]
    public async Task Pdf_FreeStudentPermit_SaysFreeIssuedNamingCampus()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var vehicle = AddVehicle(w.Db, "priya", "9PRI999");
        var freeCampus = w.Catalog.All().First(f => f.IsFree).Campus;
        var app = await ApplyAsync(w, "priya", vehicle.Id, freeCampus,
            PermitTypeCode.Term, new DateOnly(2026, 8, 24));
        Assert.Equal(PaymentStatus.NotRequired, app.PaymentStatus);

        var text = PdfText(new PermitPdfService()
            .BuildPermitPdf(LoadForPdf(w, app.Id), "https://solvia.test"));
        Assert.Contains("FREE — ISSUED", text);
        Assert.Contains(freeCampus, text); // the $0 campus is named
    }
}
