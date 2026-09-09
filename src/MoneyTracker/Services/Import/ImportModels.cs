using MoneyTracker.Domain;

namespace MoneyTracker.Services.Import;

/// <summary>A row read from the Excel and mapped to a common internal shape (spec §6).</summary>
public class StatementRow
{
    public int RowIndex { get; set; }
    public DateTime TransactionDate { get; set; }

    /// <summary>Positive magnitude.</summary>
    public decimal Amount { get; set; }
    public TransactionType Type { get; set; }

    /// <summary>Signed value as read (negative = credit/refund on a credit-card statement).</summary>
    public decimal SignedAmount { get; set; }

    public string OriginalDescription { get; set; } = string.Empty;
    public string? BankCategoryPath { get; set; }
    public string? Reference { get; set; }
    public string CurrencyCode { get; set; } = "EUR";
}

/// <summary>Result of reading a statement file.</summary>
public class StatementReadResult
{
    public string? AccountIdentifier { get; set; }
    public List<StatementRow> Rows { get; } = new();
    public List<string> Errors { get; } = new();
    public bool HasErrors => Errors.Count > 0;
}
