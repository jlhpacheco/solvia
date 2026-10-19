using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Solvia.Config;
using Solvia.Data;
using Solvia.Models;
using Solvia.Models.Enums;
using Solvia.Services;

namespace Solvia.Tests;

/// <summary>
/// Shared test-database helper: one open in-memory SQLite connection
/// per test class instance = one fresh database with the real schema
/// and the real seeds (EnsureCreated, exactly like the app's first
/// run). Contexts built from the same options share that database.
/// </summary>
internal static class RedoTestDb
{
    public static (SolviaDbContext Context, SqliteConnection Connection) Create()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var context = new SolviaDbContext(OptionsFor(connection));
        context.Database.EnsureCreated();
        return (context, connection);
    }

    public static SolviaDbContext NewContext(SqliteConnection connection) =>
        new(OptionsFor(connection));

    private static DbContextOptions<SolviaDbContext> OptionsFor(SqliteConnection connection) =>
        new DbContextOptionsBuilder<SolviaDbContext>().UseSqlite(connection).Options;

    public static string FindFeesCsv()
    {
        var starts = new[]
        {
            Path.GetDirectoryName(typeof(RedoTestDb).Assembly.Location),
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

        throw new FileNotFoundException(
            "Could not locate data/fees.csv above the test assembly.");
    }
}

/// <summary>
/// R1 domain-core tests: seeds, the category resolver, startup
/// validation, the composite duplicate index, the concurrency token,
/// and the term-dates authority. Everything runs against the real
/// EF model on SQLite — the same schema the app creates.
/// </summary>
public sealed class DomainTests
{
    // ------------------------------------------------------------- seeds

    [Fact]
    public void Seeds_SixFictionalPeople_IncludingElenaAndPat()
    {
        var (db, connection) = RedoTestDb.Create();
        using var _ = connection;
        using var __ = db;

        var people = db.Students.OrderBy(s => s.Id).ToList();
        Assert.Equal(6, people.Count);
        Assert.Equal("elena@college.example", people.Single(s => s.Id == "elena").Email);
        Assert.Equal("pat@public.example", people.Single(s => s.Id == "pat").Email);
        // The override column exists and starts unset for everyone.
        Assert.All(people, p => Assert.Null(p.CategoryOverride));
    }

    [Fact]
    public void Seeds_PermitTypes_AreDailyAndTerm_Active_PricePlaceholderZero()
    {
        var (db, connection) = RedoTestDb.Create();
        using var _ = connection;
        using var __ = db;

        var types = db.PermitTypes.OrderBy(t => t.Id).ToList();
        Assert.Equal(2, types.Count);
        Assert.Equal(PermitTypeCode.Daily, types[0].Code);
        Assert.Equal(1, types[0].DurationDays);
        Assert.Equal(PermitTypeCode.Term, types[1].Code);
        Assert.Equal(120, types[1].DurationDays);
        Assert.All(types, t => Assert.True(t.Active));
        // The placeholder: seeded 0, formally non-authoritative (the
        // guard test in PricingTests proves prices never come from it).
        Assert.All(types, t => Assert.Equal(0, t.PriceCents));
    }

    // ---------------------------------------------------------- resolver

    private static UserCategoryResolver NewResolver() =>
        new(Options.Create(new CategoryOptions
        {
            EmployeeDomains = ["college.example"],
            StudentDomains = ["student.example"],
        }));

    [Fact]
    public void Resolver_SeededPeople_ResolveByDomain()
    {
        var (db, connection) = RedoTestDb.Create();
        using var _ = connection;
        using var __ = db;
        var resolver = NewResolver();

        Assert.Equal(UserCategory.Student, resolver.Resolve(db.Students.Find("maria")!));
        Assert.Equal(UserCategory.Employee, resolver.Resolve(db.Students.Find("elena")!));
        Assert.Equal(UserCategory.Public, resolver.Resolve(db.Students.Find("pat")!));
    }

    [Fact]
    public void Resolver_OverrideWinsOverBothDomainLists()
    {
        var resolver = NewResolver();

        // Override beats the employee domain…
        Assert.Equal(
            UserCategory.Student,
            resolver.Resolve("elena@college.example", UserCategory.Student));
        // …and beats the student domain.
        Assert.Equal(
            UserCategory.Employee,
            resolver.Resolve("maria@student.example", UserCategory.Employee));
    }

