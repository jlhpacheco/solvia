using Microsoft.EntityFrameworkCore;

namespace Solvia.Data;

/// <summary>Additive, idempotent SQLite upgrade after the ordinary EnsureCreated call.</summary>
public static class PrepaidSchema
{
    public static void EnsureCreated(SolviaDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);
        // EnsureCreated does not add tables to an already populated database.
        // Keep this DDL aligned with the mapped model; no existing table changes.
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "PrepaidPurchases" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_PrepaidPurchases" PRIMARY KEY,
                "StudentId" TEXT NOT NULL,
                "Campus" TEXT NOT NULL,
                "Category" TEXT NOT NULL,
                "VehicleId" TEXT NOT NULL,
                "Term" TEXT NOT NULL,
                "UnitPriceCents" INTEGER NOT NULL,
                "TotalCents" INTEGER NOT NULL,
                "PaymentStatus" TEXT NOT NULL,
                "PayPalOrderId" TEXT NULL,
                "PayPalCaptureId" TEXT NULL,
                "PaidAtUtc" TEXT NULL,
                "SubmittedAtUtc" TEXT NOT NULL,
                "ConcurrencyToken" TEXT NOT NULL,
                "DraftKey" TEXT NOT NULL,
                CONSTRAINT "FK_PrepaidPurchases_Students_StudentId" FOREIGN KEY ("StudentId") REFERENCES "Students" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_PrepaidPurchases_Vehicles_VehicleId" FOREIGN KEY ("VehicleId") REFERENCES "Vehicles" ("Id") ON DELETE RESTRICT
            );
            CREATE TABLE IF NOT EXISTS "PrepaidPurchaseItems" (
                "PurchaseId" TEXT NOT NULL,
                "ApplicationId" TEXT NOT NULL,
                CONSTRAINT "PK_PrepaidPurchaseItems" PRIMARY KEY ("PurchaseId", "ApplicationId"),
                CONSTRAINT "FK_PrepaidPurchaseItems_PrepaidPurchases_PurchaseId" FOREIGN KEY ("PurchaseId") REFERENCES "PrepaidPurchases" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_PrepaidPurchaseItems_PermitApplications_ApplicationId" FOREIGN KEY ("ApplicationId") REFERENCES "PermitApplications" ("Id") ON DELETE RESTRICT
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_PrepaidPurchases_DraftKey" ON "PrepaidPurchases" ("DraftKey");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_PrepaidPurchases_PayPalOrderId" ON "PrepaidPurchases" ("PayPalOrderId");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_PrepaidPurchases_PayPalCaptureId" ON "PrepaidPurchases" ("PayPalCaptureId");
            CREATE INDEX IF NOT EXISTS "IX_PrepaidPurchases_StudentId" ON "PrepaidPurchases" ("StudentId");
            CREATE INDEX IF NOT EXISTS "IX_PrepaidPurchases_VehicleId" ON "PrepaidPurchases" ("VehicleId");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_PrepaidPurchaseItems_ApplicationId" ON "PrepaidPurchaseItems" ("ApplicationId");
            """);
    }
}
