namespace MoneyTracker.Domain;

/// <summary>
/// A bank account or card. v1 is effectively single-account, but the table exists
/// so fingerprints and the dashboard are ready for multiple accounts/cards (spec §17).
/// </summary>
public class Account
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Stable identifier from the statement, e.g. "490845******5019".</summary>
    public string Identifier { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