    [Fact]
    public void Resolver_DomainBranches_AndFallbacks()
    {
        var resolver = NewResolver();

        Assert.Equal(UserCategory.Employee, resolver.Resolve("x@college.example", null));
        Assert.Equal(UserCategory.Student, resolver.Resolve("x@student.example", null));
        Assert.Equal(UserCategory.Public, resolver.Resolve("x@somewhere-else.example", null));
        // Domain matching is case-insensitive…
        Assert.Equal(UserCategory.Employee, resolver.Resolve("x@COLLEGE.example", null));
        // …and a missing/garbled address falls back to Public, never throws.
        Assert.Equal(UserCategory.Public, resolver.Resolve(null, null));
        Assert.Equal(UserCategory.Public, resolver.Resolve("not-an-email", null));
    }

    // ---------------------------------------------- startup validation

    [Fact]
    public void StartupValidation_Passes_OnSeededData()
    {
        var (db, connection) = RedoTestDb.Create();
        using var _ = connection;
        using var __ = db;

        // No throw = the seeded catalog + real fee file satisfy every check.
        db.ValidateStartupData(new FeeCatalogService(RedoTestDb.FindFeesCsv()));
    }

    [Fact]
    public void StartupValidation_ThrowsNamingTheCheck_WhenTypeDeactivated()
    {
        var (db, connection) = RedoTestDb.Create();
        using var _ = connection;
        using var __ = db;

        var term = db.PermitTypes.Single(t => t.Code == PermitTypeCode.Term);
        term.Active = false;
        db.SaveChanges();

        var ex = Assert.Throws<InvalidOperationException>(
            () => db.ValidateStartupData(new FeeCatalogService(RedoTestDb.FindFeesCsv())));
        Assert.Contains("permit-type active", ex.Message);
    }

    [Fact]
    public void StartupValidation_ThrowsNamingTheCheck_WhenTypeMissing()
    {
        var (db, connection) = RedoTestDb.Create();
        using var _ = connection;
        using var __ = db;

        var daily = db.PermitTypes.Single(t => t.Code == PermitTypeCode.Daily);
        db.PermitTypes.Remove(daily);
        db.SaveChanges();

        var ex = Assert.Throws<InvalidOperationException>(
            () => db.ValidateStartupData(new FeeCatalogService(RedoTestDb.FindFeesCsv())));
        Assert.Contains("permit-type catalog", ex.Message);
    }

    // -------------------------------------------- applications in the DB

    private static (Vehicle Vehicle, PermitType Type) SeedVehicleAndType(SolviaDbContext db)
    {
        var vehicle = new Vehicle
        {
            Id = Guid.NewGuid(),
            StudentId = "maria",
            Plate = "8XYZ123",
            State = UsState.CA,
            Color = VehicleColor.Blue,
            Make = "Honda",
            Model = "Civic",
        };
        db.Vehicles.Add(vehicle);
        db.SaveChanges();
        var type = db.PermitTypes.Single(t => t.Code == PermitTypeCode.Term);
        return (vehicle, type);
    }

    private static PermitApplication NewApplication(Vehicle vehicle, PermitType type) => new()
    {
        Id = Guid.NewGuid(),
        StudentId = "maria",
        Campus = "Fresno City College",
        Category = UserCategory.Student,
        VehicleId = vehicle.Id,
        PermitTypeId = type.Id,
        Term = TermKind.Fall,
        Source = "form",
        StartDate = new DateOnly(2026, 8, 24),
        EndDate = new DateOnly(2026, 12, 22),
        Status = ApplicationStatus.Pending,
        PriceCents = 3000,
        PaymentStatus = PaymentStatus.Pending,
        SubmittedAtUtc = DateTime.UtcNow,
        ConcurrencyToken = Guid.NewGuid(),
    };

    [Fact]
    public void DuplicateApplication_ViolatesTheCompositeUniqueIndex()
    {
        var (db, connection) = RedoTestDb.Create();
        using var _ = connection;
        using var __ = db;
        var (vehicle, type) = SeedVehicleAndType(db);

        db.PermitApplications.Add(NewApplication(vehicle, type));
        db.SaveChanges();

        // Same person + vehicle + type + window = the same permit
        // sold twice; the database itself must refuse (item 15).
        db.PermitApplications.Add(NewApplication(vehicle, type));
        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
    }

