using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MoneyTracker.Data;
using MoneyTracker.Domain;
using MoneyTracker.Services.Dashboard;

namespace MoneyTracker.Pages.Dashboard;

public class CategoryModel : PageModel
{
    private readonly AppDbContext _db;

    public CategoryModel(AppDbContext db) => _db = db;

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }
    [BindProperty(SupportsGet = true)]
    public DateRangePreset Preset { get; set; } = DateRangePreset.ThisMonth;
    [BindProperty(SupportsGet = true)]
    public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)]
    public DateTime? To { get; set; }

    public bool Found { get; private set; }
    public string CategoryPath { get; private set; } = string.Empty;
    public DateTime RangeFrom { get; private set; }
    public DateTime RangeTo { get; private set; }
    public decimal Total { get; private set; }
    public List<Section> Sections { get; } = new();

    public class Section
    {
        public int CategoryId { get; set; }
        public string Path { get; set; } = string.Empty;
        public bool IsSelf { get; set; }
        public decimal Subtotal { get; set; }
        public List<Transaction> Transactions { get; set; } = new();
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var (from, to) = new DashboardFilter { Preset = Preset, From = From, To = To }.Resolve(DateTime.Today);
        RangeFrom = from;
        RangeTo = to;

        var cats = await _db.Categories.AsNoTracking().ToListAsync();
        var byId = cats.ToDictionary(c => c.Id);
        if (!byId.ContainsKey(Id))
        {
            Found = false;
            return Page();
        }
        Found = true;
        CategoryPath = BuildPath(Id, byId);

        var childrenByParent = cats
            .Where(c => c.ParentId != null)
            .GroupBy(c => c.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Name).Select(x => x.Id).ToList());

        // Ordered list of category ids: the selected one first, then descendants (DFS).
        var ordered = new List<int> { Id };
        void Walk(int parent)
        {
            if (!childrenByParent.TryGetValue(parent, out var children)) return;
            foreach (var c in children) { ordered.Add(c); Walk(c); }
        }
        Walk(Id);

        var idSet = ordered.ToHashSet();
        var txs = await _db.Transactions.AsNoTracking()
            .Where(t => t.CategoryId != null && idSet.Contains(t.CategoryId.Value)
                        && t.TransactionDate >= from && t.TransactionDate <= to)
            .OrderByDescending(t => t.TransactionDate).ThenByDescending(t => t.Id)
            .ToListAsync();

        var byCat = txs.GroupBy(t => t.CategoryId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var cid in ordered)
        {
            var isSelf = cid == Id;
            byCat.TryGetValue(cid, out var list);
            list ??= new List<Transaction>();
            // Show the selected category always; show sub-categories only when they have activity.
            if (!isSelf && list.Count == 0) continue;

            Sections.Add(new Section
            {
                CategoryId = cid,
                Path = BuildPath(cid, byId),
                IsSelf = isSelf,
                Transactions = list,
                Subtotal = list.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount)
            });
        }

        Total = Sections.Sum(s => s.Subtotal);
        return Page();
    }

    private static string BuildPath(int id, IReadOnlyDictionary<int, Category> byId)
    {
        var names = new List<string>();
        int? cur = id;
        var guard = 0;
        while (cur is int cid && byId.TryGetValue(cid, out var c) && guard++ < 5)
        {
            names.Insert(0, c.Name);
            cur = c.ParentId;
        }
        return string.Join(" → ", names);
    }
}
