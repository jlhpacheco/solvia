using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Solvia.Data;
using Solvia.Models;
using Solvia.Models.Enums;

namespace Solvia.Tests;

/// <summary>
/// T1 schema tests against a real (in-memory) SQLite database: the seed
/// data lands, enums are stored as TEXT, and the composite unique index
/// from docs/SCHEMA.md blocks a duplicate permit.
/// </summary>
public sealed class PermitSchemaTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<SolviaDbContext> _options;

    public PermitSchemaTests()
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

    [Fact]
    public void SeedContainsFictionalStudentsIncludingMaria()
    {
        using var db = NewDb();
        var ids = db.Students.Select(s => s.Id).ToList();
        Assert.Contains("maria", ids);
        Assert.True(ids.Count >= 3);
        // Fictional people only — every seeded address is an .example domain.
        Assert.All(
            db.Students.ToList(),
            s => Assert.EndsWith(".example", s.Email));
    }

    [Fact]
    public void SeedContainsDailyAndTermPermitTypes()
    {
        using var db = NewDb();
        var codes = db.PermitTypes.Select(t => t.Code).ToList();
        Assert.Contains(PermitTypeCode.Daily, codes);
        Assert.Contains(PermitTypeCode.Term, codes);
        Assert.All(db.PermitTypes.ToList(), t => Assert.True(t.Active));
    }

    [Fact]
    public void PermitTypeCodeIsStoredAsText()
    {
        using var db = NewDb();
        var row = db.PermitTypes.Single(t => t.Code == PermitTypeCode.Term);
        var raw = db.Database
            .SqlQueryRaw<string>(
                "SELECT Code AS Value FROM PermitTypes WHERE Id = {0}", row.Id)
            .Single();
        Assert.Equal("Term", raw);
    }

    [Fact]
    public void DuplicatePermitInsertIsBlockedByUniqueIndex()
    {
        var start = new DateOnly(2026, 8, 24);
        var end = new DateOnly(2026, 12, 11);
        Guid vehicleId;

        using (var db = NewDb())
        {
            var vehicle = new Vehicle
            {
                Id = Guid.NewGuid(),
                StudentId = "maria", // seeded fictional student
                Plate = "8ABC123",
                State = UsState.CA,
                Color = VehicleColor.Blue,
                Make = "Toyota",
                Model = "Corolla",
            };
            db.Vehicles.Add(vehicle);
            db.SaveChanges();
            vehicleId = vehicle.Id;

            db.PermitApplications.Add(NewApplication(vehicleId, start, end));
            db.SaveChanges(); // first insert: fine
        }

        using (var db = NewDb())
        {
            // Same student + vehicle + type + window = the same permit twice.
            db.PermitApplications.Add(NewApplication(vehicleId, start, end));
            var ex = Assert.Throws<DbUpdateException>(() => db.SaveChanges());
            Assert.Contains("UNIQUE", ex.InnerException?.Message ?? "");
        }
    }

    [Fact]
    public void SameStudentDifferentWindowIsAllowed()
    {
        Guid vehicleId;
        using (var db = NewDb())
        {
            var vehicle = new Vehicle
            {
                Id = Guid.NewGuid(),
                StudentId = "maria",
                Plate = "8ABC123",
                State = UsState.CA,
                Color = VehicleColor.Blue,
            };
            db.Vehicles.Add(vehicle);
            db.SaveChanges();
            vehicleId = vehicle.Id;

            db.PermitApplications.Add(NewApplication(
                vehicleId, new DateOnly(2026, 8, 24), new DateOnly(2026, 12, 11)));
            db.PermitApplications.Add(NewApplication(
                vehicleId, new DateOnly(2027, 1, 19), new DateOnly(2027, 5, 28)));
            db.SaveChanges(); // different windows: no violation
        }

        using var verify = NewDb();
        Assert.Equal(2, verify.PermitApplications.Count());
    }

    private static PermitApplication NewApplication(
        Guid vehicleId, DateOnly start, DateOnly end) => new()
    {
        Id = Guid.NewGuid(),
        StudentId = "maria",
        VehicleId = vehicleId,
        PermitTypeId = 2, // seeded Term type
        StartDate = start,
        EndDate = end,
        Status = ApplicationStatus.Pending,
        PriceCents = 3000,
        PaymentStatus = PaymentStatus.Pending,
        SubmittedAtUtc = DateTime.UtcNow,
    };
}
