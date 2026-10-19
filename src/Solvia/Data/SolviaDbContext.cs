using Microsoft.EntityFrameworkCore;
using Solvia.Models;
using Solvia.Models.Enums;
using Solvia.Services;

namespace Solvia.Data;

/// <summary>
/// Solvia's EF Core context (SQLite — a single local file, zero
/// server install; the reference app's entity shapes, enum-as-text
/// conversions, and composite unique index carry over unchanged).
///
/// R1 owns this file (REDO-PLAN §3): the full domain — Student (with
/// CategoryOverride), PermitType, Vehicle, PermitApplication (with
/// the Category snapshot, ReceiptIssuedAtUtc, and the concurrency
/// token), WebhookEventLog — the composite duplicate index, the
/// seeds, and the startup validation hook
/// (<see cref="ValidateStartupData"/>).
/// </summary>
public sealed class SolviaDbContext : DbContext
{
    public SolviaDbContext(DbContextOptions<SolviaDbContext> options)
        : base(options)
    {
    }

    public DbSet<Student> Students => Set<Student>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<PermitType> PermitTypes => Set<PermitType>();
    public DbSet<PermitApplication> PermitApplications => Set<PermitApplication>();
    public DbSet<WebhookEventLog> WebhookEventLogs => Set<WebhookEventLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // --- Student -------------------------------------------------
        modelBuilder.Entity<Student>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).HasMaxLength(64);
            e.Property(s => s.DisplayName).IsRequired().HasMaxLength(100);
            e.Property(s => s.Email).IsRequired().HasMaxLength(200);
            e.Property(s => s.CategoryOverride).HasConversion<string>().HasMaxLength(20);

            // The seeded fictional cast (REDO-PLAN §2b): four students
            // on the student domain, Elena on the college domain
            // (resolves Employee), Pat on a public domain (resolves
            // Public). CategoryOverride is null for everyone — the
            // field exists, nothing seeds it. All addresses are
            // .example — nobody real.
            e.HasData(
                new Student { Id = "maria", DisplayName = "Maria", Email = "maria@student.example" },
                new Student { Id = "diego", DisplayName = "Diego", Email = "diego@student.example" },
                new Student { Id = "priya", DisplayName = "Priya", Email = "priya@student.example" },
                new Student { Id = "sam", DisplayName = "Sam", Email = "sam@student.example" },
                new Student { Id = "elena", DisplayName = "Elena", Email = "elena@college.example" },
                new Student { Id = "pat", DisplayName = "Pat", Email = "pat@public.example" });
        });

        // --- PermitType ----------------------------------------------
        modelBuilder.Entity<PermitType>(e =>
        {
            e.HasKey(t => t.Id);
            // Code is stored as TEXT ("Daily" / "Term").
            e.Property(t => t.Code).HasConversion<string>().IsRequired().HasMaxLength(20);
            e.Property(t => t.Name).IsRequired().HasMaxLength(200);
            e.HasIndex(t => t.Code).IsUnique();

            // Exactly two types (REDO-PLAN item 10). PriceCents is a
            // seeded-0 NON-AUTHORITATIVE placeholder — Solvia's price
            // truth is the per-college published fee, resolved by
            // PricingService; nothing may ever charge from this column.
            e.HasData(
                new PermitType
                {
                    Id = 1,
                    Code = PermitTypeCode.Daily,
                    Name = "Daily permit",
                    DurationDays = 1,
                    PriceCents = 0,
                    Active = true,
                },
                new PermitType
                {
                    Id = 2,
                    Code = PermitTypeCode.Term,
                    Name = "Term / semester permit",
                    DurationDays = 120,
                    PriceCents = 0,
                    Active = true,
                });
        });

        // --- Vehicle ---------------------------------------------------
        modelBuilder.Entity<Vehicle>(e =>
        {
            e.HasKey(v => v.Id);
            e.Property(v => v.Plate).IsRequired().HasMaxLength(20);
            e.Property(v => v.State).HasConversion<string>().IsRequired().HasMaxLength(2);
            e.Property(v => v.Color).HasConversion<string>().IsRequired().HasMaxLength(20);
            e.Property(v => v.Make).HasMaxLength(50);
            e.Property(v => v.Model).HasMaxLength(50);
            e.HasOne(v => v.Student)
                .WithMany()
                .HasForeignKey(v => v.StudentId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(v => v.StudentId);
        });

        // --- PermitApplication (the permit itself) ---------------------
        modelBuilder.Entity<PermitApplication>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.Campus).IsRequired().HasMaxLength(200);
            e.Property(a => a.Category).HasConversion<string>().IsRequired().HasMaxLength(20);
            e.Property(a => a.Term).HasConversion<string>().IsRequired().HasMaxLength(20);
            e.Property(a => a.Source).IsRequired().HasMaxLength(16);
            e.Property(a => a.Status).HasConversion<string>().IsRequired().HasMaxLength(20);
            e.Property(a => a.PaymentStatus).HasConversion<string>().IsRequired().HasMaxLength(20);
            e.Property(a => a.PayPalOrderId).HasMaxLength(64);
            e.Property(a => a.PayPalCaptureId).HasMaxLength(64);
            e.Property(a => a.ReceiptNumber).HasMaxLength(32);
            e.Property(a => a.VerifyToken).HasMaxLength(64);
            // Item 51: optimistic concurrency. Writers rotate the value
            // before saving; EF checks the loaded value on UPDATE.
            e.Property(a => a.ConcurrencyToken).IsConcurrencyToken();

            e.HasOne(a => a.Student)
                .WithMany()
                .HasForeignKey(a => a.StudentId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(a => a.Vehicle)
                .WithMany()
                .HasForeignKey(a => a.VehicleId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(a => a.PermitType)
                .WithMany()
                .HasForeignKey(a => a.PermitTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            // No duplicate permits: the same person cannot buy the
            // same type, for the same vehicle, over the same window
            // twice (REDO-PLAN item 15).
            e.HasIndex(a => new
                {
                    a.StudentId,
                    a.VehicleId,
                    a.PermitTypeId,
                    a.StartDate,
                    a.EndDate,
                })
                .IsUnique();

            // The receipt number IS the permit number — unique once
            // issued (SQLite unique indexes still allow many NULLs
            // pre-issuance). Same for the public verify token.
            e.HasIndex(a => a.ReceiptNumber).IsUnique();
            e.HasIndex(a => a.VerifyToken).IsUnique();
        });

        // --- WebhookEventLog (pre-built for R7, item 33) ---------------
        modelBuilder.Entity<WebhookEventLog>(e =>
        {
            e.HasKey(w => w.Id);
            e.Property(w => w.Provider).IsRequired().HasMaxLength(32);
            e.Property(w => w.EventId).IsRequired().HasMaxLength(128);
            e.Property(w => w.EventType).IsRequired().HasMaxLength(128);
            e.Property(w => w.Outcome).HasMaxLength(32);
            // Idempotency key: one row per (provider, event id).
            e.HasIndex(w => new { w.Provider, w.EventId }).IsUnique();
        });
    }

    // ------------------------------------------------------------------
    // Startup validation (REDO-PLAN item 10)
    // ------------------------------------------------------------------

    /// <summary>
    /// The portable half of the reference app's "refuse to run on bad
    /// pricing data": the app must not start on a broken permit-type
    /// catalog or an empty fee truth. Every failure throws an
    /// <see cref="InvalidOperationException"/> NAMING the failed check
    /// ("Startup validation failed — check '…': …") so the operator
    /// sees exactly which rule broke. Wiring into Program.cs is a
    /// one-line change request to R0; tests exercise this directly.
    /// </summary>
    public void ValidateStartupData(IFeeCatalog feeCatalog)
    {
        ArgumentNullException.ThrowIfNull(feeCatalog);

        var types = PermitTypes.AsNoTracking().ToList();
        var daily = types.SingleOrDefault(t => t.Code == PermitTypeCode.Daily);
        var term = types.SingleOrDefault(t => t.Code == PermitTypeCode.Term);

        if (types.Count != 2 || daily is null || term is null)
        {
            throw Failed("permit-type catalog",
                $"expected exactly the Daily and Term permit types to be seeded, " +
                $"found {types.Count} type row(s).");
        }

        if (!daily.Active || !term.Active)
        {
            throw Failed("permit-type active",
                $"both seeded types must be Active (Daily Active={daily.Active}, " +
                $"Term Active={term.Active}).");
        }

        if (daily.DurationDays <= 0 || term.DurationDays <= 0)
        {
            throw Failed("permit-type duration",
                $"both seeded types must last at least one day (Daily " +
                $"DurationDays={daily.DurationDays}, Term DurationDays={term.DurationDays}).");
        }

        var campuses = feeCatalog.All();
        if (!campuses.Any(c => c.HasPublishedSemesterPrice))
        {
            throw Failed("fee catalog term price",
                "the fee catalog holds no campus with a published semester " +
                "(Term) price — Term permits would have nothing honest to charge.");
        }

        if (!campuses.Any(c => c.DayPassCents.HasValue))
        {
            throw Failed("fee catalog day-pass price",
                "the fee catalog holds no campus with a parseable day-pass " +
                "price — Daily permits would have nothing honest to charge.");
        }

        static InvalidOperationException Failed(string check, string detail) =>
            new($"Startup validation failed — check '{check}': {detail}");
    }
}
