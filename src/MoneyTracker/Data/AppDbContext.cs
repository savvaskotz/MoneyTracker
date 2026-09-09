using Microsoft.EntityFrameworkCore;
using MoneyTracker.Domain;

namespace MoneyTracker.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<ImportStagingRow> ImportStagingRows => Set<ImportStagingRow>();
    public DbSet<CategoryRule> CategoryRules => Set<CategoryRule>();
    public DbSet<ImportProfile> ImportProfiles => Set<ImportProfile>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // ---- Category ----
        b.Entity<Category>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.NormalizedName).HasMaxLength(100).IsRequired();
            e.HasOne(x => x.Parent)
                .WithMany(x => x.Children)
                .HasForeignKey(x => x.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
            // No two siblings with the same normalized name.
            e.HasIndex(x => new { x.ParentId, x.NormalizedName }).IsUnique();
        });

        // ---- Account ----
        b.Entity<Account>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Identifier).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.Identifier).IsUnique();
        });

        // ---- Transaction ----
        b.Entity<Transaction>(e =>
        {
            e.Property(x => x.Amount).HasColumnType("decimal(18,2)");
            e.Property(x => x.Type).HasConversion<byte>();
            e.Property(x => x.Description).HasMaxLength(400).IsRequired();
            e.Property(x => x.OriginalDescription).HasMaxLength(1000).IsRequired();
            e.Property(x => x.NormalizedDescription).HasMaxLength(400).IsRequired();
            e.Property(x => x.Reference).HasMaxLength(100);
            e.Property(x => x.Fingerprint).HasMaxLength(64).IsFixedLength().IsRequired();
            e.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            e.Property(x => x.Note).HasMaxLength(1000);
            e.Ignore(x => x.SignedAmount);

            e.HasOne(x => x.Account).WithMany(x => x.Transactions)
                .HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Category).WithMany(x => x.Transactions)
                .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.ImportBatch).WithMany(x => x.Transactions)
                .HasForeignKey(x => x.ImportBatchId).OnDelete(DeleteBehavior.Restrict);

            // Second line of defence against duplicate inserts (spec §7).
            e.HasIndex(x => new { x.AccountId, x.Fingerprint }).IsUnique();
            e.HasIndex(x => x.TransactionDate);
            e.HasIndex(x => x.CategoryId);
            e.HasIndex(x => x.NormalizedDescription);
        });

        // ---- ImportBatch ----
        b.Entity<ImportBatch>(e =>
        {
            e.Property(x => x.FileName).HasMaxLength(260).IsRequired();
            e.Property(x => x.Status).HasConversion<byte>();
            e.HasOne(x => x.Account).WithMany()
                .HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ImportProfile).WithMany()
                .HasForeignKey(x => x.ImportProfileId).OnDelete(DeleteBehavior.SetNull);
        });

        // ---- ImportStagingRow ----
        b.Entity<ImportStagingRow>(e =>
        {
            e.Property(x => x.Amount).HasColumnType("decimal(18,2)");
            e.Property(x => x.Type).HasConversion<byte>();
            e.Property(x => x.SuggestionSource).HasConversion<byte>();
            e.Property(x => x.OriginalDescription).HasMaxLength(1000).IsRequired();
            e.Property(x => x.NormalizedDescription).HasMaxLength(400).IsRequired();
            e.Property(x => x.Reference).HasMaxLength(100);
            e.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            e.Property(x => x.Fingerprint).HasMaxLength(64).IsFixedLength().IsRequired();
            e.Property(x => x.DuplicateReason).HasMaxLength(50);
            e.HasOne(x => x.ImportBatch).WithMany(x => x.StagingRows)
                .HasForeignKey(x => x.ImportBatchId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.ImportBatchId);
        });

        // ---- CategoryRule ----
        b.Entity<CategoryRule>(e =>
        {
            e.Property(x => x.Pattern).HasMaxLength(200).IsRequired();
            e.Property(x => x.MatchType).HasConversion<byte>();
            e.Property(x => x.Source).HasConversion<byte>();
            e.HasOne(x => x.Category).WithMany()
                .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.Pattern, x.MatchType }).IsUnique();
        });

        // ---- ImportProfile ----
        b.Entity<ImportProfile>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.SheetName).HasMaxLength(100);
            e.Property(x => x.DateColumn).HasMaxLength(50).IsRequired();
            e.Property(x => x.DescriptionColumn).HasMaxLength(50).IsRequired();
            e.Property(x => x.AmountColumn).HasMaxLength(50);
            e.Property(x => x.DebitColumn).HasMaxLength(50);
            e.Property(x => x.CreditColumn).HasMaxLength(50);
            e.Property(x => x.CategoryColumn).HasMaxLength(50);
            e.Property(x => x.ReferenceColumn).HasMaxLength(50);
            e.Property(x => x.CurrencyColumn).HasMaxLength(50);
            e.Property(x => x.DefaultCurrency).HasMaxLength(3);
            e.Property(x => x.DateFormat).HasMaxLength(30);
            e.Property(x => x.DecimalSeparator).HasMaxLength(4);
            e.Property(x => x.GroupSeparator).HasMaxLength(4);
            e.Property(x => x.CategoryPathSeparator).HasMaxLength(10);
            e.HasIndex(x => x.Name).IsUnique();
        });
    }
}
