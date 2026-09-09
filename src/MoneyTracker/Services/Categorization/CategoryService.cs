using Microsoft.EntityFrameworkCore;
using MoneyTracker.Data;
using MoneyTracker.Domain;

namespace MoneyTracker.Services.Categorization;

public class CategoryValidationException : Exception
{
    public CategoryValidationException(string message) : base(message) { }
}

public interface ICategoryService
{
    Task<List<Category>> GetAllAsync();

    /// <summary>Flat list with a display path like "Parent → Child", ordered for dropdowns.</summary>
    Task<List<CategoryOption>> GetOptionsAsync();

    /// <summary>Get-or-create the whole path (e.g. "Σπίτι / Supermarket"). Returns the leaf.</summary>
    Task<Category> EnsurePathAsync(IEnumerable<string> segments);

    /// <summary>Find the leaf of an existing path without creating anything. Null if not found.</summary>
    Task<Category?> FindByPathAsync(IEnumerable<string> segments);

    Task<Category> CreateAsync(string name, int? parentId);

    Task MoveAsync(int categoryId, int? newParentId);

    Task DeleteAsync(int categoryId);
}

public record CategoryOption(int Id, string Path, byte Depth);

public class CategoryService : ICategoryService
{
    public const int MaxDepth = 3;

    private readonly AppDbContext _db;

    public CategoryService(AppDbContext db) => _db = db;

    public Task<List<Category>> GetAllAsync() =>
        _db.Categories.AsNoTracking().OrderBy(c => c.Name).ToListAsync();

