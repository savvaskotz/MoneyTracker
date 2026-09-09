using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MoneyTracker.Data;
using MoneyTracker.Domain;
using MoneyTracker.Services.Categorization;
using MoneyTracker.Services.Import;

namespace MoneyTracker.Pages.Import;

public class PreviewModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IImportService _import;
    private readonly ICategoryService _categories;

    public PreviewModel(AppDbContext db, IImportService import, ICategoryService categories)
    {
        _db = db;
        _import = import;
        _categories = categories;
    }

    public ImportBatch Batch { get; private set; } = null!;
    public List<ImportStagingRow> Rows { get; private set; } = new();
    public List<CategoryOption> CategoryOptions { get; private set; } = new();
    public Dictionary<int, string> CategoryPaths { get; private set; } = new();

    [BindProperty]
    public Dictionary<long, int?> Selected { get; set; } = new();

    [BindProperty]
    public Dictionary<long, bool> Included { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (Batch.Status != ImportStatus.Pending)
            return RedirectToPage("/Import/Result", new { id });
        return Page();
    }

    public async Task<IActionResult> OnPostConfirmAsync(int id)
    {
        var batch = await _db.ImportBatches.FirstOrDefaultAsync(b => b.Id == id);
        if (batch == null) return NotFound();
        if (batch.Status != ImportStatus.Pending)
            return RedirectToPage("/Import/Result", new { id });

        await _import.ConfirmAsync(id, Selected, Included);
        return RedirectToPage("/Import/Result", new { id });
    }

    public async Task<IActionResult> OnPostCancelAsync(int id)
    {
        await _import.CancelAsync(id);
        return RedirectToPage("/Import/Upload");
    }

    private async Task<bool> LoadAsync(int id)
    {
        var batch = await _db.ImportBatches.FirstOrDefaultAsync(b => b.Id == id);
        if (batch == null) return false;
        Batch = batch;

        Rows = await _db.ImportStagingRows
            .Where(s => s.ImportBatchId == id)
            .OrderBy(s => s.RowIndex)
            .ToListAsync();

        CategoryOptions = await _categories.GetOptionsAsync();
        CategoryPaths = CategoryOptions.ToDictionary(o => o.Id, o => o.Path);
        return true;
    }
}
