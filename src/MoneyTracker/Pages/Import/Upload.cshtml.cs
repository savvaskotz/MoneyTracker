using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MoneyTracker.Services.Import;

namespace MoneyTracker.Pages.Import;

public class UploadModel : PageModel
{
    private readonly IImportService _import;
    private readonly IConfiguration _config;

    public UploadModel(IImportService import, IConfiguration config)
    {
        _import = import;
        _config = config;
    }

    [BindProperty]
    public IFormFile? File { get; set; }

    public List<string> Errors { get; } = new();

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (File == null || File.Length == 0)
        {
            Errors.Add("Επίλεξε ένα αρχείο Excel.");
            return Page();
        }

        var maxSize = _config.GetValue<long?>("Upload:MaxFileSizeBytes") ?? 5_242_880;
        if (File.Length > maxSize)
        {
            Errors.Add($"Το αρχείο είναι πολύ μεγάλο (όριο {maxSize / 1024 / 1024} MB).");
            return Page();
        }

        var ext = Path.GetExtension(File.FileName).ToLowerInvariant();
        if (ext != ".xlsx")
        {
            Errors.Add("Επιτρέπονται μόνο αρχεία .xlsx.");
            return Page();
        }

        await using var stream = File.OpenReadStream();
        var (batchId, errors) = await _import.BuildPreviewAsync(stream, Path.GetFileName(File.FileName));

        if (batchId == null)
        {
            Errors.AddRange(errors);
            return Page();
        }

        return RedirectToPage("/Import/Preview", new { id = batchId.Value });
    }
}
