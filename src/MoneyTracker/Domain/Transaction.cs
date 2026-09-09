namespace MoneyTracker.Domain;

/// <summary>
/// A confirmed transaction. Amount is stored as a positive magnitude; the sign is
/// expressed by <see cref="Type"/> (spec §5). See docs/DESIGN.md §2.3.
/// </summary>
public class Transaction
{
    public long Id { get; set; }

    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;

    public DateTime TransactionDate { get; set; }

    /// <summary>Positive magnitude (>= 0).</summary>
    public decimal Amount { get; set; }

    public TransactionType Type { get; set; }

    public int? CategoryId { get; set; }
    public Category? Category { get; set; }

    /// <summary>Cleaned description shown to the user.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Raw description exactly as read from the Excel (never altered).</summary>
    public string OriginalDescription { get; set; } = string.Empty;

    /// <summary>Normalized form used for matching/suggestions (spec §10).</summary>
    public string NormalizedDescription { get; set; } = string.Empty;

    /// <summary>Bank document/reference number, when the statement provides one.</summary>
    public string? Reference { get; set; }

    /// <summary>SHA-256 hex fingerprint for duplicate detection (spec §7).</summary>
    public string Fingerprint { get; set; } = string.Empty;

    public int ImportBatchId { get; set; }
    public ImportBatch ImportBatch { get; set; } = null!;

    public string CurrencyCode { get; set; } = "EUR";

    /// <summary>Optional free-text note the user can attach to the transaction.</summary>
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Signed value: negative for expenses, positive for income.</summary>
    public decimal SignedAmount => Type == TransactionType.Income ? Amount : -Amount;
}
