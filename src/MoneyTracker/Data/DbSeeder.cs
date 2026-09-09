using Microsoft.EntityFrameworkCore;
using MoneyTracker.Domain;

namespace MoneyTracker.Data;

public static class DbSeeder
{
    /// <summary>Seeds reference data that must exist for the app to work. Idempotent.</summary>
    public static void Seed(AppDbContext db)
    {
        PatchSchema(db);

        if (!db.ImportProfiles.Any())
        {
            // Derived from the real Piraeus Bank credit-card export (docs/DESIGN.md §6).
            db.ImportProfiles.Add(new ImportProfile
            {
                Name = "Τράπεζα Πειραιώς – Πιστωτική κάρτα",
                SheetName = "Κινήσεις Πιστωτικών Καρτών",
                // Headers are on the first row; the reader also auto-detects the header
                // row, so exports that add preamble lines above it still work.
                HeaderRowIndex = 1,
                DateColumn = "Ημ/νία Συναλλαγής",
                DescriptionColumn = "Περιγραφή Συναλλαγής",
                AmountColumn = "Ποσό",
                CategoryColumn = "Κατηγορία",
                ReferenceColumn = "Αριθμός Παραστατικού",
                CurrencyColumn = "Νόμισμα",
                DefaultCurrency = "EUR",
                DateFormat = "dd/MM/yyyy",
                DecimalSeparator = ",",
                GroupSeparator = ".",
                PositiveIsExpense = true,
                CategoryPathSeparator = " / ",
                CreatedAt = DateTime.UtcNow
            });
            db.SaveChanges();
        }
    }

    /// <summary>
    /// Lightweight idempotent column additions so existing databases created by an earlier
    /// version pick up new columns without being dropped (EnsureCreated does not alter tables).
    /// </summary>
    private static void PatchSchema(AppDbContext db)
    {
        db.Database.ExecuteSqlRaw(
            "IF COL_LENGTH('ImportStagingRows','Included') IS NULL " +
            "ALTER TABLE ImportStagingRows ADD Included bit NOT NULL " +
            "CONSTRAINT DF_ImportStagingRows_Included DEFAULT(1);");

        db.Database.ExecuteSqlRaw(
            "IF COL_LENGTH('ImportBatches','UpdatedCount') IS NULL " +
            "ALTER TABLE ImportBatches ADD UpdatedCount int NOT NULL " +
            "CONSTRAINT DF_ImportBatches_UpdatedCount DEFAULT(0);");
    }
}
