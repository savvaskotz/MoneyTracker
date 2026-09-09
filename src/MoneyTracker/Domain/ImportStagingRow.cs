namespace MoneyTracker.Domain;

/// <summary>
/// A parsed candidate row shown in the preview before the user confirms (spec §8).
/// Nothing reaches <see cref="Transaction"/> until confirmation.
/// </summary>
public class ImportStagingRow
{
    public long Id { get; set; }

    public int ImportBatchId { get; set; }
    public ImportBatch ImportBatch { get; set; } = null!;

    public int RowIndex { get; set; }

    public DateTime TransactionDate { get; set; }
    public decimal Amount { get; set; }
    public TransactionType Type { get; set; }

    public string OriginalDescription { get; set; } = string.Empty;
    public string NormalizedDescription { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public string CurrencyCode { get; set; } = "EUR";

    public string Fingerprint { get; set; } = string.Empty;

    public int? SuggestedCategoryId { get; set; }
    public int? SelectedCategoryId { get; set; }
    public SuggestionSource SuggestionSource { get; set; } = SuggestionSource.None;

    public bool IsDuplicate { get; set; }
    public string? DuplicateReason { get; set; }
}
