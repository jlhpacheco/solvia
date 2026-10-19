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
using Solvia.Models.ViewModels;
using Solvia.Services;

namespace Solvia.Tests;

/// <summary>
/// R5 My Permits + Receipt page tests (REDO-PLAN §3 R5, checklist
/// items 36–42): tiles computed from the person's own rows, Duvara's
/// column set and payment words on the row model, Pay Now only when
/// money is owed, the PDF action only on live rows, newest-first,
/// and the Receipt page's friendly behavior.
///
/// Rows are produced by the real ApplicationService (never
/// hand-inserted). The one Paid row is finished with the exact
/// state R6's capture path writes — Paid + order/capture ids +
/// PaidAtUtc, then issuance — because the capture itself is R6's
/// test surface; what R5 owes is the display of that truth.
/// </summary>
public sealed class MyPermitsTests
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

    /// <summary>Creates one application through the real creation brain.</summary>
    private static async Task<PermitApplication> ApplyAsync(
        World w, string studentId, Guid vehicleId, string campus,
        PermitTypeCode type, DateOnly start)
    {
        var result = await w.Applications.CreateAsync(new ApplicationInput(
            studentId, vehicleId, campus, type, TermKind.Fall, start, "form"));
        Assert.Equal(CreateApplicationStatus.Created, result.Status);
        return result.Application!;
    }

    /// <summary>
    /// Stands in for R6's COMPLETED-capture write (CheckoutService
    /// is R6's file): Paid + the PayPal ids + the paid stamp, then
    /// issuance through the one mint path, saved in one unit of work
    /// — R5 displays exactly this state.
    /// </summary>
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

    /// <summary>
    /// Maria's three-row scenario: a $30 Fresno Term still pending,
    /// a FREE-campus Term already issued, and a $6 day pass paid —
    /// submitted oldest → newest as paid, pending, free.
    /// </summary>
    private sealed record Scenario(
        PermitApplication Pending, PermitApplication Free, PermitApplication Paid);

    private static async Task<Scenario> MariaScenarioAsync(World w)
    {
        var vehicle = AddVehicle(w.Db, "maria");
        var freeCampus = w.Catalog.All().First(f => f.IsFree).Campus;
        var dayCampus = w.Catalog.All()
            .First(f => f.Campus.Contains("Bakersfield")).Campus;

        var paid = await ApplyAsync(w, "maria", vehicle.Id, dayCampus,
            PermitTypeCode.Daily, new DateOnly(2026, 8, 24));
        await MarkPaidAsCaptureWouldAsync(w, paid, "ORDER-R5TEST", "CAPTURE-R5TEST");

        var pending = await ApplyAsync(w, "maria", vehicle.Id, "Fresno City College",
            PermitTypeCode.Term, new DateOnly(2026, 8, 24));

        var free = await ApplyAsync(w, "maria", vehicle.Id, freeCampus,
            PermitTypeCode.Term, new DateOnly(2026, 8, 25));

        // Deterministic newest-first order: paid < pending < free.
        var now = DateTime.UtcNow;
        paid.SubmittedAtUtc = now.AddHours(-3);
        pending.SubmittedAtUtc = now.AddHours(-2);
        free.SubmittedAtUtc = now.AddHours(-1);
        await w.Db.SaveChangesAsync();
        return new Scenario(pending, free, paid);
    }

    // ------------------------------------------------------------ My

    [Fact]
    public async Task My_TilesAndRows_FromOwnApplicationsOnly()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var scenario = await MariaScenarioAsync(w);

        var result = await NewController(w, "maria").My(CancellationToken.None);
        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<MyPermitsViewModel>(view.Model);

        Assert.Equal(3, model.TotalCount);
        Assert.Equal(1, model.PaidCount);
        Assert.Equal(1, model.PendingPaymentCount); // only the $30 Term owes
        Assert.Equal(3, model.Rows.Count);

        // Newest first: free, pending, paid.
        Assert.Equal(
            new[] { scenario.Free.Id, scenario.Pending.Id, scenario.Paid.Id },
            model.Rows.Select(r => r.Id).ToArray());
    }

    [Fact]
    public async Task My_RowFlagsAndPaymentWords()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        await MariaScenarioAsync(w);

        var result = await NewController(w, "maria").My(CancellationToken.None);
        var model = Assert.IsType<MyPermitsViewModel>(
            Assert.IsType<ViewResult>(result).Model);
        var byId = model.Rows.ToDictionary(r => r.Id);

        var pending = model.Rows.Single(r => r.PaymentDisplay.StartsWith("Pending"));
        Assert.Equal("Pending ($30.00)", pending.PaymentDisplay);
        Assert.True(pending.ShowPayNow);          // item 39: money owed
        Assert.False(pending.ShowPdf);            // item 40: not live
        Assert.False(pending.IsLive);
        Assert.Equal("Student", pending.CategoryLabel);
        Assert.Equal("8XYZ123 (CA)", pending.VehicleDisplay);
        Assert.Contains("Term / semester permit", pending.TypeName);

        var paid = model.Rows.Single(r => r.PaymentDisplay.StartsWith("Paid"));
        Assert.Equal("Paid ($6.00)", paid.PaymentDisplay);
        Assert.False(paid.ShowPayNow);
        Assert.True(paid.ShowPdf);
        Assert.NotNull(paid.ReceiptNumber);

        var free = model.Rows.Single(r => r.PaymentDisplay == "Not Required");
        Assert.Equal("Not Required", free.PaymentDisplay); // spelled out, no amount
        Assert.False(free.ShowPayNow);
        Assert.True(free.ShowPdf);
        Assert.NotNull(free.ReceiptNumber);
        Assert.Equal(3, byId.Count);
    }

    [Fact]
    public async Task My_AnotherPersonSeesNoneOfMariasRows()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        await MariaScenarioAsync(w);

        var result = await NewController(w, "diego").My(CancellationToken.None);
        var model = Assert.IsType<MyPermitsViewModel>(
            Assert.IsType<ViewResult>(result).Model);

        Assert.Equal(0, model.TotalCount);
        Assert.Equal(0, model.PaidCount);
        Assert.Equal(0, model.PendingPaymentCount);
        Assert.Empty(model.Rows); // the view renders "No applications yet"
    }

    // -------------------------------------------------------- Receipt

    [Fact]
    public async Task Receipt_LivePermit_ShowsNumberAndPayPalIds()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var scenario = await MariaScenarioAsync(w);

        var result = await NewController(w, "maria")
            .Receipt(scenario.Paid.Id, CancellationToken.None);
        var model = Assert.IsType<ReceiptViewModel>(
            Assert.IsType<ViewResult>(result).Model);

        Assert.True(model.IsLive);
        Assert.False(model.IsFree);
        Assert.Equal(scenario.Paid.ReceiptNumber, model.ReceiptNumber);
        Assert.Equal("ORDER-R5TEST", model.PayPalOrderId);
        Assert.Equal("CAPTURE-R5TEST", model.PayPalCaptureId);
        Assert.Equal("$6.00", model.TotalDisplay);
        Assert.Contains("solvia.test/verify?t=", model.VerifyUrl);
    }

    [Fact]
    public async Task Receipt_UnpaidOwned_IsFriendlyNotAnError()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var scenario = await MariaScenarioAsync(w);

        var result = await NewController(w, "maria")
            .Receipt(scenario.Pending.Id, CancellationToken.None);
        var model = Assert.IsType<ReceiptViewModel>(
            Assert.IsType<ViewResult>(result).Model);

        Assert.False(model.IsLive);
        Assert.Null(model.ReceiptNumber);
        Assert.Equal("$30.00", model.TotalDisplay); // the amount still owed
    }

    [Fact]
    public async Task Receipt_OtherPersonsOrUnknown_Is404()
    {
        var w = NewWorld();
        using var _ = w.Connection;
        using var __ = w.Db;
        var scenario = await MariaScenarioAsync(w);
        var asDiego = NewController(w, "diego");

        Assert.IsType<NotFoundResult>(await asDiego
            .Receipt(scenario.Paid.Id, CancellationToken.None));
        Assert.IsType<NotFoundResult>(await NewController(w, "maria")
            .Receipt(Guid.NewGuid(), CancellationToken.None));
    }
}