    [Fact]
    public void CategorySnapshot_AndIssuanceStamp_RoundTrip()
    {
        var (db, connection) = RedoTestDb.Create();
        using var _ = connection;
        var (vehicle, type) = SeedVehicleAndType(db);

        var app = NewApplication(vehicle, type);
        app.Category = UserCategory.Employee;
        app.ReceiptIssuedAtUtc = new DateTime(2026, 8, 24, 15, 30, 0, DateTimeKind.Utc);
        db.PermitApplications.Add(app);
        db.SaveChanges();
        db.Dispose();

        using var reloaded = RedoTestDb.NewContext(connection);
        var row = reloaded.PermitApplications.Single(a => a.Id == app.Id);
        Assert.Equal(UserCategory.Employee, row.Category);
        Assert.Equal(app.ReceiptIssuedAtUtc, row.ReceiptIssuedAtUtc);
    }

    [Fact]
    public void ConcurrencyToken_ChangesOnSave_WhenWriterRotatesIt()
    {
        var (db, connection) = RedoTestDb.Create();
        using var _ = connection;
        var (vehicle, type) = SeedVehicleAndType(db);

        var app = NewApplication(vehicle, type);
        var original = app.ConcurrencyToken;
        db.PermitApplications.Add(app);
        db.SaveChanges();

        // The writer protocol (item 51): rotate before every save.
        var rotated = Guid.NewGuid();
        app.ConcurrencyToken = rotated;
        app.PriceCents = 3100;
        db.SaveChanges();
        db.Dispose();

        using var reloaded = RedoTestDb.NewContext(connection);
        var row = reloaded.PermitApplications.Single(a => a.Id == app.Id);
        Assert.Equal(rotated, row.ConcurrencyToken);
        Assert.NotEqual(original, row.ConcurrencyToken);
    }

    [Fact]
    public void StaleWriter_ThrowsDbUpdateConcurrencyException()
    {
        var (setup, connection) = RedoTestDb.Create();
        using var _ = connection;
        var (vehicle, type) = SeedVehicleAndType(setup);
        var app = NewApplication(vehicle, type);
        setup.PermitApplications.Add(app);
        setup.SaveChanges();
        setup.Dispose();

        using var first = RedoTestDb.NewContext(connection);
        using var second = RedoTestDb.NewContext(connection);
        var firstRow = first.PermitApplications.Single(a => a.Id == app.Id);
        var secondRow = second.PermitApplications.Single(a => a.Id == app.Id);

        // First writer rotates + saves: succeeds.
        firstRow.ConcurrencyToken = Guid.NewGuid();
        firstRow.PriceCents = 3100;
        first.SaveChanges();

        // Second writer still holds the stale token: its save must be
        // refused, never silently overwrite the first writer's work.
        secondRow.PriceCents = 9999;
        Assert.Throws<DbUpdateConcurrencyException>(() => second.SaveChanges());
    }

    // ------------------------------------------------------- term dates

    [Fact]
    public void TermDates_FresnoFall2026_MatchesTheTermCalendar()
    {
        var (db, connection) = RedoTestDb.Create();
        using var _ = connection;
        using var __ = db;
        var catalog = new FeeCatalogService(RedoTestDb.FindFeesCsv());
        var dates = new TermDatesService(catalog, db);

        var fall = dates.GetTermDates("Fresno City College", TermKind.Fall, 2026);
        Assert.NotNull(fall);
        Assert.Equal(new DateOnly(2026, 8, 24), fall!.TermStart);
        Assert.Equal(new DateOnly(2026, 12, 11), fall.TermEnd);
        // Permit end = start + the Term type's 120 days, read from the DB.
        Assert.Equal(120, fall.DurationDays);
        Assert.Equal(new DateOnly(2026, 12, 22), fall.PermitEndDate);
        Assert.Equal(fall.PermitEndDate, fall.RenewalDate);

        Assert.Null(dates.GetTermDates("Hogwarts School of Parking", TermKind.Fall, 2026));
    }
}
