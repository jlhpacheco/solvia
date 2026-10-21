using System.Data.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Solvia.Config;
using Solvia.Controllers;
using Solvia.Data;
using Solvia.Models;
using Solvia.Models.Enums;
using Solvia.Models.ViewModels;
using Solvia.Services;

namespace Solvia.Tests;

/// <summary>Offline history and real SQLite transaction regressions; no provider HTTP.</summary>
public sealed class VehiclePurchaseLockTests
{
    private static ApplicationService Applications(SolviaDbContext db, IFeeCatalog catalog) => new(
        db, catalog, new PricingService(catalog), new UserCategoryResolver(Options.Create(new CategoryOptions())),
        new TermDatesService(catalog, db), new IssuanceService(new PermitNumberService(db)));

    private static async Task<PermitApplication> Draft(SolviaDbContext db, IFeeCatalog catalog, string state)
    {
        var service = Applications(db, catalog);
        var vehicle = await service.ResolveVehicleForDraftAsync("maria", "LOCK742", "Volvo", "760", "White", "CA");
        var app = (await service.CreateAsync(new ApplicationInput("maria", vehicle.Id, "Fresno City College",
            PermitTypeCode.Daily, TermKind.Fall, new DateOnly(2026, 10, 20), "fallback", true))).Application!;
        if (state == "order") app.PayPalOrderId = "OFFLINE-VEHICLE-ORDER";
        if (state == "paid") app.PaymentStatus = PaymentStatus.Paid; // no receipt: Paid independently protects it
        if (state == "issued")
        {
            app.PaymentStatus = PaymentStatus.NotRequired; app.PriceCents = 0;
            await new IssuanceService(new PermitNumberService(db)).IssueIfAbsentAsync(app);
        }
        await db.SaveChangesAsync();
        await db.Entry(app).Reference(a => a.Vehicle).LoadAsync();
        await db.Entry(app).Reference(a => a.PermitType).LoadAsync();
        return app;
    }

