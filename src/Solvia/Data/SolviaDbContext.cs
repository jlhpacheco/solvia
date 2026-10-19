using Microsoft.EntityFrameworkCore;
using Solvia.Models;
using Solvia.Models.Enums;

namespace Solvia.Data;

/// <summary>
/// Solvia's EF Core context (SQLite — a single file, zero install).
///
/// T1 (domain core): DbSets for the permit-only schema in docs/SCHEMA.md,
/// every enum stored as TEXT, the composite unique index that stops the
/// same permit being sold twice, and first-run seed data (the permit-type
/// catalog + the fictional demo students, incl. Maria).
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // --- Student -----------------------------------------------------
        modelBuilder.Entity<Student>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).HasMaxLength(64);
            e.Property(s => s.DisplayName).IsRequired().HasMaxLength(100);
            e.Property(s => s.Email).IsRequired().HasMaxLength(200);
        });

        // --- PermitType --------------------------------------------------
        modelBuilder.Entity<PermitType>(e =>
        {
            e.HasKey(t => t.Id);
            // Code is stored as TEXT ("Daily" / "Term"), per docs/SCHEMA.md.
            e.Property(t => t.Code).HasConversion<string>().IsRequired().HasMaxLength(20);
            e.Property(t => t.Name).IsRequired().HasMaxLength(200);
            e.HasIndex(t => t.Code).IsUnique();
        });

        // --- Vehicle -----------------------------------------------------
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

        // --- PermitApplication (the permit itself) -----------------------
        modelBuilder.Entity<PermitApplication>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.Campus).IsRequired().HasMaxLength(200);
            e.Property(a => a.Status).HasConversion<string>().IsRequired().HasMaxLength(20);
            e.Property(a => a.PaymentStatus).HasConversion<string>().IsRequired().HasMaxLength(20);
            e.Property(a => a.PayPalOrderId).HasMaxLength(64);
            e.Property(a => a.PayPalCaptureId).HasMaxLength(64);
            e.Property(a => a.ReceiptNumber).HasMaxLength(32);
            e.Property(a => a.VerifyToken).HasMaxLength(64);

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

            // No duplicate permits (docs/SCHEMA.md guard rule): the same
            // student cannot buy the same type, for the same vehicle, over
            // the same window twice.
            e.HasIndex(a => new
                {
                    a.StudentId,
                    a.VehicleId,
                    a.PermitTypeId,
                    a.StartDate,
                    a.EndDate,
                })
                .IsUnique();

            // The receipt number IS the permit number — unique once issued
            // (SQLite unique indexes still allow many NULLs pre-payment).
            e.HasIndex(a => a.ReceiptNumber).IsUnique();
            e.HasIndex(a => a.VerifyToken).IsUnique();
        });

        Seed(modelBuilder);
    }

    /// <summary>
    /// First-run seed data: the permit-type catalog and the fictional demo
    /// students. Every person here is invented for the demo — no real
    /// student data ever enters this database.
    /// </summary>
    private static void Seed(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PermitType>().HasData(
            new PermitType
            {
                Id = 1,
                Code = PermitTypeCode.Daily,
                Name = "Daily permit",
                DurationDays = 1,
                PriceCents = 0, // placeholder — real price is snapshotted from fees.csv
                Active = true,
            },
            new PermitType
            {
                Id = 2,
                Code = PermitTypeCode.Term,
                Name = "Term / semester permit",
                DurationDays = 120,
                PriceCents = 0, // placeholder — real price is snapshotted from fees.csv
                Active = true,
            });

        modelBuilder.Entity<Student>().HasData(
            new Student
            {
                Id = "maria",
                DisplayName = "Maria",
                Email = "maria@student.example",
            },
            new Student
            {
                Id = "diego",
                DisplayName = "Diego",
                Email = "diego@student.example",
            },
            new Student
            {
                Id = "priya",
                DisplayName = "Priya",
                Email = "priya@student.example",
            },
            new Student
            {
                Id = "sam",
                DisplayName = "Sam",
                Email = "sam@student.example",
            });
    }
}
