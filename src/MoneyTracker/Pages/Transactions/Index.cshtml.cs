using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MoneyTracker.Data;
using MoneyTracker.Domain;
using MoneyTracker.Services.Categorization;

namespace MoneyTracker.Pages.Transactions;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ICategoryService _categories;
    private readonly ICategoryLearningService _learning;

    public IndexModel(AppDbContext db, ICategoryService categories, ICategoryLearningService learning)
    {
        _db = db;
        _categories = categories;
        _learning = learning;
    }

    [BindProperty(SupportsGet = true)]
    public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)]
    public DateTime? To { get; set; }
    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    /// <summary>"" = όλες, "none" = χωρίς κατηγορία, ή το id μιας κατηγορίας (μαζί με τα children της).</summary>
    [BindProperty(SupportsGet = true)]
    public string? Cat { get; set; }

    [BindProperty]
    public Dictionary<long, int?> Selected { get; set; } = new();

    [BindProperty]
    public long DeleteId { get; set; }

    public List<Transaction> Items { get; private set; } = new();
    public List<CategoryOption> CategoryOptions { get; private set; } = new();
    public string? Message { get; set; }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostSaveAsync()
    {
        // Apply category changes and learn from them (spec §11).
        var ids = Selected.Keys.ToList();
        var txs = await _db.Transactions.Where(t => ids.Contains(t.Id)).ToListAsync();
        int changed = 0;
        foreach (var t in txs)
        {
            var newCat = Selected[t.Id];
            if (t.CategoryId != newCat)
            {
                t.CategoryId = newCat;
                changed++;
                if (newCat is int cid)
                    await _learning.LearnAsync(t.NormalizedDescription, cid);
            }
        }
        await _db.SaveChangesAsync();
        Message = changed > 0 ? $"Ενημερώθηκαν {changed} κινήσεις." : "Καμία αλλαγή.";

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var t = await _db.Transactions.FindAsync(DeleteId);
        if (t != null)
        {
            _db.Transactions.Remove(t);
            await _db.SaveChangesAsync();
            Message = "Η κίνηση διαγράφηκε.";
        }
        await LoadAsync();
        return Page();
    }

    private async Task LoadAsync()
    {
        var query = _db.Transactions.AsNoTracking().AsQueryable();
        if (From is DateTime f) query = query.Where(t => t.TransactionDate >= f.Date);
        if (To is DateTime tt) query = query.Where(t => t.TransactionDate <= tt.Date);
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var q = Q.Trim();
            query = query.Where(t => t.OriginalDescription.Contains(q) || t.NormalizedDescription.Contains(q));
        }

        if (Cat == "none")
        {
            query = query.Where(t => t.CategoryId == null);
        }
        else if (int.TryParse(Cat, out var catId))
        {
            var ids = await DescendantCategoryIdsAsync(catId);
            query = query.Where(t => t.CategoryId != null && ids.Contains(t.CategoryId.Value));
        }

        Items = await query
            .OrderByDescending(t => t.TransactionDate)
            .ThenByDescending(t => t.Id)
            .Take(500)
            .ToListAsync();

        CategoryOptions = await _categories.GetOptionsAsync();
    }

    /// <summary>The category plus all its descendants (depth ≤ 3), so filtering a parent includes children.</summary>
    private async Task<HashSet<int>> DescendantCategoryIdsAsync(int categoryId)
    {
        var cats = await _db.Categories.AsNoTracking()
            .Select(c => new { c.Id, c.ParentId })
            .ToListAsync();
        var childrenByParent = cats
            .Where(c => c.ParentId != null)
            .GroupBy(c => c.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToList());

        var ids = new HashSet<int> { categoryId };
        var stack = new Stack<int>();
        stack.Push(categoryId);
        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            if (childrenByParent.TryGetValue(cur, out var children))
                foreach (var c in children)
                    if (ids.Add(c)) stack.Push(c);
        }
        return ids;
    }
}
