using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MoneyTracker.Data;
using MoneyTracker.Domain;
using MoneyTracker.Services.Dashboard;

namespace MoneyTracker.Pages.Dashboard;

public class IndexModel : PageModel
{
    private readonly IDashboardService _dashboard;
    private readonly AppDbContext _db;

    public IndexModel(IDashboardService dashboard, AppDbContext db)
    {
        _dashboard = dashboard;
        _db = db;
    }

    [BindProperty(SupportsGet = true)]
    public DateRangePreset Preset { get; set; } = DateRangePreset.ThisMonth;
    [BindProperty(SupportsGet = true)]
    public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)]
    public DateTime? To { get; set; }
    [BindProperty(SupportsGet = true)]
    public int? AccountId { get; set; }

    public DashboardViewModel Data { get; private set; } = new();
    public List<Account> Accounts { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Accounts = await _db.Accounts.AsNoTracking().OrderBy(a => a.Name).ToListAsync();
        var filter = new DashboardFilter { Preset = Preset, From = From, To = To, AccountId = AccountId };
        Data = await _dashboard.BuildAsync(filter, DateTime.Today);
    }
}
