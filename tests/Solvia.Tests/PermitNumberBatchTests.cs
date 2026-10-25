using Microsoft.EntityFrameworkCore;
using Solvia.Data;
using Solvia.Models;
using Solvia.Models.Enums;
using Solvia.Services;

namespace Solvia.Tests;

public sealed class PermitNumberBatchTests
{
    private static PermitApplication Application(SolviaDbContext db, DateOnly date, string? receipt = null)
    {
        var vehicle = new Vehicle { Id = Guid.NewGuid(), StudentId = "maria", Plate = "BATCH123",
            State = UsState.CA, Color = VehicleColor.White, Make = "Volvo", Model = "760" };
        var application = new PermitApplication { Id = Guid.NewGuid(), StudentId = "maria", VehicleId = vehicle.Id,
            Vehicle = vehicle, Campus = "Test College", Category = UserCategory.Student, PermitTypeId = 1,
            Term = TermKind.Spring, StartDate = date, EndDate = date, PriceCents = 100,
            PaymentStatus = PaymentStatus.Pending, ReceiptNumber = receipt, SubmittedAtUtc = DateTime.UtcNow,
            ConcurrencyToken = Guid.NewGuid() };
        db.PermitApplications.Add(application);
        return application;
    }

    [Fact]
    public async Task BatchIssuance_RedrawsAnUnsavedSiblingReceiptBeforeTheSingleSave()
    {
        var (db, connection) = RedoTestDb.Create();
        using var connectionLifetime = connection;
        await using var contextLifetime = db;
        var first = Application(db, new DateOnly(2030, 2, 4));
        var second = Application(db, new DateOnly(2030, 2, 6));
        await db.SaveChangesAsync();
        var sequence = new Queue<int>([111111, 111111, 222222]);
        var issuance = new IssuanceService(new PermitNumberService(db, () => sequence.Dequeue()));
        await issuance.IssueIfAbsentAsync(first);
        // Receipt one is only staged: the database still has no reservation.
        Assert.All(await db.PermitApplications.AsNoTracking().Select(a => a.ReceiptNumber).ToListAsync(), Assert.Null);
        await issuance.IssueIfAbsentAsync(second);
        Assert.EndsWith("-111111", first.ReceiptNumber);
        Assert.EndsWith("-222222", second.ReceiptNumber);
        Assert.Empty(sequence);
        await db.SaveChangesAsync(); // The real unique index must accept both.
        db.ChangeTracker.Clear();
        Assert.Equal(2, (await db.PermitApplications.Select(a => a.ReceiptNumber).ToListAsync()).Distinct().Count());
    }

    [Fact]
    public async Task AddedUnsavedReceipt_IsReservedBeforeItsInsert()
    {
        var (db, connection) = RedoTestDb.Create();
        using var connectionLifetime = connection;
        await using var contextLifetime = db;
        var year = DateTime.UtcNow.Year;
        Application(db, new DateOnly(2030, 2, 4), $"SOL-{year}-333333");
        var sequence = new Queue<int>([333333, 444444]);
        var number = await new PermitNumberService(db, () => sequence.Dequeue()).IssueReceiptNumberAsync();
        Assert.Equal($"SOL-{year}-444444", number);
        Assert.Empty(sequence);
        Assert.Empty(await db.PermitApplications.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task PersistedUntrackedReceipt_StillForcesARedraw()
    {
        var (db, connection) = RedoTestDb.Create();
        using var connectionLifetime = connection;
        await using var contextLifetime = db;
        var year = DateTime.UtcNow.Year;
        Application(db, new DateOnly(2030, 2, 4), $"SOL-{year}-555555");
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var sequence = new Queue<int>([555555, 666666]);
        var number = await new PermitNumberService(db, () => sequence.Dequeue()).IssueReceiptNumberAsync();
        Assert.Equal($"SOL-{year}-666666", number);
        Assert.Empty(sequence);
    }

    [Fact]
    public async Task TenReservedDraws_StillFailWithoutReturningADuplicateOrSavingAnything()
    {
        var (db, connection) = RedoTestDb.Create();
        using var connectionLifetime = connection;
        await using var contextLifetime = db;
        Application(db, new DateOnly(2030, 2, 4), $"SOL-{DateTime.UtcNow.Year}-777777");
        var draws = 0;
        var service = new PermitNumberService(db, () => { draws++; return 777777; });
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.IssueReceiptNumberAsync());
        Assert.Equal(10, draws);
        Assert.Contains("10 attempts", error.Message);
        Assert.Empty(await db.PermitApplications.AsNoTracking().ToListAsync());
    }
}
