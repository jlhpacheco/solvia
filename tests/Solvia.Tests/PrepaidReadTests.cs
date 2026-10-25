using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Solvia.Controllers;
using Solvia.Data;
using Solvia.Models;
using Solvia.Models.Enums;
using Solvia.Models.ViewModels;
using Solvia.Services;

namespace Solvia.Tests;

/// <summary>Offline saved-record/PDF checks; these fixtures are not provider transactions.</summary>
public sealed class PrepaidReadTests
{
    private static readonly DateOnly PacificToday = new(2026, 11, 1);
    private static readonly DateTimeOffset Instant = new(2026, 11, 2, 7, 30, 0, TimeSpan.Zero);
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Instant;
    }
    private sealed class World : IDisposable
    {
        public SolviaDbContext Db { get; }
        private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
        public World() => (Db, _connection) = RedoTestDb.Create();
        public MyPermitsController Controller(string owner = "maria")
        {
            var http = new DefaultHttpContext();
            http.Request.Scheme = "https"; http.Request.Host = new HostString("solvia.test");
            http.Request.Headers.Cookie = "solvia_student=" + owner;
            return new(Db, new PermitPdfService(), new Clock())
            {
                ControllerContext = new ControllerContext { HttpContext = http }
            };
        }
        public PrepaidPurchase AddGroup(PaymentStatus status = PaymentStatus.Paid, string owner = "maria")
        {
            var exempt = status == PaymentStatus.NotRequired;
            var referenceSuffix = Db.Set<PrepaidPurchase>().Any() ? "-" + Guid.NewGuid().ToString("N")[..8] : "";
            var vehicle = new Vehicle
            {
                Id = Guid.NewGuid(), StudentId = owner, Plate = "PREPAID-DEMO", State = UsState.CA,
                Make = "Honda", Model = "Civic", Color = VehicleColor.Blue
            };
            var purchase = new PrepaidPurchase
            {
                Id = Guid.NewGuid(), StudentId = owner, VehicleId = vehicle.Id, Vehicle = vehicle,
                Campus = "Fresno City College", Category = owner == "elena" ? UserCategory.Employee
                    : owner == "pat" ? UserCategory.Public : UserCategory.Student,
                Term = TermKind.Fall, UnitPriceCents = exempt ? 0 : 100, TotalCents = exempt ? 0 : 300,
                PaymentStatus = status, SubmittedAtUtc = Instant.UtcDateTime.AddDays(-5),
                ConcurrencyToken = Guid.NewGuid(), DraftKey = Guid.NewGuid().ToString("N"),
                PayPalOrderId = status == PaymentStatus.Paid ? "OFFLINE-SHARED-ORDER" + referenceSuffix : null,
                PayPalCaptureId = status == PaymentStatus.Paid ? "OFFLINE-SHARED-CAPTURE" + referenceSuffix : null,
                PaidAtUtc = status == PaymentStatus.Paid ? Instant.UtcDateTime.AddDays(-2) : null
            };
            for (var index = 0; index < 3; index++)
            {
                var issued = status is PaymentStatus.Paid or PaymentStatus.NotRequired;
                var date = PacificToday.AddDays(index - 1);
                var application = new PermitApplication
                {
                    Id = Guid.NewGuid(), StudentId = owner, VehicleId = vehicle.Id, Vehicle = vehicle,
                    Campus = purchase.Campus, Category = purchase.Category, PermitTypeId = 1, Term = TermKind.Fall,
                    StartDate = date, EndDate = date, PriceCents = purchase.UnitPriceCents, PaymentStatus = status,
                    Status = ApplicationStatus.Pending, Source = "form", SubmittedAtUtc = purchase.SubmittedAtUtc,
                    ConcurrencyToken = Guid.NewGuid(), PaidAtUtc = purchase.PaidAtUtc,
                    ReceiptNumber = issued ? $"OFFLINE-{purchase.Id:N}"[..20] + "-" + index : null,
                    ReceiptIssuedAtUtc = issued ? purchase.SubmittedAtUtc : null,
                    VerifyToken = issued ? "offline-" + Guid.NewGuid().ToString("N") : null
                };
                var item = new PrepaidPurchaseItem
                {
                    PurchaseId = purchase.Id, Purchase = purchase, ApplicationId = application.Id, Application = application
                };
                application.PrepaidItem = item;
                purchase.Items.Add(item);
            }
            Db.Set<PrepaidPurchase>().Add(purchase);
            Db.SaveChanges();
            return purchase;
        }
        public void Dispose() { Db.Dispose(); _connection.Dispose(); }
    }
    private static string Text(byte[] bytes)
    {
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(bytes);
        return string.Join("\n", pdf.GetPages().Select(page => string.Join(" ", page.GetWords().Select(word => word.Text))));
    }
    private static PermitApplication Load(SolviaDbContext db, Guid id) => db.PermitApplications
        .Include(application => application.Student).Include(application => application.Vehicle).Include(application => application.PermitType)
        .Include(application => application.PrepaidItem).ThenInclude(item => item!.Purchase).ThenInclude(purchase => purchase!.Items)
        .Single(application => application.Id == id);

    [Fact]
    public async Task ReceiptAndPdfReadSharedReferencesWithoutWritingThemToTheChild()
    {
        using var world = new World();
        var purchase = world.AddGroup(); var id = purchase.Items[2].ApplicationId;
        world.Db.ChangeTracker.Clear();
        var receipt = Assert.IsType<ReceiptViewModel>(Assert.IsType<ViewResult>(
            await world.Controller().Receipt(id, default)).Model);
        Assert.Equal(purchase.Id, receipt.PurchaseId);
        Assert.Equal("$1.00", receipt.TotalDisplay);
        Assert.Equal("$3.00", receipt.PurchaseTotalDisplay);
        Assert.Equal(3, receipt.PurchaseDayCount);
        Assert.Equal("OFFLINE-SHARED-ORDER", receipt.PayPalOrderId);
        Assert.Equal("OFFLINE-SHARED-CAPTURE", receipt.PayPalCaptureId);
        Assert.Equal("Upcoming · Paid", receipt.StatusDisplay);
        Assert.False(receipt.IsActive); Assert.True(receipt.IsLive);
        var served = Assert.IsType<FileContentResult>(await world.Controller().PermitPdf(id, default));
        var text = Text(served.FileContents);
        Assert.Contains("OFFLINE-SHARED-ORDER", text); Assert.Contains("OFFLINE-SHARED-CAPTURE", text);
        Assert.Contains(purchase.Id.ToString(), text); Assert.Contains("Overall purchase total", text);
        Assert.Contains("$3.00 USD for 3 dates", text); Assert.Contains("this date's fee $1.00 USD", text);
        Assert.DoesNotContain("PAID $3.00 USD", text);
        var child = await world.Db.PermitApplications.SingleAsync(application => application.Id == id);
        Assert.Null(child.PayPalOrderId); Assert.Null(child.PayPalCaptureId); Assert.Equal(100, child.PriceCents);
        Assert.Equal("OFFLINE-SHARED-ORDER", child.PaymentOrderReference);
        Assert.Equal(300, child.PurchaseTotalCents);
        Assert.DoesNotContain(world.Db.ChangeTracker.Entries(), entry => entry.State == EntityState.Modified);
        WriteEvidence("future-paid-permit.pdf", served.FileContents);
    }

    [Fact]
    public async Task MyPermitsUsesPacificDateAndDistinguishesPaidValidityWithoutHidingPdfs()
    {
        using var world = new World(); var purchase = world.AddGroup(); world.Db.ChangeTracker.Clear();
        var model = Assert.IsType<MyPermitsViewModel>(Assert.IsType<ViewResult>(await world.Controller().My(default)).Model);
        Assert.Equal(3, model.TotalCount); Assert.Equal(3, model.PaidCount); Assert.Equal(0, model.PendingPaymentCount);
        var byDate = purchase.Items.ToDictionary(item => item.Application.StartDate, item => item.ApplicationId);
        Assert.Equal("Valid today", model.Rows.Single(row => row.Id == byDate[PacificToday]).StatusDisplay);
        Assert.Equal("Upcoming · Paid", model.Rows.Single(row => row.Id == byDate[PacificToday.AddDays(1)]).StatusDisplay);
        Assert.Equal("Expired · Paid", model.Rows.Single(row => row.Id == byDate[PacificToday.AddDays(-1)]).StatusDisplay);
        Assert.All(model.Rows, row => { Assert.True(row.ShowPdf); Assert.Equal(purchase.Id, row.PurchaseId); });
        Assert.Single(model.Rows, row => row.ShowPurchaseActions);
        Assert.DoesNotContain(model.Rows, row => row.ShowPayNow);
    }

    [Fact]
    public async Task PendingBundleHasOneGroupPayActionAndNoIssuedPdf()
    {
        using var world = new World(); var purchase = world.AddGroup(PaymentStatus.Pending); world.Db.ChangeTracker.Clear();
        var model = Assert.IsType<MyPermitsViewModel>(Assert.IsType<ViewResult>(await world.Controller().My(default)).Model);
        Assert.Equal(1, model.PendingPaymentCount);
        var pay = Assert.Single(model.Rows, row => row.ShowPayNow);
        Assert.Equal("/Prepaid/Review/" + purchase.Id, pay.PaymentUrl);
        Assert.All(model.Rows, row => Assert.False(row.ShowPdf));
        var id = purchase.Items[0].ApplicationId;
        var receipt = Assert.IsType<ReceiptViewModel>(Assert.IsType<ViewResult>(await world.Controller().Receipt(id, default)).Model);
        Assert.True(receipt.CanPay); Assert.False(receipt.IsLive); Assert.Equal(pay.PaymentUrl, receipt.PaymentUrl);
        var deniedPdf = Assert.IsType<ContentResult>(await world.Controller().PermitPdf(id, default));
        Assert.Equal(400, deniedPdf.StatusCode);
    }

    [Fact]
    public async Task ForeignPersonCannotReadAnotherOwnersBundledReceiptPdfOrList()
    {
        using var world = new World(); var purchase = world.AddGroup(); var foreign = world.Controller("diego");
        Assert.IsType<NotFoundResult>(await foreign.Receipt(purchase.Items[0].ApplicationId, default));
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(await foreign.PermitPdf(purchase.Items[0].ApplicationId, default)).StatusCode);
        var mine = Assert.IsType<MyPermitsViewModel>(Assert.IsType<ViewResult>(await foreign.My(default)).Model);
        Assert.Empty(mine.Rows);
    }

    [Fact]
    public async Task OperationsUsesSharedCaptureAndPerDayPricesWithoutFlaggingUpcomingAsPaymentTrouble()
    {
        using var world = new World(); var purchase = world.AddGroup(); world.Db.ChangeTracker.Clear();
        var review = await new PaymentReviewService(world.Db, new Clock()).ReadAsync();
        Assert.Equal(PacificToday, review.ValidityDate);
        Assert.Equal(3, review.PaidWithCaptureCount); Assert.Equal(300, review.PaidWithCaptureCents);
        Assert.All(review.Rows, row => { Assert.Equal(100, row.PriceCents); Assert.Equal("OFFLINE-SHARED-CAPTURE", row.CaptureId); });
        var upcoming = review.Rows.Single(row => row.StartDate > PacificToday);
        Assert.Contains("Upcoming · Paid", upcoming.ValidityReason); Assert.Empty(upcoming.Issues);
        Assert.Contains("Paid · permit inactive", review.Rows.Single(row => row.EndDate < PacificToday).Issues);
        Assert.Empty(world.Db.ChangeTracker.Entries());
        Assert.DoesNotContain("PREPAID-DEMO", JsonSerializer.Serialize(review));
        Assert.Equal(3, purchase.Items.Count);
    }

    [Fact]
    public void PacketHasOneLetterPagePerDateWithSharedReferencesAndDistinctPermitNumbers()
    {
        using var world = new World(); var purchase = world.AddGroup(); world.Db.ChangeTracker.Clear();
        var applications = purchase.Items.Select(item => Load(world.Db, item.ApplicationId)).Reverse().ToArray();
        var bytes = new PermitPdfService().BuildPermitPacket(applications, "https://solvia.test");
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(bytes);
        Assert.Equal(3, pdf.NumberOfPages);
        foreach (var page in pdf.GetPages())
        {
            Assert.InRange((double)page.Width, 611.9, 612.1); Assert.InRange((double)page.Height, 791.9, 792.1);
            var text = string.Join(" ", page.GetWords().Select(word => word.Text));
            Assert.Contains("OFFLINE-SHARED-CAPTURE", text); Assert.Contains("Overall purchase total", text);
        }
        var all = Text(bytes);
        Assert.All(applications, application => Assert.Contains(application.ReceiptNumber!, all));
        Assert.Contains("Oct 31, 2026", string.Join(" ", pdf.GetPage(1).GetWords().Select(word => word.Text)));
        WriteEvidence("paid-permit-packet.pdf", bytes);
    }

    [Fact]
    public async Task ExemptFuturePermitsCanBeDownloadedWithoutProviderReferences()
    {
        using var world = new World(); var purchase = world.AddGroup(PaymentStatus.NotRequired, "elena");
        var id = purchase.Items[2].ApplicationId;
        var model = Assert.IsType<ReceiptViewModel>(Assert.IsType<ViewResult>(await world.Controller("elena").Receipt(id, default)).Model);
        Assert.True(model.IsFree); Assert.True(model.IsLive); Assert.False(model.IsActive);
        Assert.Equal("Upcoming · Issued", model.StatusDisplay); Assert.Equal("$0.00", model.PurchaseTotalDisplay);
        var file = Assert.IsType<FileContentResult>(await world.Controller("elena").PermitPdf(id, default));
        var text = Text(file.FileContents); Assert.Contains("EXEMPT (Employee)", text);
        Assert.Contains("Not required", text); Assert.DoesNotContain("OFFLINE-SHARED-CAPTURE", text);
        var applications = purchase.Items.Select(item => Load(world.Db, item.ApplicationId)).ToArray();
        WriteEvidence("exempt-permit-packet.pdf", new PermitPdfService().BuildPermitPacket(applications, "https://solvia.test"));
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("duplicate")]
    [InlineData("foreign-owner")]
    [InlineData("other-purchase")]
    public void PacketRejectsUnpaidDuplicateOrMixedPurchaseInputs(string state)
    {
        using var world = new World(); var first = world.AddGroup();
        var applications = first.Items.Select(item => Load(world.Db, item.ApplicationId)).ToList();
        if (state == "pending") applications[1].PaymentStatus = PaymentStatus.Pending;
        if (state == "duplicate") applications[1] = applications[0];
        if (state is "foreign-owner" or "other-purchase")
        {
            var second = world.AddGroup(owner: state == "foreign-owner" ? "pat" : "maria");
            applications[1] = Load(world.Db, second.Items[0].ApplicationId);
        }
        if (state == "pending")
            Assert.Throws<InvalidOperationException>(() => new PermitPdfService().BuildPermitPacket(applications));
        else
            Assert.Throws<ArgumentException>(() => new PermitPdfService().BuildPermitPacket(applications));
    }

    [Fact]
    public void LegacyStandalonePaymentReferencesRemainAvailableWithoutAPurchase()
    {
        var app = new PermitApplication { PayPalOrderId = "LEGACY-ORDER", PayPalCaptureId = "LEGACY-CAPTURE" };
        Assert.Equal("LEGACY-ORDER", app.PaymentOrderReference); Assert.Equal("LEGACY-CAPTURE", app.PaymentCaptureReference);
        Assert.Null(app.PurchaseTotalCents);
    }

    private static void WriteEvidence(string name, byte[] bytes)
    {
        if (Environment.GetEnvironmentVariable("SOLVIA_PREPAID_READ_EVIDENCE") != "1") return;
        var repo = Directory.GetParent(Path.GetDirectoryName(RedoTestDb.FindFeesCsv())!)!.FullName;
        var directory = Path.Combine(repo, ".playwright-data", "prepaid-read");
        Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory, name), bytes);
    }
}
