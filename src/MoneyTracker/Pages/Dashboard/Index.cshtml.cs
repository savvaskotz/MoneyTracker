using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MoneyTracker.Services.Dashboard;

namespace MoneyTracker.Pages.Dashboard;

public class IndexModel : PageModel
{
    private readonly IDashboardService _dashboard;

    public IndexModel(IDashboardService dashboard) => _dashboard = dashboard;

    [BindProperty(SupportsGet = true)]
    public DateRangePreset Preset { get; set; } = DateRangePreset.ThisMonth;
    [BindProperty(SupportsGet = true)]
    public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)]
    public DateTime? To { get; set; }

    public DashboardViewModel Data { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var filter = new DashboardFilter { Preset = Preset, From = From, To = To };
        Data = await _dashboard.BuildAsync(filter, DateTime.Today);
    }
}
