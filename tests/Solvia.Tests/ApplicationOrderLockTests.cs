using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Solvia.Config;
using Solvia.Data;
using Solvia.Models;
using Solvia.Models.Enums;
using Solvia.Services;

namespace Solvia.Tests;

/// <summary>Offline regressions for the purchase snapshot bound to an existing PayPal order.</summary>
public sealed class ApplicationOrderLockTests
{
    private const string ExistingOrder = "OFFLINE-BOUND-ORDER";

    private sealed class World : IDisposable
    {
        public SolviaDbContext Db { get; }
        public SqliteConnection Connection { get; }
        public IFeeCatalog Catalog { get; }
        public IIssuanceService Issuance { get; }
        public ApplicationService Applications { get; }
        public Vehicle Original { get; }
        public Vehicle Alternate { get; }
        public ApplicationInput Input { get; }

        public World()
        {
            (Db, Connection) = RedoTestDb.Create();
            Catalog = new FeeCatalogService(RedoTestDb.FindFeesCsv());
            Issuance = new IssuanceService(new PermitNumberService(Db));
            Applications = new ApplicationService(Db, Catalog, new PricingService(Catalog),
                new UserCategoryResolver(Options.Create(new CategoryOptions())),
                new TermDatesService(Catalog, Db), Issuance);
            Original = Vehicle("LOCK432");
            Alternate = Vehicle("OTHER432");
            Db.Vehicles.AddRange(Original, Alternate);
            Db.SaveChanges();
            Input = new ApplicationInput("maria", Original.Id, "Fresno City College",
                PermitTypeCode.Term, TermKind.Fall, new DateOnly(2026, 8, 24), "together", true);
        }

        private static Vehicle Vehicle(string plate) => new()
        {
            Id = Guid.NewGuid(), StudentId = "maria", Plate = plate,
            State = UsState.CA, Make = "Honda", Model = "Civic", Color = VehicleColor.Blue
        };

        public void Dispose()
        {
            Db.Dispose();
            Connection.Dispose();
        }
    }

