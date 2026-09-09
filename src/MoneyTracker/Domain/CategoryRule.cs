namespace MoneyTracker.Domain;

/// <summary>
/// A categorization rule. Created explicitly by the user or learned from corrections (spec §11).
/// </summary>
public class CategoryRule
{
    public int Id { get; set; }

    /// <summary>Normalized pattern to match against a transaction's NormalizedDescription.</summary>
    public string Pattern { get; set; } = string.Empty;

    public MatchType MatchType { get; set; } = MatchType.Exact;

    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    /// <summary>Higher wins on ties.</summary>
    public int Priority { get; set; }

    public RuleSource Source { get; set; }

    public int HitCount { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