    private sealed class TempDataStub : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private static VehiclesController Garage(SolviaDbContext db)
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.Cookie = "solvia_student=maria";
        return new VehiclesController(db)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, new TempDataStub())
        };
    }

    private static VehicleEditViewModel Form(string change = "none") => new()
    {
        Plate = change == "plate" ? "CHANGED7" : "LOCK742",
        State = change == "state" ? UsState.NV : UsState.CA,
        Make = change == "make" ? "Honda" : "Volvo",
        Model = change == "model" ? "740" : "760",
        Color = change == "color" ? VehicleColor.Red : VehicleColor.White
    };

    private static Task<Vehicle> Resolve(SolviaDbContext db, IFeeCatalog catalog, VehicleEditViewModel form) =>
        Applications(db, catalog).ResolveVehicleForDraftAsync("maria", form.Plate, form.Make, form.Model,
            form.Color.ToString(), form.State.ToString());

    [Theory]
    [InlineData("order", "make", false)]
    [InlineData("order", "model", false)]
    [InlineData("order", "color", false)]
    [InlineData("paid", "model", false)]
    [InlineData("issued", "color", false)]
    [InlineData("order", "make", true)]
    [InlineData("order", "model", true)]
    [InlineData("order", "color", true)]
    [InlineData("paid", "model", true)]
    [InlineData("issued", "color", true)]
    public async Task BothDoors_RejectDescriptiveEditsToPaymentOrIssuedHistory(string state, string change, bool garage)
    {
        var (db, connection) = RedoTestDb.Create();
        using var _ = connection; using var __ = db;
        var catalog = new FeeCatalogService(RedoTestDb.FindFeesCsv());
        var app = await Draft(db, catalog, state);
        var before = CheckoutReviewSnapshot.Compute(app);
        var receipt = app.ReceiptNumber;
        if (garage)
        {
            var view = Assert.IsType<ViewResult>(await Garage(db).Edit(app.VehicleId, Form(change), default));
            Assert.Equal(VehiclePurchaseLockedException.PublicMessage,
                Assert.IsType<VehicleEditViewModel>(view.Model).Error);
        }
        else
            await Assert.ThrowsAsync<VehiclePurchaseLockedException>(() => Resolve(db, catalog, Form(change)));

        await db.Entry(app.Vehicle!).ReloadAsync();
        Assert.Equal(before, CheckoutReviewSnapshot.Compute(app));
        Assert.Equal(receipt, (await db.PermitApplications.AsNoTracking().SingleAsync()).ReceiptNumber);
        Assert.False(db.ChangeTracker.HasChanges());
    }

    [Theory]
    [InlineData("order", false)]
    [InlineData("paid", false)]
    [InlineData("issued", false)]
    [InlineData("order", true)]
    [InlineData("paid", true)]
    [InlineData("issued", true)]
    public async Task IdenticalReuse_RemainsAllowed_WithCallerTransaction(string state, bool garage)
    {
        var (db, connection) = RedoTestDb.Create();
        using var _ = connection; using var __ = db;
        var catalog = new FeeCatalogService(RedoTestDb.FindFeesCsv());
        var app = await Draft(db, catalog, state);
        var before = CheckoutReviewSnapshot.Compute(app);
        await using var transaction = await db.Database.BeginTransactionAsync();
        if (garage) Assert.IsType<RedirectToActionResult>(await Garage(db).Edit(app.VehicleId, Form(), default));
        else Assert.Equal(app.VehicleId, (await Resolve(db, catalog, Form())).Id);
        Assert.Same(transaction, db.Database.CurrentTransaction);
        await transaction.CommitAsync();
        Assert.Equal(before, CheckoutReviewSnapshot.Compute(app));
        Assert.Single(db.Vehicles);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnboundPendingDraft_CanStillUpdateDescription_ButNotReferencedIdentity(bool garage)
    {
        var (db, connection) = RedoTestDb.Create();
        using var _ = connection; using var __ = db;
        var catalog = new FeeCatalogService(RedoTestDb.FindFeesCsv());
        var app = await Draft(db, catalog, "pending");
        if (garage) Assert.IsType<RedirectToActionResult>(await Garage(db).Edit(app.VehicleId, Form("model"), default));
        else await Resolve(db, catalog, Form("model"));
        Assert.Equal("740", app.Vehicle!.Model);
        if (garage)
        {
            var held = Assert.IsType<ViewResult>(await Garage(db).Edit(app.VehicleId, Form("state"), default));
            Assert.Contains("different plate or state", Assert.IsType<VehicleEditViewModel>(held.Model).Error);
        }
        else await Assert.ThrowsAsync<DraftVehicleStateLockedException>(() => Resolve(db, catalog, Form("state")));
        await db.Entry(app.Vehicle).ReloadAsync();
        Assert.Equal(UsState.CA, app.Vehicle.State);
        Assert.Equal("740", app.Vehicle.Model);
    }

    private sealed class BeginSignal : DbTransactionInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection,
            TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class PausedPayPal : IPayPalService
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Creates;
        public async Task<PayPalOrderResult> CreateOrderAsync(int amountCents, string reference,
            string? returnUrl = null, string? cancelUrl = null, CancellationToken cancellationToken = default)
        {
            Creates++; Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new("OFFLINE-RACE-ORDER", "CREATED", "https://www.sandbox.paypal.com/checkoutnow?token=OFFLINE-RACE-ORDER");
        }
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("No HTTP.");
        public Task<PayPalOrderResult> GetOrderAsync(string id, CancellationToken cancellationToken = default) => throw new InvalidOperationException("No lookup.");
        public Task<PayPalCaptureResult> CaptureOrderAsync(string id, CancellationToken cancellationToken = default) => throw new InvalidOperationException("No capture.");
        public Task<bool> VerifyWebhookSignatureAsync(string a, string b, string c, string d, string e, string f,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("No webhook.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CheckoutWriteTransaction_WinsBeforeConcurrentVehicleEdit(bool garage)
    {
        var path = Path.Combine(Path.GetTempPath(), $"solvia-vehicle-lock-{Guid.NewGuid():N}.db");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false, DefaultTimeout = 10 }.ToString();
        var options = new DbContextOptionsBuilder<SolviaDbContext>().UseSqlite(connectionString).Options;
        var catalog = new FeeCatalogService(RedoTestDb.FindFeesCsv());
        try
        {
            await using var checkoutDb = new SolviaDbContext(options);
            await checkoutDb.Database.EnsureCreatedAsync();
            var app = await Draft(checkoutDb, catalog, "pending");
            var original = CheckoutReviewSnapshot.Compute(app);
            var provider = new PausedPayPal();
            var checkout = new CheckoutService(checkoutDb, provider, new IssuanceService(new PermitNumberService(checkoutDb)),
                catalog, new PricingService(catalog), NullLogger<CheckoutService>.Instance);
            var start = checkout.StartAsync(app.Id, null, null, reviewedSnapshot: original);
            await provider.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            var signal = new BeginSignal();
            var edit = Task.Run(async () =>
            {
                await using var editDb = new SolviaDbContext(new DbContextOptionsBuilder<SolviaDbContext>()
                    .UseSqlite(connectionString).AddInterceptors(signal).Options);
                if (garage)
                {
                    var held = Assert.IsType<ViewResult>(await Garage(editDb).Edit(app.VehicleId, Form("model"), default));
                    Assert.Equal(VehiclePurchaseLockedException.PublicMessage, Assert.IsType<VehicleEditViewModel>(held.Model).Error);
                }
                else await Assert.ThrowsAsync<VehiclePurchaseLockedException>(() => Resolve(editDb, catalog, Form("model")));
            });
            try { await signal.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15)); }
            finally { provider.Release.TrySetResult(); }
            Assert.Equal(CheckoutStartKind.OrderReady, (await start.WaitAsync(TimeSpan.FromSeconds(15))).Kind);
            await edit.WaitAsync(TimeSpan.FromSeconds(15));
            await checkoutDb.Entry(app.Vehicle!).ReloadAsync();
            Assert.Equal(original, CheckoutReviewSnapshot.Compute(app));
            Assert.Equal("760", app.Vehicle!.Model);
            Assert.Equal("OFFLINE-RACE-ORDER", app.PayPalOrderId);
            Assert.Equal(1, provider.Creates);
        }
        finally
        {
            foreach (var file in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(file)) File.Delete(file);
        }
    }
}
