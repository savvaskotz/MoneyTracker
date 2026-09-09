namespace MoneyTracker.Domain;

/// <summary>
/// Hierarchical category (adjacency list). Max depth = 3 (enforced in CategoryService).
/// </summary>
public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;

    public int? ParentId { get; set; }
    public Category? Parent { get; set; }
    public ICollection<Category> Children { get; set; } = new List<Category>();

    /// <summary>1..3. Maintained by the application on create/move.</summary>
    public byte Depth { get; set; }

    public DateTime CreatedAt { get; set; }

    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
