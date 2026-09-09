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
            // Exact category only (do not include sub-categories).
            query = query.Where(t => t.CategoryId == catId);
        }

        Items = await query
            .OrderByDescending(t => t.TransactionDate)
            .ThenByDescending(t => t.Id)
            .Take(500)
            .ToListAsync();

        CategoryOptions = await _categories.GetOptionsAsync();
    }
}
