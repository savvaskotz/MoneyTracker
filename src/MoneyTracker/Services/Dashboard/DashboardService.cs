using Microsoft.EntityFrameworkCore;
using MoneyTracker.Data;
using MoneyTracker.Domain;

namespace MoneyTracker.Services.Dashboard;

public enum DateRangePreset
{
    Today,
    ThisWeek,
    ThisMonth,
    PreviousMonth,
    ThisYear,
    Custom
}

public class DashboardFilter
{
    public DateRangePreset Preset { get; set; } = DateRangePreset.ThisMonth;
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    public (DateTime From, DateTime To) Resolve(DateTime today)
    {
        today = today.Date;
        switch (Preset)
        {
            case DateRangePreset.Today:
                return (today, today);
            case DateRangePreset.ThisWeek:
            {
                int diff = ((int)today.DayOfWeek + 6) % 7; // Monday-based
                var start = today.AddDays(-diff);
                return (start, start.AddDays(6));
            }
            case DateRangePreset.ThisMonth:
            {
                var start = new DateTime(today.Year, today.Month, 1);
                return (start, start.AddMonths(1).AddDays(-1));
            }
            case DateRangePreset.PreviousMonth:
            {
                var start = new DateTime(today.Year, today.Month, 1).AddMonths(-1);
                return (start, start.AddMonths(1).AddDays(-1));
            }
            case DateRangePreset.ThisYear:
                return (new DateTime(today.Year, 1, 1), new DateTime(today.Year, 12, 31));
            case DateRangePreset.Custom:
                var f = (From ?? today).Date;
                var t = (To ?? today).Date;
                return f <= t ? (f, t) : (t, f);
            default:
                return (today, today);
        }
    }
}

public class CategoryTotalNode
{
    public int CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public byte Depth { get; set; }
    public decimal Direct { get; set; }   // amount on this category itself
    public decimal Total { get; set; }    // rolled up over all descendants (spec §15)
    public List<CategoryTotalNode> Children { get; } = new();
}

public class MerchantTotal
{
    public string Merchant { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int Count { get; set; }
}

public class MonthTotal
{
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Expenses { get; set; }
    public decimal Income { get; set; }
    public string Label => $"{Month:00}/{Year}";
}

public class DashboardViewModel
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }

    public decimal TotalIncome { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal Balance => TotalIncome - TotalExpenses;
    public int TransactionCount { get; set; }
    public decimal AverageExpense { get; set; }

    public decimal UncategorizedExpenses { get; set; }

    public List<CategoryTotalNode> ExpensesByCategory { get; set; } = new();
    public List<MonthTotal> ByMonth { get; set; } = new();
    public List<MerchantTotal> TopMerchants { get; set; } = new();
    public List<Transaction> LargestExpenses { get; set; } = new();
}

public interface IDashboardService
{
    Task<DashboardViewModel> BuildAsync(DashboardFilter filter, DateTime today);
}

public class DashboardService : IDashboardService
{
    private readonly AppDbContext _db;

    public DashboardService(AppDbContext db) => _db = db;

    public async Task<DashboardViewModel> BuildAsync(DashboardFilter filter, DateTime today)
    {
        var (from, to) = filter.Resolve(today);

        var txQuery = _db.Transactions.AsNoTracking()
            .Where(t => t.TransactionDate >= from && t.TransactionDate <= to);

        var expenses = await txQuery.Where(t => t.Type == TransactionType.Expense).ToListAsync();
        var income = await txQuery.Where(t => t.Type == TransactionType.Income).ToListAsync();

        var vm = new DashboardViewModel
        {
            From = from,
            To = to,
            TotalExpenses = expenses.Sum(t => t.Amount),
            TotalIncome = income.Sum(t => t.Amount),
            TransactionCount = expenses.Count + income.Count,
            AverageExpense = expenses.Count > 0 ? Math.Round(expenses.Average(t => t.Amount), 2) : 0m,
            UncategorizedExpenses = expenses.Where(t => t.CategoryId == null).Sum(t => t.Amount)
        };

        vm.ExpensesByCategory = await BuildCategoryTreeAsync(expenses);

        vm.ByMonth = expenses.Concat(income)
            .GroupBy(t => new { t.TransactionDate.Year, t.TransactionDate.Month })
            .Select(g => new MonthTotal
            {
                Year = g.Key.Year,
                Month = g.Key.Month,
                Expenses = g.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount),
                Income = g.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount)
            })
            .OrderBy(m => m.Year).ThenBy(m => m.Month)
            .ToList();

        vm.TopMerchants = expenses
            .GroupBy(t => t.NormalizedDescription)
            .Select(g => new MerchantTotal { Merchant = g.Key, Amount = g.Sum(t => t.Amount), Count = g.Count() })
            .OrderByDescending(m => m.Amount)
            .Take(10)
            .ToList();

        vm.LargestExpenses = expenses
            .OrderByDescending(t => t.Amount)
            .Take(10)
            .ToList();

        return vm;
    }

    private async Task<List<CategoryTotalNode>> BuildCategoryTreeAsync(List<Transaction> expenses)
    {
        var categories = await _db.Categories.AsNoTracking().ToListAsync();

        var nodes = categories.ToDictionary(
            c => c.Id,
            c => new CategoryTotalNode { CategoryId = c.Id, Name = c.Name, Depth = c.Depth });

        // Direct sums per category.
        foreach (var g in expenses.Where(t => t.CategoryId != null).GroupBy(t => t.CategoryId!.Value))
            if (nodes.TryGetValue(g.Key, out var n))
                n.Direct = g.Sum(t => t.Amount);

        // Wire up children.
        var roots = new List<CategoryTotalNode>();
        foreach (var c in categories)
        {
            if (c.ParentId is int pid && nodes.TryGetValue(pid, out var parent))
                parent.Children.Add(nodes[c.Id]);
            else
                roots.Add(nodes[c.Id]);
        }

        // Roll up totals (depth <= 3, so cheap recursion) — spec §15.
        decimal Rollup(CategoryTotalNode n)
        {
            n.Total = n.Direct + n.Children.Sum(Rollup);
            return n.Total;
        }
        foreach (var r in roots) Rollup(r);

        // Only show roots that have any spending in the range, sorted by total.
        foreach (var r in roots)
            SortChildren(r);

        return roots
            .Where(r => r.Total > 0)
            .OrderByDescending(r => r.Total)
            .ToList();
    }

    private static void SortChildren(CategoryTotalNode n)
    {
        n.Children.RemoveAll(c => c.Total <= 0);
        n.Children.Sort((a, b) => b.Total.CompareTo(a.Total));
        foreach (var c in n.Children) SortChildren(c);
    }
}
