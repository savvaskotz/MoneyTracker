using Microsoft.EntityFrameworkCore;
using MoneyTracker.Data;
using MoneyTracker.Domain;
using MoneyTracker.Services.Categorization;
using MoneyTracker.Services.Duplicates;

namespace MoneyTracker.Services.Import;

public interface IImportService
{
    /// <summary>Parse + stage a file for preview. Returns the batch id, or errors (no batch).</summary>
    Task<(int? BatchId, List<string> Errors)> BuildPreviewAsync(Stream excelStream, string fileName);

    Task ConfirmAsync(int batchId,
        IDictionary<long, int?> selectedCategoryByRowId,
        IDictionary<long, bool> includedByRowId);

    Task CancelAsync(int batchId);
}

public class ImportService : IImportService
{
    private readonly AppDbContext _db;
    private readonly IExcelStatementReader _reader;
    private readonly INormalizationService _normalizer;
    private readonly IFingerprintService _fingerprint;
    private readonly ICategoryService _categories;
    private readonly ICategorySuggestionService _suggestions;
    private readonly ICategoryLearningService _learning;

    public ImportService(
        AppDbContext db,
        IExcelStatementReader reader,
        INormalizationService normalizer,
        IFingerprintService fingerprint,
        ICategoryService categories,
        ICategorySuggestionService suggestions,
        ICategoryLearningService learning)
    {
        _db = db;
        _reader = reader;
        _normalizer = normalizer;
        _fingerprint = fingerprint;
        _categories = categories;
        _suggestions = suggestions;
        _learning = learning;
    }

