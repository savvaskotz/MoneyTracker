using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MoneyTracker.Pages;

public class IndexModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Dashboard/Index");
}
