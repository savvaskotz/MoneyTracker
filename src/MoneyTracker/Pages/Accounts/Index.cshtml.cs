using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MoneyTracker.Data;
using MoneyTracker.Domain;

namespace MoneyTracker.Pages.Accounts;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;

    public IndexModel(AppDbContext db) => _db = db;

    public record AccountRow(int Id, string Name, int TransactionCount);

    public List<AccountRow> Accounts { get; private set; } = new();

    [BindProperty]
    public string? NewName { get; set; }

    [BindProperty]
    public int RenameId { get; set; }
    [BindProperty]
    public string? RenameName { get; set; }

    public string? Error { get; set; }
    public string? Message { get; set; }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostCreateAsync()
    {
        var name = (NewName ?? string.Empty).Trim();
        if (name.Length == 0)
            Error = "Δώσε όνομα λογαριασμού.";
        else if (await _db.Accounts.AnyAsync(a => a.Name == name))
            Error = "Υπάρχει ήδη λογαριασμός με αυτό το όνομα.";
        else
        {
            _db.Accounts.Add(new MoneyTracker.Domain.Account
            {
                Name = name,
                Identifier = "ACC-" + Guid.NewGuid().ToString("N")[..8],
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
            Message = $"Δημιουργήθηκε ο λογαριασμός «{name}».";
        }
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostRenameAsync()
    {
        var name = (RenameName ?? string.Empty).Trim();
        var acc = await _db.Accounts.FindAsync(RenameId);
        if (acc == null)
            Error = "Ο λογαριασμός δεν βρέθηκε.";
        else if (name.Length == 0)
            Error = "Δώσε όνομα.";
        else if (await _db.Accounts.AnyAsync(a => a.Name == name && a.Id != RenameId))
            Error = "Υπάρχει ήδη λογαριασμός με αυτό το όνομα.";
        else
        {
            acc.Name = name;
            await _db.SaveChangesAsync();
            Message = "Ο λογαριασμός μετονομάστηκε.";
        }
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var acc = await _db.Accounts.FindAsync(id);
        if (acc == null)
            Error = "Ο λογαριασμός δεν βρέθηκε.";
        else if (await _db.Transactions.AnyAsync(t => t.AccountId == id))
            Error = "Δεν μπορεί να διαγραφεί λογαριασμός που έχει κινήσεις.";
        else
        {
            _db.Accounts.Remove(acc);
            await _db.SaveChangesAsync();
            Message = "Ο λογαριασμός διαγράφηκε.";
        }
        await LoadAsync();
        return Page();
    }

    private async Task LoadAsync()
    {
        Accounts = await _db.Accounts.AsNoTracking()
            .OrderBy(a => a.Name)
            .Select(a => new AccountRow(a.Id, a.Name, a.Transactions.Count))
            .ToListAsync();
    }
}