    public async Task<(int? BatchId, List<string> Errors)> BuildPreviewAsync(Stream excelStream, string fileName)
    {
        var profile = await _db.ImportProfiles.OrderBy(p => p.Id).FirstOrDefaultAsync();
        if (profile == null)
            return (null, new List<string> { "Δεν έχει οριστεί import profile." });

        var read = _reader.Read(excelStream, profile);
        if (read.HasErrors)
            return (null, read.Errors);

        var account = await ResolveAccountAsync(read.AccountIdentifier);

        // Resolve the bank's category column to an EXISTING category only — we do NOT create
        // categories automatically. If the user hasn't created it, the row stays uncategorized.
        var bankCategoryIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var distinctPaths = read.Rows
            .Select(r => r.BankCategoryPath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var path in distinctPaths)
        {
            if (IsUncategorizedBankLabel(path)) continue;
            var segments = path.Split(profile.CategoryPathSeparator,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var leaf = await _categories.FindByPathAsync(segments);
            if (leaf != null) bankCategoryIds[path] = leaf.Id;
        }

        // Normalize + compute occurrence ordinals (stable, file order) for the fingerprint.
        var normalized = read.Rows
            .Select(r => new { Row = r, Norm = _normalizer.Normalize(r.OriginalDescription) })
            .ToList();

        // Existing transactions for this account, for duplicate/update detection.
        var existing = await _db.Transactions
            .Where(t => t.AccountId == account.Id)
            .Select(t => new { t.Fingerprint, t.Reference, t.CategoryId })
            .ToListAsync();
        var fpToExisting = existing
            .GroupBy(e => e.Fingerprint)
            .ToDictionary(g => g.Key, g => g.First());
        var refCounts = existing
            .Where(e => !string.IsNullOrEmpty(e.Reference))
            .GroupBy(e => e.Reference!)
            .ToDictionary(g => g.Key, g => g.Count());
        var refToExisting = existing
            .Where(e => !string.IsNullOrEmpty(e.Reference))
            .GroupBy(e => e.Reference!)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.First());

        var ordinalCounter = new Dictionary<string, int>();
        var seenInFile = new HashSet<string>();

        var ctx = await _suggestions.CreateContextAsync();

        var batch = new ImportBatch
        {
            AccountId = account.Id,
            ImportProfileId = profile.Id,
            FileName = fileName,
            Status = ImportStatus.Pending,
            TotalRows = read.Rows.Count,
            CreatedAt = DateTime.UtcNow
        };
        _db.ImportBatches.Add(batch);
        await _db.SaveChangesAsync();

        int dupCount = 0, existCount = 0;
        foreach (var item in normalized)
        {
            var r = item.Row;
            var norm = item.Norm;

            var groupKey = string.Join("|", r.TransactionDate.ToString("yyyy-MM-dd"),
                r.SignedAmount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                norm, r.Reference ?? "");
            ordinalCounter.TryGetValue(groupKey, out var ord);
            ord += 1;
            ordinalCounter[groupKey] = ord;

            var fp = _fingerprint.Compute(account.Id, r.TransactionDate, r.SignedAmount, norm, r.Reference, ord);

            // Detect existing (-> update) vs in-file duplicate (-> skip) vs new.
            string? dupReason = null;
            int? existingCatId = null;
            if (fpToExisting.TryGetValue(fp, out var m1))
            {
                dupReason = "ExistsInDb";
                existingCatId = m1.CategoryId;
            }
            else if (r.Reference != null && refCounts.TryGetValue(r.Reference, out var rc) && rc == 1)
            {
                // Same παραστατικό already stored (even if some field changed) -> update it.
                dupReason = "ExistsInDb";
                existingCatId = refToExisting[r.Reference].CategoryId;
            }
            else if (!seenInFile.Add(fp))
            {
                dupReason = "DuplicateInFile";
            }

            if (dupReason == "ExistsInDb") existCount++;
            else if (dupReason == "DuplicateInFile") dupCount++;

            int? bankCatId = r.BankCategoryPath != null && bankCategoryIds.TryGetValue(r.BankCategoryPath, out var bid)
                ? bid : (int?)null;
            var (suggestedId, source) = _suggestions.Suggest(ctx, norm, bankCatId);

            // For an existing row, keep its current category by default (update should not wipe it).
            int? selected = dupReason == "ExistsInDb" ? (existingCatId ?? suggestedId) : suggestedId;

            _db.ImportStagingRows.Add(new ImportStagingRow
            {
                ImportBatchId = batch.Id,
                RowIndex = r.RowIndex,
                TransactionDate = r.TransactionDate,
                Amount = r.Amount,
                Type = r.Type,
                OriginalDescription = r.OriginalDescription,
                NormalizedDescription = norm,
                Reference = r.Reference,
                CurrencyCode = r.CurrencyCode,
                Fingerprint = fp,
                SuggestedCategoryId = suggestedId,
                SelectedCategoryId = selected,
                SuggestionSource = source,
                IsDuplicate = dupReason != null,
                DuplicateReason = dupReason,
                // In-file duplicates are excluded by default; new & existing rows are included.
                Included = dupReason != "DuplicateInFile"
            });
        }

        batch.DuplicateCount = dupCount;
        batch.UpdatedCount = existCount;
        batch.NewCount = read.Rows.Count - dupCount - existCount;
        await _db.SaveChangesAsync();

        return (batch.Id, new List<string>());
    }