    [Theory]
    [InlineData("lower-price-day")]
    [InlineData("same-price-other-vehicle")]
    [InlineData("campus")]
    [InlineData("term")]
    [InlineData("date")]
    [InlineData("source")]
    [InlineData("unchanged")]
    public async Task OrderBoundDraft_RefusesEveryRevision_AndPreservesSavedPurchase(string change)
    {
        using var world = new World();
        var application = (await world.Applications.CreateAsync(world.Input)).Application!;
        Assert.Equal(3000, application.PriceCents);
        application.PayPalOrderId = ExistingOrder;
        application.ConcurrencyToken = Guid.NewGuid();
        await world.Db.SaveChangesAsync();
        var before = Snapshot(application);
        var revision = change switch
        {
            "lower-price-day" => world.Input with
                { TypeCode = PermitTypeCode.Daily, StartDate = new DateOnly(2026, 10, 20) },
            "same-price-other-vehicle" => world.Input with { VehicleId = world.Alternate.Id },
            "campus" => world.Input with { Campus = "CSU Monterey Bay" },
            "term" => world.Input with { Term = TermKind.Spring, StartDate = new DateOnly(2027, 1, 20) },
            "date" => world.Input with { StartDate = new DateOnly(2026, 8, 25) },
            "source" => world.Input with { Source = "fallback" },
            _ => world.Input
        };

        var result = await world.Applications.ReviseDraftAsync(application.Id, revision);

        Assert.Equal(CreateApplicationStatus.PaymentStarted, result.Status);
        Assert.Equal(application.Id, result.Application!.Id);
        Assert.Equal(ApplicationService.PaymentStartedMessage, result.ErrorMessage);
        Assert.Equal(before, Snapshot(result.Application));
        Assert.False(world.Db.ChangeTracker.HasChanges());
        world.Db.ChangeTracker.Clear();
        Assert.Equal(before, Snapshot(await world.Db.PermitApplications.SingleAsync()));
        Assert.Equal(2, await world.Db.Vehicles.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaleRevision_WhenCheckoutWins_PreservesItsPurchaseAndReturnsCurrentState(bool paid)
    {
        using var world = new World();
        var application = (await world.Applications.CreateAsync(world.Input)).Application!;
        object expected;
        using (var checkoutContext = RedoTestDb.NewContext(world.Connection))
        {
            var current = await checkoutContext.PermitApplications.SingleAsync();
            current.PayPalOrderId = ExistingOrder;
            current.ConcurrencyToken = Guid.NewGuid();
            if (paid)
            {
                current.PaymentStatus = PaymentStatus.Paid;
                current.PayPalCaptureId = "OFFLINE-CAPTURE";
                current.PaidAtUtc = new DateTime(2026, 10, 20, 16, 0, 0, DateTimeKind.Utc);
                current.ReceiptNumber = "SOL-2026-123456";
                current.ReceiptIssuedAtUtc = current.PaidAtUtc;
                current.VerifyToken = "offline-verification-token";
            }
            await checkoutContext.SaveChangesAsync();
            expected = Snapshot(current);
        }
        // The revision context still tracks the earlier row with no order.
        Assert.Null(application.PayPalOrderId);

        var result = await world.Applications.ReviseDraftAsync(application.Id,
            world.Input with { TypeCode = PermitTypeCode.Daily,
                VehicleId = world.Alternate.Id, StartDate = new DateOnly(2026, 10, 20) });

        Assert.Equal(paid ? CreateApplicationStatus.AlreadyLive : CreateApplicationStatus.PaymentStarted,
            result.Status);
        Assert.Equal(expected, Snapshot(result.Application!));
        Assert.DoesNotContain(world.Db.ChangeTracker.Entries<PermitApplication>(),
            entry => entry.State == EntityState.Modified);
        Assert.Equal(expected, Snapshot(await world.Db.PermitApplications.AsNoTracking().SingleAsync()));
    }

    [Fact]
    public async Task RefusedRevision_CanResumeProviderBackedExistingCheckout_WithoutCreatingOrCapturing()
    {
        using var world = new World();
        var application = (await world.Applications.CreateAsync(world.Input)).Application!;
        application.PayPalOrderId = ExistingOrder;
        await world.Db.SaveChangesAsync();
        var blocked = await world.Applications.ReviseDraftAsync(application.Id,
            world.Input with { TypeCode = PermitTypeCode.Daily, StartDate = new DateOnly(2026, 10, 20) });
        Assert.Equal(CreateApplicationStatus.PaymentStarted, blocked.Status);
        var provider = new ResumeOnlyPayPal();
        var checkout = new CheckoutService(world.Db, provider, world.Issuance,
            world.Catalog, new PricingService(world.Catalog), NullLogger<CheckoutService>.Instance);

        var resumed = await checkout.StartAsync(application.Id,
            "http://localhost/Checkout/Return", "http://localhost/Checkout/Cancel");

        Assert.Equal(CheckoutStartKind.OrderReady, resumed.Kind);
        Assert.Equal(ExistingOrder, resumed.OrderId);
        Assert.Equal("https://www.sandbox.paypal.com/checkoutnow?token=OFFLINE-BOUND-ORDER", resumed.ApproveUrl);
        Assert.Equal(new[] { ExistingOrder }, provider.Lookups);
        Assert.Equal(3000, application.PriceCents);
        Assert.Equal(PaymentStatus.Pending, application.PaymentStatus);
        Assert.Null(application.PayPalCaptureId);
        Assert.Null(application.ReceiptNumber);
    }

    private static object Snapshot(PermitApplication app) => new
    {
        app.Id, app.StudentId, app.Campus, app.Category, app.VehicleId, app.PermitTypeId,
        app.Term, app.Source, app.StartDate, app.EndDate, app.Status, app.PriceCents,
        app.PaymentStatus, app.PayPalOrderId, app.PayPalCaptureId, app.PaidAtUtc,
        app.ReceiptNumber, app.ReceiptIssuedAtUtc, app.VerifyToken, app.SubmittedAtUtc,
        app.ConcurrencyToken
    };

    /// <summary>Local contract stub, never an HTTP client or actual provider approval.</summary>
    private sealed class ResumeOnlyPayPal : IPayPalService
    {
        public List<string> Lookups { get; } = [];
        public Task<PayPalOrderResult> GetOrderAsync(string orderId, CancellationToken cancellationToken = default)
        {
            Lookups.Add(orderId);
            return Task.FromResult(new PayPalOrderResult(orderId, "CREATED",
                "https://www.sandbox.paypal.com/checkoutnow?token=OFFLINE-BOUND-ORDER"));
        }
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No OAuth request is allowed in this offline test.");
        public Task<PayPalOrderResult> CreateOrderAsync(int amountCents, string reference,
            string? returnUrl = null, string? cancelUrl = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Resumption must not create another order.");
        public Task<PayPalCaptureResult> CaptureOrderAsync(string orderId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("A CREATED order must not be captured by resumption.");
        public Task<bool> VerifyWebhookSignatureAsync(string transmissionId, string transmissionTime,
            string transmissionSig, string certUrl, string authAlgo, string webhookEventJson,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No webhook call is allowed in this offline test.");
    }
}