    public async Task<List<CategoryOption>> GetOptionsAsync()
    {
        var all = await _db.Categories.AsNoTracking().ToListAsync();
        var byId = all.ToDictionary(c => c.Id);
        var options = all
            .Select(c => new CategoryOption(c.Id, BuildPath(c, byId), c.Depth))
            .OrderBy(o => o.Path, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return options;
    }

    public async Task<Category> EnsurePathAsync(IEnumerable<string> segments)
    {
        var names = segments
            .Select(s => s?.Trim() ?? string.Empty)
            .Where(s => s.Length > 0)
            .Take(MaxDepth)
            .ToList();

        if (names.Count == 0)
            throw new CategoryValidationException("Κενή διαδρομή κατηγορίας.");

        Category? parent = null;
        Category? current = null;
        byte depth = 0;

        foreach (var name in names)
        {
            depth++;
            var key = TextNormalizer.Key(name);
            var parentId = parent?.Id;

            current = await _db.Categories
                .FirstOrDefaultAsync(c => c.ParentId == parentId && c.NormalizedName == key);

            if (current == null)
            {
                current = new Category
                {
                    Name = name,
                    NormalizedName = key,
                    ParentId = parentId,
                    Depth = depth,
                    CreatedAt = DateTime.UtcNow
                };
                _db.Categories.Add(current);
                await _db.SaveChangesAsync(); // need Id for the next level
            }

            parent = current;
        }

        return current!;
    }

    public async Task<Category?> FindByPathAsync(IEnumerable<string> segments)
    {
        var names = segments
            .Select(s => s?.Trim() ?? string.Empty)
            .Where(s => s.Length > 0)
            .Take(MaxDepth)
            .ToList();
        if (names.Count == 0) return null;

        int? parentId = null;
        Category? current = null;
        foreach (var name in names)
        {
            var key = TextNormalizer.Key(name);
            var pid = parentId;
            current = await _db.Categories
                .FirstOrDefaultAsync(c => c.ParentId == pid && c.NormalizedName == key);
            if (current == null) return null;
            parentId = current.Id;
        }
        return current;
    }

    public async Task<Category> CreateAsync(string name, int? parentId)
    {
        name = (name ?? string.Empty).Trim();
        if (name.Length == 0)
            throw new CategoryValidationException("Το όνομα κατηγορίας είναι υποχρεωτικό.");

        byte depth = 1;
        if (parentId is int pid)
        {
            var parent = await _db.Categories.FindAsync(pid)
                ?? throw new CategoryValidationException("Η γονική κατηγορία δεν βρέθηκε.");
            if (parent.Depth >= MaxDepth)
                throw new CategoryValidationException(
                    $"Υπέρβαση μέγιστου βάθους ({MaxDepth} επίπεδα).");
            depth = (byte)(parent.Depth + 1);
        }

        var key = TextNormalizer.Key(name);
        var exists = await _db.Categories.AnyAsync(c => c.ParentId == parentId && c.NormalizedName == key);
        if (exists)
            throw new CategoryValidationException("Υπάρχει ήδη αδελφή κατηγορία με το ίδιο όνομα.");

        var cat = new Category
        {
            Name = name,
            NormalizedName = key,
            ParentId = parentId,
            Depth = depth,
            CreatedAt = DateTime.UtcNow
        };
        _db.Categories.Add(cat);
        await _db.SaveChangesAsync();
        return cat;
    }

    public async Task MoveAsync(int categoryId, int? newParentId)
    {
        if (categoryId == newParentId)
            throw new CategoryValidationException("Μια κατηγορία δεν μπορεί να είναι γονέας του εαυτού της.");

        var all = await _db.Categories.ToListAsync();
        var byId = all.ToDictionary(c => c.Id);
        if (!byId.TryGetValue(categoryId, out var node))
            throw new CategoryValidationException("Η κατηγορία δεν βρέθηκε.");

        Category? newParent = null;
        if (newParentId is int npid)
        {
            if (!byId.TryGetValue(npid, out newParent))
                throw new CategoryValidationException("Η νέα γονική κατηγορία δεν βρέθηκε.");

            // No circular hierarchy: the new parent must not be the node or a descendant of it.
            for (var p = newParent; p != null; p = p.ParentId is int id && byId.TryGetValue(id, out var pp) ? pp : null)
            {
                if (p.Id == node.Id)
                    throw new CategoryValidationException("Μη επιτρεπτή μετακίνηση: θα δημιουργούσε κύκλο.");
            }
        }

        // Depth check: new base depth + height of the moved subtree must fit in MaxDepth.
        var childrenByParent = all.GroupBy(c => c.ParentId)
            .ToDictionary(g => g.Key, g => g.ToList());
        int subtreeHeight = Height(node.Id, childrenByParent); // node counts as 1
        byte newDepth = (byte)((newParent?.Depth ?? 0) + 1);
        if (newDepth + subtreeHeight - 1 > MaxDepth)
            throw new CategoryValidationException(
                $"Υπέρβαση μέγιστου βάθους ({MaxDepth} επίπεδα) μετά τη μετακίνηση.");

        // Unique-name-among-siblings check at the destination.
        var clash = all.Any(c => c.ParentId == newParentId
                                 && c.NormalizedName == node.NormalizedName
                                 && c.Id != node.Id);
        if (clash)
            throw new CategoryValidationException("Υπάρχει ήδη αδελφή κατηγορία με το ίδιο όνομα στον προορισμό.");

        node.ParentId = newParentId;
        node.Depth = newDepth;
        UpdateDescendantDepths(node.Id, newDepth, childrenByParent);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int categoryId)
    {
        var node = await _db.Categories.FindAsync(categoryId)
            ?? throw new CategoryValidationException("Η κατηγορία δεν βρέθηκε.");
        var hasChildren = await _db.Categories.AnyAsync(c => c.ParentId == categoryId);
        if (hasChildren)
            throw new CategoryValidationException("Δεν μπορεί να διαγραφεί κατηγορία που έχει υποκατηγορίες.");
        var used = await _db.Transactions.AnyAsync(t => t.CategoryId == categoryId);
        if (used)
            throw new CategoryValidationException("Δεν μπορεί να διαγραφεί κατηγορία που χρησιμοποιείται σε κινήσεις.");
        _db.Categories.Remove(node);
        await _db.SaveChangesAsync();
    }

    private static string BuildPath(Category c, IReadOnlyDictionary<int, Category> byId)
    {
        var names = new List<string>();
        var cur = c;
        var guard = 0;
        while (cur != null && guard++ < MaxDepth + 1)
        {
            names.Insert(0, cur.Name);
            cur = cur.ParentId is int pid && byId.TryGetValue(pid, out var p) ? p : null;
        }
        return string.Join(" → ", names);
    }

    private static int Height(int nodeId, IReadOnlyDictionary<int?, List<Category>> childrenByParent)
    {
        if (!childrenByParent.TryGetValue(nodeId, out var children) || children.Count == 0)
            return 1;
        return 1 + children.Max(c => Height(c.Id, childrenByParent));
    }

    private static void UpdateDescendantDepths(int nodeId, byte nodeDepth,
        IReadOnlyDictionary<int?, List<Category>> childrenByParent)
    {
        if (!childrenByParent.TryGetValue(nodeId, out var children)) return;
        foreach (var child in children)
        {
            child.Depth = (byte)(nodeDepth + 1);
            UpdateDescendantDepths(child.Id, child.Depth, childrenByParent);
        }
    }
}
