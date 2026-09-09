using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MoneyTracker.Data;
using MoneyTracker.Domain;

namespace MoneyTracker.Pages.Import;

public class ResultModel : PageModel
{
    private readonly AppDbContext _db;

    public ResultModel(AppDbContext db) => _db = db;

    public ImportBatch Batch { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var batch = await _db.ImportBatches.FirstOrDefaultAsync(b => b.Id == id);
        if (batch == null) return NotFound();
        Batch = batch;
        return Page();
    }
}