    public async Task ConfirmAsync(int batchId,
        IDictionary<long, int?> selectedCategoryByRowId,
        IDictionary<long, bool> includedByRowId)
    {
        var batch = await _db.ImportBatches
            .Include(b => b.StagingRows)
            .FirstOrDefaultAsync(b => b.Id == batchId)
            ?? throw new InvalidOperationException("Το import δεν βρέθηκε.");

        if (batch.Status != ImportStatus.Pending)
            throw new InvalidOperationException("Το import έχει ήδη ολοκληρωθεί ή ακυρωθεί.");

        await using var tx = await _db.Database.BeginTransactionAsync();

        int newCount = 0, updatedCount = 0;
        foreach (var row in batch.StagingRows.OrderBy(s => s.RowIndex))
        {
            // The user can uncheck a row in the preview to skip it entirely. Every processable
            // row renders a checkbox, so an absent key means the user unchecked it.
            var included = includedByRowId.TryGetValue(row.Id, out var inc) && inc;
            if (!included) continue;
            if (row.DuplicateReason == "DuplicateInFile") continue;

            var selected = selectedCategoryByRowId.TryGetValue(row.Id, out var sel)
                ? sel
                : row.SelectedCategoryId;

            if (row.DuplicateReason == "ExistsInDb")
            {
                // Update the existing transaction instead of inserting a duplicate.
                var existing = await _db.Transactions
                    .FirstOrDefaultAsync(t => t.AccountId == batch.AccountId && t.Fingerprint == row.Fingerprint);
                if (existing == null && !string.IsNullOrEmpty(row.Reference))
                {
                    var matches = await _db.Transactions
                        .Where(t => t.AccountId == batch.AccountId && t.Reference == row.Reference)
                        .ToListAsync();
                    if (matches.Count == 1) existing = matches[0];
                }

                if (existing != null)
                {
                    var prevCat = existing.CategoryId;
                    existing.CategoryId = selected;
                    existing.Amount = row.Amount;
                    existing.Type = row.Type;
                    existing.TransactionDate = row.TransactionDate;
                    existing.Description = row.OriginalDescription;
                    existing.OriginalDescription = row.OriginalDescription;
                    existing.NormalizedDescription = row.NormalizedDescription;
                    existing.Reference = row.Reference;
                    existing.Fingerprint = row.Fingerprint;
                    existing.CurrencyCode = row.CurrencyCode;
                    updatedCount++;

                    if (selected is int uc && uc != prevCat)
                        await _learning.LearnAsync(row.NormalizedDescription, uc);
                    continue;
                }
                // Fall through to insert if the existing row could not be resolved.
            }

            _db.Transactions.Add(new Transaction
            {
                AccountId = batch.AccountId,
                TransactionDate = row.TransactionDate,
                Amount = row.Amount,
                Type = row.Type,
                CategoryId = selected,
                Description = row.OriginalDescription,
                OriginalDescription = row.OriginalDescription,
                NormalizedDescription = row.NormalizedDescription,
                Reference = row.Reference,
                Fingerprint = row.Fingerprint,
                ImportBatchId = batch.Id,
                CurrencyCode = row.CurrencyCode,
                CreatedAt = DateTime.UtcNow
            });
            newCount++;

            // Learn when the user picked a category different from the suggestion (spec §11).
            if (selected is int catId && catId != row.SuggestedCategoryId)
                await _learning.LearnAsync(row.NormalizedDescription, catId);
        }

        batch.NewCount = newCount;
        batch.UpdatedCount = updatedCount;
        batch.Status = ImportStatus.Completed;
        batch.CompletedAt = DateTime.UtcNow;

        // Staging is no longer needed once promoted.
        _db.ImportStagingRows.RemoveRange(batch.StagingRows);

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task CancelAsync(int batchId)
    {
        var batch = await _db.ImportBatches
            .Include(b => b.StagingRows)
            .FirstOrDefaultAsync(b => b.Id == batchId);
        if (batch == null || batch.Status != ImportStatus.Pending) return;

        batch.Status = ImportStatus.Cancelled;
        _db.ImportStagingRows.RemoveRange(batch.StagingRows);
        await _db.SaveChangesAsync();
    }

    private async Task<Account> ResolveAccountAsync(string? identifier)
    {
        identifier = string.IsNullOrWhiteSpace(identifier) ? "DEFAULT" : identifier.Trim();
        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.Identifier == identifier);
        if (account != null) return account;

        account = new Account
        {
            Identifier = identifier,
            Name = identifier == "DEFAULT" ? "Default account" : identifier,
            CreatedAt = DateTime.UtcNow
        };
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync();
        return account;
    }

    private static bool IsUncategorizedBankLabel(string path)
    {
        var p = path.Trim().ToUpperInvariant();
        return p is "ΧΩΡΙΣ ΚΑΤΗΓΟΡΙΑ" or "ΑΝΑΚΑΤΑΝΟΜΗ" or "UNCATEGORIZED";
    }
}
