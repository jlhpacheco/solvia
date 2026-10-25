using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Solvia.Config;
using Solvia.Data;
using Solvia.Models;
using Solvia.Models.Enums;
using Solvia.Services;

namespace Solvia.Tests;

/// <summary>Offline financial tests: real SQLite/issuance; PayPal is only a counting stub.</summary>
public sealed class PrepaidPurchaseTests
{
    private static readonly DateOnly Today = new(2030, 1, 1);
    private static readonly DateOnly[] Dates = [new(2030, 2, 4), new(2030, 2, 6)];
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class Catalog : IFeeCatalog
    {
        public int? DayPrice = 100;
        public CampusFee? Find(string campus) => campus.Trim().Equals("Test College", StringComparison.OrdinalIgnoreCase)
            ? new CampusFee("Test College", "$30", "", "$1", "", 3000, 3000, 3000, false, "Fictional offline test fee") { DayPassCents = DayPrice }
            : null;
        public IReadOnlyList<CampusFee> All() => [Find("Test College")!];
        public int? SemesterPriceCents(CampusFee fee, TermKind term) => 3000;
    }

    private sealed class Provider : IPayPalService
    {
        public int CreateCalls, CaptureCalls, GetCalls, LastAmount;
        public string? LastReference;
        public PayPalCaptureResult? CaptureResult;
        public Exception? CaptureException;
        public string GetStatus = "CREATED";
        public TaskCompletionSource<bool>? CreateGate, CaptureGate;
        public TaskCompletionSource<bool> CreateEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> CaptureEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException("No HTTP calls in these tests.");
        public async Task<PayPalOrderResult> CreateOrderAsync(int amountCents, string reference, string? returnUrl = null,
            string? cancelUrl = null, CancellationToken cancellationToken = default)
        {
            var number = Interlocked.Increment(ref CreateCalls);
            LastAmount = amountCents; LastReference = reference;
            CreateEntered.TrySetResult(true);
            if (CreateGate is not null) await CreateGate.Task.WaitAsync(cancellationToken);
            return new($"ORDER-{number}", "CREATED", $"https://www.sandbox.paypal.com/checkoutnow?token=ORDER-{number}");
        }
        public Task<PayPalOrderResult> GetOrderAsync(string orderId, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref GetCalls);
            return Task.FromResult(new PayPalOrderResult(orderId, GetStatus, $"https://www.sandbox.paypal.com/checkoutnow?token={orderId}"));
        }
        public async Task<PayPalCaptureResult> CaptureOrderAsync(string orderId, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref CaptureCalls);
            CaptureEntered.TrySetResult(true);
            if (CaptureGate is not null) await CaptureGate.Task.WaitAsync(cancellationToken);
            if (CaptureException is not null) throw CaptureException;
            return CaptureResult ?? new("COMPLETED", "CAPTURE-1", LastAmount, "USD");
        }
        public Task<bool> VerifyWebhookSignatureAsync(string transmissionId, string transmissionTime, string transmissionSig,
            string certUrl, string authAlgo, string webhookEventJson, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Signature verification belongs to separate webhook tests.");
    }

    private sealed class FaultIssuance(IIssuanceService inner) : IIssuanceService
    {
        public int Calls;
        public int? FailOnCall;
        public async Task<bool> IssueIfAbsentAsync(PermitApplication application, CancellationToken cancellationToken = default)
        {
            if (++Calls == FailOnCall) throw new InvalidOperationException("Controlled issuance failure.");
            return await inner.IssueIfAbsentAsync(application, cancellationToken);
        }
    }

    private sealed class World : IDisposable
    {
        public readonly Catalog Catalog = new();
        public readonly Provider Provider = new();
        public readonly Clock Time = new();
        public readonly SqliteConnection Connection;
        public readonly SolviaDbContext Db;
        public readonly FaultIssuance Issuance;
        public readonly ApplicationService Applications;
        public readonly PrepaidPurchaseService Service;
        public readonly string? FilePath;
        public World(bool file = false)
        {
            FilePath = file ? Path.Combine(Path.GetTempPath(), $"solvia-prepaid-{Guid.NewGuid():N}.db") : null;
            Connection = new SqliteConnection(FilePath is null ? "Data Source=:memory:" : $"Data Source={FilePath};Pooling=false;Default Timeout=30");
            Connection.Open();
            Db = new(new DbContextOptionsBuilder<SolviaDbContext>().UseSqlite(Connection).Options);
            Db.Database.EnsureCreated();
            Issuance = new(new IssuanceService(new PermitNumberService(Db)));
            Applications = NewApplications(Db, Issuance);
            Service = NewService(Db, Issuance);
        }
        public ApplicationService NewApplications(SolviaDbContext context, IIssuanceService? issuance = null) => new(context, Catalog,
            new PricingService(Catalog), new UserCategoryResolver(Options.Create(new CategoryOptions())), new TermDatesService(Catalog, context),
            issuance ?? new IssuanceService(new PermitNumberService(context)));
        public PrepaidPurchaseService NewService(SolviaDbContext context, IIssuanceService? issuance = null)
        {
            issuance ??= new IssuanceService(new PermitNumberService(context));
            return new(context, NewApplications(context, issuance), Provider, issuance, new PricingService(Catalog), Catalog, Time);
        }
        public SolviaDbContext Sibling() => new(new DbContextOptionsBuilder<SolviaDbContext>()
            .UseSqlite($"Data Source={FilePath};Pooling=false;Default Timeout=30").Options);
        public Vehicle Vehicle(string owner = "maria")
        {
            var vehicle = new Vehicle { Id = Guid.NewGuid(), StudentId = owner, Plate = "TEST" + Guid.NewGuid().ToString("N")[..6],
                State = UsState.CA, Color = VehicleColor.White, Make = "Volvo", Model = "760" };
            Db.Vehicles.Add(vehicle); Db.SaveChanges(); return vehicle;
        }
        public Task<PrepaidPurchase> Create(string owner = "maria", Vehicle? vehicle = null, IReadOnlyList<DateOnly>? dates = null) =>
            Service.CreateAsync(owner, (vehicle ?? Vehicle(owner)).Id, "Test College", TermKind.Spring, dates ?? Dates);
        public Task<CheckoutStartResult> Start(PrepaidPurchase purchase, string? snapshot = null) => Service.StartAsync(purchase.Id,
            snapshot ?? PrepaidPurchaseService.ReviewSnapshot(purchase), "http://localhost/return", "http://localhost/cancel");
        public void Dispose()
        {
            Db.Dispose(); Connection.Dispose();
            if (FilePath is not null && File.Exists(FilePath)) File.Delete(FilePath);
        }
    }

    [Fact]
    public async Task Create_UsesUnitFeeAndExactDates_OneStableDraftAcrossQuoteChanges()
    {
        using var w = new World();
        var vehicle = w.Vehicle();
        var first = await w.Create(vehicle: vehicle);
        Assert.Equal(100, first.UnitPriceCents); Assert.Equal(200, first.TotalCents);
        Assert.Equal(UserCategory.Student, first.Category);
        Assert.Equal(Dates, first.Items.Select(i => i.Application.StartDate).Order());
        Assert.All(first.Items, i => { Assert.Equal(i.Application.StartDate, i.Application.EndDate); Assert.Equal(100, i.Application.PriceCents); Assert.Null(i.Application.ReceiptNumber); });
        w.Catalog.DayPrice = 200;
        var retry = await w.Create(vehicle: vehicle, dates: Dates.Reverse().ToArray());
        Assert.Equal(first.Id, retry.Id); Assert.Equal(200, retry.TotalCents);
        Assert.Equal(2, await w.Db.PermitApplications.CountAsync());
        Assert.Single(await w.Db.PrepaidPurchases.ToListAsync()); Assert.Equal(0, w.Provider.CreateCalls);
    }

    [Fact]
    public async Task CompletedCapture_SettlesAllChildrenAtomically_WithOnlyOneSharedReference_AndStableRetries()
    {
        using var w = new World();
        var purchase = await w.Create(); var snapshot = PrepaidPurchaseService.ReviewSnapshot(purchase);
        var started = await w.Start(purchase);
        Assert.Equal(200, w.Provider.LastAmount); Assert.Equal($"prepaid-{purchase.Id:N}", w.Provider.LastReference);
        Assert.Equal(purchase.Id, started.ApplicationId);
        Assert.Equal(snapshot, PrepaidPurchaseService.ReviewSnapshot((await w.Service.LoadAsync(purchase.Id))!));
        var result = await w.Service.CaptureAsync(purchase.Id);
        Assert.True(result.Completed);
        var paid = (await w.Service.LoadAsync(purchase.Id))!;
        Assert.Equal(PaymentStatus.Paid, paid.PaymentStatus); Assert.Equal("CAPTURE-1", paid.PayPalCaptureId);
        Assert.Equal(new Clock().GetUtcNow().UtcDateTime, paid.PaidAtUtc);
        Assert.All(paid.Items, item =>
        {
            Assert.Equal(PaymentStatus.Paid, item.Application.PaymentStatus);
            Assert.Equal(paid.PaidAtUtc, item.Application.PaidAtUtc);
            Assert.NotNull(item.Application.ReceiptNumber); Assert.NotNull(item.Application.VerifyToken);
            Assert.Null(item.Application.PayPalOrderId); Assert.Null(item.Application.PayPalCaptureId);
            Assert.False(PermitValidity.IsActive(item.Application, Today));
            Assert.True(PermitValidity.IsActive(item.Application, item.Application.StartDate));
            Assert.False(PermitValidity.IsActive(item.Application, item.Application.StartDate.AddDays(1)));
        });
        var receipts = paid.Items.Select(i => i.Application.ReceiptNumber).ToArray();
        Assert.Equal(2, receipts.Distinct().Count());
        Assert.Equal(CheckoutStartKind.AlreadyPaid, (await w.Start(paid, snapshot)).Kind);
        Assert.True((await w.Service.CaptureAsync(paid.Id)).Completed);
        Assert.Equal(1, w.Provider.CreateCalls); Assert.Equal(1, w.Provider.CaptureCalls); Assert.Equal(0, w.Provider.GetCalls);
        Assert.Equal(receipts, (await w.Service.LoadAsync(paid.Id))!.Items.Select(i => i.Application.ReceiptNumber));
        w.Db.ChangeTracker.Clear();
        var child = await w.Db.PermitApplications.FirstAsync();
        Assert.NotNull(child.PrepaidItem); Assert.Equal(paid.Id, child.PrepaidItem.Purchase.Id);
        Assert.Equal("CAPTURE-1", child.PrepaidItem.Purchase.PayPalCaptureId);
    }

    [Theory]
    [InlineData("PENDING", PaymentStatus.Pending)]
    [InlineData("DENIED", PaymentStatus.Failed)]
    [InlineData("FAILED", PaymentStatus.Failed)]
    public async Task NonCompletedCapture_NeverIssuesAnyChild(string status, PaymentStatus expected)
    {
        using var w = new World(); var p = await w.Create(); await w.Start(p);
        w.Provider.CaptureResult = new(status, "UNSETTLED", 200, "USD");
        Assert.False((await w.Service.CaptureAsync(p.Id)).Completed);
        var saved = (await w.Service.LoadAsync(p.Id))!;
        Assert.Equal(expected, saved.PaymentStatus); Assert.Null(saved.PaidAtUtc); Assert.Null(saved.PayPalCaptureId);
        Assert.All(saved.Items, i => { Assert.Equal(expected, i.Application.PaymentStatus); Assert.Null(i.Application.ReceiptNumber); Assert.Null(i.Application.PaidAtUtc); });
    }

    [Theory]
    [InlineData(100, "USD", "CAPTURE-1")]
    [InlineData(201, "USD", "CAPTURE-1")]
    [InlineData(null, "USD", "CAPTURE-1")]
    [InlineData(200, "EUR", "CAPTURE-1")]
    [InlineData(200, null, "CAPTURE-1")]
    [InlineData(200, "USD", null)]
    [InlineData(200, "USD", " ")]
    public async Task CompletedButUnverifiedCapture_FailsEveryChild(int? amount, string? currency, string? captureId)
    {
        using var w = new World(); var p = await w.Create(); await w.Start(p);
        w.Provider.CaptureResult = new("COMPLETED", captureId, amount, currency);
        var result = await w.Service.CaptureAsync(p.Id);
        Assert.False(result.Completed); Assert.Equal("VERIFICATION_FAILED", result.Status);
        Assert.All((await w.Service.LoadAsync(p.Id))!.Items, i => { Assert.Equal(PaymentStatus.Failed, i.Application.PaymentStatus); Assert.Null(i.Application.ReceiptNumber); });
    }

    [Theory]
    [InlineData("elena", 100)]
    [InlineData("maria", 0)]
    [InlineData("pat", 0)]
    public async Task FreeOrExempt_EntireGroupIssuesWithoutProviderCalls(string owner, int published)
    {
        using var w = new World(); w.Catalog.DayPrice = published;
        var p = await w.Create(owner);
        Assert.Equal(PaymentStatus.NotRequired, p.PaymentStatus); Assert.Equal(0, p.TotalCents);
        Assert.All(p.Items, i => { Assert.Equal(PaymentStatus.NotRequired, i.Application.PaymentStatus); Assert.NotNull(i.Application.ReceiptNumber); Assert.Null(i.Application.PaidAtUtc); });
        Assert.Equal(CheckoutStartKind.NotRequired, (await w.Start(p)).Kind);
        Assert.True((await w.Service.CaptureAsync(p.Id)).Completed);
        Assert.Equal(0, w.Provider.CreateCalls + w.Provider.CaptureCalls + w.Provider.GetCalls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    public async Task MissingOrNegativeFee_BlocksEvenEmployeeWithoutCreatingRows(int? price)
    {
        using var w = new World(); w.Catalog.DayPrice = price; var vehicle = w.Vehicle("elena");
        await Assert.ThrowsAsync<CheckoutPricingException>(() => w.Create("elena", vehicle));
        Assert.Empty(await w.Db.PrepaidPurchases.ToListAsync()); Assert.Empty(await w.Db.PermitApplications.ToListAsync());
    }

    public static IEnumerable<object[]> InvalidDates()
    {
        yield return [Array.Empty<DateOnly>()];
        yield return [new[] { Today.AddDays(-1) }];
        yield return [new[] { Dates[0], Dates[0] }];
        yield return [new[] { Today, Today.AddDays(182) }];
        yield return [Enumerable.Range(0, 183).Select(Today.AddDays).ToArray()];
    }

    [Theory, MemberData(nameof(InvalidDates))]
    public async Task InvalidDates_CreateNothing(DateOnly[] dates)
    {
        using var w = new World(); var vehicle = w.Vehicle();
        await Assert.ThrowsAsync<CheckoutPricingException>(() => w.Create(vehicle: vehicle, dates: dates));
        Assert.Empty(await w.Db.PermitApplications.ToListAsync()); Assert.Equal(0, w.Provider.CreateCalls);
    }

    [Fact]
    public async Task ForeignVehicle_CannotCreateAGroup()
    {
        using var w = new World(); var vehicle = w.Vehicle("pat");
        await Assert.ThrowsAsync<KeyNotFoundException>(() => w.Create(vehicle: vehicle));
        Assert.Empty(await w.Db.PrepaidPurchases.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingPendingDayOrSettledTerm_IsNotAdoptedAndNoPartialChildrenRemain(bool term)
    {
        using var w = new World(); var vehicle = w.Vehicle();
        var prior = (await w.Applications.CreateAsync(new("maria", vehicle.Id, "Test College",
            term ? PermitTypeCode.Term : PermitTypeCode.Daily, TermKind.Spring, Dates[0], "form"))).Application!;
        if (term) { prior.PaymentStatus = PaymentStatus.Paid; await w.Db.SaveChangesAsync(); }
        await Assert.ThrowsAsync<CheckoutPricingException>(() => w.Create(vehicle: vehicle));
        Assert.Single(await w.Db.PermitApplications.ToListAsync()); Assert.Empty(await w.Db.PrepaidPurchases.ToListAsync());
        Assert.Empty(await w.Db.PrepaidPurchaseItems.ToListAsync());
    }

    [Fact]
    public async Task FreeCreationFailureOnSecondPermit_RollsBackFirstIssuedChildAndParent()
    {
        using var w = new World(); w.Catalog.DayPrice = 0; w.Issuance.FailOnCall = 2;
        await Assert.ThrowsAsync<InvalidOperationException>(() => w.Create());
        Assert.Empty(await w.Db.PermitApplications.ToListAsync()); Assert.Empty(await w.Db.PrepaidPurchases.ToListAsync());
    }

    [Fact]
    public async Task IssuanceFailureAfterCapture_RollsBackEveryPaidStampAndReceipt()
    {
        using var w = new World(); var p = await w.Create(); await w.Start(p); w.Issuance.FailOnCall = 2;
        await Assert.ThrowsAsync<InvalidOperationException>(() => w.Service.CaptureAsync(p.Id));
        var saved = (await w.Service.LoadAsync(p.Id))!;
        Assert.Equal(PaymentStatus.Pending, saved.PaymentStatus); Assert.Null(saved.PayPalCaptureId); Assert.Null(saved.PaidAtUtc);
        Assert.All(saved.Items, i => { Assert.Equal(PaymentStatus.Pending, i.Application.PaymentStatus); Assert.Null(i.Application.ReceiptNumber); Assert.Null(i.Application.PaidAtUtc); });
    }

    [Fact]
    public async Task ProviderException_LeavesExistingOrderPendingWithoutIssuance()
    {
        using var w = new World(); var p = await w.Create(); await w.Start(p);
        w.Provider.CaptureException = new InvalidOperationException("Controlled provider failure.");
        await Assert.ThrowsAsync<InvalidOperationException>(() => w.Service.CaptureAsync(p.Id));
        var saved = (await w.Service.LoadAsync(p.Id))!;
        Assert.Equal("ORDER-1", saved.PayPalOrderId); Assert.Null(saved.PaidAtUtc);
        Assert.All(saved.Items, i => Assert.Null(i.Application.ReceiptNumber));
    }

    [Fact]
    public async Task PriceChange_RequiresRefreshAndFreshReview_ThenRepricesAllChildren()
    {
        using var w = new World(); var p = await w.Create(); var old = PrepaidPurchaseService.ReviewSnapshot(p);
        w.Catalog.DayPrice = 250;
        await Assert.ThrowsAsync<CheckoutPricingException>(() => w.Start(p, old));
        Assert.Equal(0, w.Provider.CreateCalls);
        var refreshed = await w.Service.RefreshPriceAsync(p.Id);
        Assert.Equal(500, refreshed.TotalCents); Assert.All(refreshed.Items, i => Assert.Equal(250, i.Application.PriceCents));
        await Assert.ThrowsAsync<CheckoutReviewChangedException>(() => w.Start(refreshed, old));
        await w.Start(refreshed); Assert.Equal(500, w.Provider.LastAmount);
    }

    [Fact]
    public async Task ExistingOrder_LocksItsQuoteAndResumesWithoutASecondCreate()
    {
        using var w = new World(); var p = await w.Create(); await w.Start(p); w.Catalog.DayPrice = 500;
        var same = await w.Service.RefreshPriceAsync(p.Id);
        Assert.Equal(200, same.TotalCents); Assert.Equal(100, same.UnitPriceCents);
        var resumed = await w.Start(same);
        Assert.Equal("ORDER-1", resumed.OrderId); Assert.Equal(1, w.Provider.CreateCalls); Assert.Equal(1, w.Provider.GetCalls);
        Assert.True((await w.Service.CaptureAsync(p.Id)).Completed); Assert.Equal(200, w.Provider.LastAmount);
    }

    [Fact]
    public async Task SamePriceVehicleEditAndDateMutation_CannotReuseAnOldReviewedSnapshot()
    {
        using var w = new World(file: true); var p = await w.Create(); var old = PrepaidPurchaseService.ReviewSnapshot(p);
        await using (var other = w.Sibling())
        {
            var vehicle = await other.Vehicles.SingleAsync(v => v.Id == p.VehicleId); vehicle.Make = "Changed make";
            await other.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<CheckoutReviewChangedException>(() => w.Start(p, old)); Assert.Equal(0, w.Provider.CreateCalls);
        var fresh = (await w.Service.LoadAsync(p.Id))!;
        var date = fresh.Items[0].Application; date.StartDate = date.EndDate = date.StartDate.AddDays(1);
        await w.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<CheckoutPricingException>(() => w.Start(fresh)); Assert.Equal(0, w.Provider.CreateCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CaptureIdAlreadyUsedByOtherPurchaseOrIndividualPermit_IsRejected(bool individual)
    {
        using var w = new World();
        if (individual)
        {
            var vehicle = w.Vehicle();
            var prior = (await w.Applications.CreateAsync(new("maria", vehicle.Id, "Test College", PermitTypeCode.Daily,
                TermKind.Spring, Dates[0], "form"))).Application!;
            prior.PayPalCaptureId = "CAPTURE-1"; prior.PaymentStatus = PaymentStatus.Paid; await w.Db.SaveChangesAsync();
        }
        else { var prior = await w.Create(); await w.Start(prior); await w.Service.CaptureAsync(prior.Id); }
        var p = await w.Create(); await w.Start(p);
        Assert.False((await w.Service.CaptureAsync(p.Id)).Completed);
        Assert.All((await w.Service.LoadAsync(p.Id))!.Items, i => Assert.Null(i.Application.ReceiptNumber));
    }

    [Fact]
    public async Task StageVerifiedCapture_DoesNotSaveOrBeginATransaction()
    {
        using var w = new World(file: true); var p = await w.Create(); await w.Start(p);
        var tracked = (await w.Service.LoadAsync(p.Id))!;
        Assert.Null(w.Db.Database.CurrentTransaction);
        Assert.True((await w.Service.StageVerifiedCaptureAsync(tracked, new("COMPLETED", "WEBHOOK-CAPTURE", 200, "USD"))).Completed);
        Assert.Null(w.Db.Database.CurrentTransaction);
        await using (var other = w.Sibling())
        {
            var persisted = await other.PrepaidPurchases.SingleAsync(x => x.Id == p.Id);
            Assert.Equal(PaymentStatus.Pending, persisted.PaymentStatus); Assert.Null(persisted.PayPalCaptureId);
            Assert.All(await other.PermitApplications.ToListAsync(), a => Assert.Null(a.ReceiptNumber));
        }
        await w.Db.SaveChangesAsync();
        await using var after = w.Sibling(); Assert.Equal(PaymentStatus.Paid, (await after.PrepaidPurchases.SingleAsync()).PaymentStatus);
        Assert.Equal(0, w.Provider.CaptureCalls);
    }

    [Fact]
    public async Task ConcurrentStartAndCapture_UseOneOrderAndOneCapture()
    {
        using var w = new World(file: true); var p = await w.Create(); var snapshot = PrepaidPurchaseService.ReviewSnapshot(p);
        w.Provider.CreateGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<CheckoutStartResult> Start() { await using var db = w.Sibling(); return await w.NewService(db).StartAsync(p.Id, snapshot, "http://localhost/return", "http://localhost/cancel"); }
        var first = Task.Run(Start); await w.Provider.CreateEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = Task.Run(Start);
        w.Provider.CreateGate.TrySetResult(true);
        var starts = await Task.WhenAll(first, second);
        Assert.All(starts, result => Assert.Equal("ORDER-1", result.OrderId)); Assert.Equal(1, w.Provider.CreateCalls);
        w.Provider.CaptureGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<CheckoutCaptureResult> Capture() { await using var db = w.Sibling(); return await w.NewService(db).CaptureAsync(p.Id); }
        var capture1 = Task.Run(Capture); await w.Provider.CaptureEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var capture2 = Task.Run(Capture);
        w.Provider.CaptureGate.TrySetResult(true);
        Assert.All(await Task.WhenAll(capture1, capture2), result => Assert.True(result.Completed));
        Assert.Equal(1, w.Provider.CaptureCalls);
    }

    [Fact]
    public async Task AdditiveSchema_IsIdempotentAndDoesNotAlterExistingApplicationTable()
    {
        using var w = new World();
        string ApplicationDdl() { using var command = w.Connection.CreateCommand(); command.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name='PermitApplications'"; return (string)command.ExecuteScalar()!; }
        var before = ApplicationDdl();
        await w.Db.Database.ExecuteSqlRawAsync("DROP TABLE PrepaidPurchaseItems; DROP TABLE PrepaidPurchases;");
        PrepaidSchema.EnsureCreated(w.Db); PrepaidSchema.EnsureCreated(w.Db);
        Assert.Equal(before, ApplicationDdl()); Assert.Equal(6, await w.Db.Students.CountAsync());
        Assert.Equal(2, (await w.Create()).Items.Count);
    }

    [Fact]
    public async Task OverflowingTotal_RollsBackEveryCreatedChild()
    {
        using var w = new World(); w.Catalog.DayPrice = int.MaxValue;
        await Assert.ThrowsAsync<CheckoutPricingException>(() => w.Create());
        Assert.Empty(await w.Db.PermitApplications.ToListAsync()); Assert.Empty(await w.Db.PrepaidPurchases.ToListAsync());
    }

    [Fact]
    public async Task PacificCalendar_AllowsTheLocalDayAfterUtcMidnight_ButBlocksItAfterPacificMidnight()
    {
        using var w = new World();
        w.Time.Now = new DateTimeOffset(2030, 1, 2, 2, 0, 0, TimeSpan.Zero); // Jan 1, 6 PM Pacific.
        var p = await w.Create(dates: [Today]);
        Assert.Equal(Today, Assert.Single(p.Items).Application.StartDate);
        w.Time.Now = new DateTimeOffset(2030, 1, 2, 8, 1, 0, TimeSpan.Zero); // Jan 2 Pacific.
        await Assert.ThrowsAsync<CheckoutPricingException>(() => w.Start(p));
        Assert.Equal(0, w.Provider.CreateCalls);
        Assert.Null((await w.Service.LoadAsync(p.Id))!.PayPalOrderId);
    }

    [Fact]
    public async Task PassedDates_BlockExistingUnapprovedOrder_ButApprovedOrderCanRecoverItsCapture()
    {
        using var w = new World(); var p = await w.Create(); await w.Start(p);
        w.Time.Now = new DateTimeOffset(2030, 2, 5, 20, 0, 0, TimeSpan.Zero);
        await Assert.ThrowsAsync<CheckoutPricingException>(() => w.Start(p));
        Assert.Equal(1, w.Provider.CreateCalls); Assert.Equal(0, w.Provider.CaptureCalls);
        w.Provider.GetStatus = "APPROVED";
        Assert.Equal(CheckoutStartKind.AlreadyPaid, (await w.Start(p)).Kind);
        Assert.Equal(1, w.Provider.CreateCalls); Assert.Equal(1, w.Provider.CaptureCalls);
        var paid = (await w.Service.LoadAsync(p.Id))!;
        Assert.All(paid.Items, i => Assert.NotNull(i.Application.ReceiptNumber));
        Assert.False(PermitValidity.IsActive(paid.Items.Single(i => i.Application.StartDate == Dates[0]).Application, new DateOnly(2030, 2, 5)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingParent_WithAnIndividuallyPaidOrIssuedChild_BlocksFurtherPayment(bool receipt)
    {
        using var w = new World(); var p = await w.Create(); await w.Start(p);
        var tracked = (await w.Service.LoadAsync(p.Id))!;
        var child = tracked.Items[0].Application;
        if (receipt) child.ReceiptNumber = "UNRELATED-ISSUANCE";
        else child.PaymentStatus = PaymentStatus.Paid;
        await w.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<CheckoutPricingException>(() => w.Service.CaptureAsync(p.Id));
        Assert.Equal(0, w.Provider.CaptureCalls);
        Assert.Equal(PaymentStatus.Pending, (await w.Service.LoadAsync(p.Id))!.PaymentStatus);
    }

    [Fact]
    public async Task StageCapture_ObservesPersistedCaptureConflictEvenWhenTrackedParentWasLoadedEarlier()
    {
        using var w = new World(file: true); var p = await w.Create(); await w.Start(p);
        var tracked = (await w.Service.LoadAsync(p.Id))!;
        Assert.Null(tracked.PayPalCaptureId);
        await using (var other = w.Sibling())
        {
            var persisted = await other.PrepaidPurchases.SingleAsync(x => x.Id == p.Id);
            persisted.PayPalCaptureId = "EARLIER-CAPTURE"; persisted.ConcurrencyToken = Guid.NewGuid();
            await other.SaveChangesAsync();
        }
        var result = await w.Service.StageVerifiedCaptureAsync(tracked, new("COMPLETED", "DIFFERENT-CAPTURE", 200, "USD"));
        Assert.False(result.Completed); Assert.Equal("CAPTURE_CONFLICT", result.Status);
        Assert.Equal(PaymentStatus.Pending, tracked.PaymentStatus); Assert.All(tracked.Items, i => Assert.Null(i.Application.ReceiptNumber));
        await using var verify = w.Sibling();
        Assert.Equal("EARLIER-CAPTURE", (await verify.PrepaidPurchases.SingleAsync()).PayPalCaptureId);
    }

    [Fact]
    public async Task CancelDraft_DeletesOnlyItsUnpaidPermitsAndPreservesVehicleAndUnrelatedHistory()
    {
        using var w = new World(); var vehicle = w.Vehicle(); var p = await w.Create(vehicle: vehicle);
        var unrelated = (await w.Applications.CreateAsync(new("maria", vehicle.Id, "Test College", PermitTypeCode.Daily,
            TermKind.Spring, Dates[1].AddDays(10), "form"))).Application!;
        await w.Service.CancelDraftAsync(p.Id);
        Assert.Null(await w.Service.LoadAsync(p.Id)); Assert.Empty(await w.Db.PrepaidPurchaseItems.ToListAsync());
        Assert.Equal(unrelated.Id, Assert.Single(await w.Db.PermitApplications.ToListAsync()).Id);
        Assert.Equal(vehicle.Id, Assert.Single(await w.Db.Vehicles.ToListAsync()).Id);
        var revised = await w.Create(vehicle: vehicle, dates: [Dates[0]]);
        Assert.Single(revised.Items); Assert.NotEqual(p.Id, revised.Id);
        Assert.Equal(0, w.Provider.CreateCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelDraft_RejectsAnOrderBoundOrFreeIssuedPurchase(bool free)
    {
        using var w = new World(); if (free) w.Catalog.DayPrice = 0;
        var p = await w.Create(); if (!free) await w.Start(p);
        await Assert.ThrowsAsync<CheckoutPricingException>(() => w.Service.CancelDraftAsync(p.Id));
        Assert.NotNull(await w.Service.LoadAsync(p.Id)); Assert.Equal(2, await w.Db.PermitApplications.CountAsync());
    }
}
