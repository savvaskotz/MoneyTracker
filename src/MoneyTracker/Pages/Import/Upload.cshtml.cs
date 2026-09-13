using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MoneyTracker.Data;
using MoneyTracker.Domain;
using MoneyTracker.Services.Import;

namespace MoneyTracker.Pages.Import;

public class UploadModel : PageModel
{
    private readonly IImportService _import;
    private readonly IConfiguration _config;
    private readonly AppDbContext _db;

    public UploadModel(IImportService import, IConfiguration config, AppDbContext db)
    {
        _import = import;
        _config = config;
        _db = db;
    }

    [BindProperty]
    public IFormFile? UploadFile { get; set; }

    [BindProperty]
    public int? AccountId { get; set; }

    [BindProperty]
    public string? NewAccountName { get; set; }

    public List<Account> Accounts { get; private set; } = new();

    public List<string> Errors { get; } = new();

    public async Task OnGetAsync() => await LoadAccountsAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadAccountsAsync();

        if (UploadFile == null || UploadFile.Length == 0)
        {
            Errors.Add("Επίλεξε ένα αρχείο Excel.");
            return Page();
        }

        var maxSize = _config.GetValue<long?>("Upload:MaxFileSizeBytes") ?? 5_242_880;
        if (UploadFile.Length > maxSize)
        {
            Errors.Add($"Το αρχείο είναι πολύ μεγάλο (όριο {maxSize / 1024 / 1024} MB).");
            return Page();
        }

        var ext = Path.GetExtension(UploadFile.FileName).ToLowerInvariant();
        if (ext != ".xlsx")
        {
            Errors.Add("Επιτρέπονται μόνο αρχεία .xlsx.");
            return Page();
        }

        // Determine the target account: a new one (if named) or the chosen existing one.
        int accountId;
        if (!string.IsNullOrWhiteSpace(NewAccountName))
        {
            var name = NewAccountName.Trim();
            var existing = await _db.Accounts.FirstOrDefaultAsync(a => a.Name == name);
            if (existing != null)
            {
                accountId = existing.Id;
            }
            else
            {
                var account = new Account
                {
                    Name = name,
                    Identifier = "ACC-" + Guid.NewGuid().ToString("N")[..8],
                    CreatedAt = DateTime.UtcNow
                };
                _db.Accounts.Add(account);
                await _db.SaveChangesAsync();
                accountId = account.Id;
                await LoadAccountsAsync();
            }
        }
        else if (AccountId is int a)
        {
            accountId = a;
        }
        else
        {
            Errors.Add("Διάλεξε λογαριασμό (ή δώσε όνομα για νέο).");
            return Page();
        }

        await using var stream = UploadFile.OpenReadStream();
        var (batchId, errors) = await _import.BuildPreviewAsync(stream, Path.GetFileName(UploadFile.FileName), accountId);

        if (batchId == null)
        {
            Errors.AddRange(errors);
            return Page();
        }

        return RedirectToPage("/Import/Preview", new { id = batchId.Value });
    }

    private async Task LoadAccountsAsync() =>
        Accounts = await _db.Accounts.AsNoTracking().OrderBy(a => a.Name).ToListAsync();
}
