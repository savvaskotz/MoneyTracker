namespace MoneyTracker.Domain;

/// <summary>
/// Configurable mapping of an Excel statement's layout to our internal model (spec §6).
/// Different banks have different columns, so this is data, not hard-coded logic.
/// </summary>
public class ImportProfile
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Worksheet name; null = first/active sheet.</summary>
    public string? SheetName { get; set; }

    /// <summary>1-based row number that contains the column headers.</summary>
    public int HeaderRowIndex { get; set; }

    // Column headers (matched case-insensitively against the header row).
    public string DateColumn { get; set; } = string.Empty;
    public string DescriptionColumn { get; set; } = string.Empty;

    /// <summary>Single signed amount column (used when Debit/Credit columns are null).</summary>
    public string? AmountColumn { get; set; }
    public string? DebitColumn { get; set; }
    public string? CreditColumn { get; set; }

    /// <summary>Optional: bank's own category column (strong suggestion source).</summary>
    public string? CategoryColumn { get; set; }

    /// <summary>Optional: bank reference/document number column (fingerprint component).</summary>
    public string? ReferenceColumn { get; set; }

    /// <summary>Optional: currency column; when null, DefaultCurrency is used.</summary>
    public string? CurrencyColumn { get; set; }
    public string DefaultCurrency { get; set; } = "EUR";

    public string DateFormat { get; set; } = "dd/MM/yyyy";

    /// <summary>Decimal separator used in the amount cells (e.g. "," for el-GR).</summary>
    public string DecimalSeparator { get; set; } = ",";
    public string GroupSeparator { get; set; } = ".";

    /// <summary>
    /// True: a positive amount is an expense (typical credit-card statement), a negative
    /// amount is a credit/refund (Income). False: reversed.
    /// </summary>
    public bool PositiveIsExpense { get; set; } = true;

    /// <summary>Separator used inside the bank category column for hierarchy, e.g. " / ".</summary>
    public string CategoryPathSeparator { get; set; } = " / ";

    public DateTime CreatedAt { get; set; }
}
