using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MoneyTracker.Domain;
using MoneyTracker.Services.Categorization;

namespace MoneyTracker.Pages.Categories;

public class IndexModel : PageModel
{
    private readonly ICategoryService _categories;

    public IndexModel(ICategoryService categories) => _categories = categories;

    public List<CategoryOption> Options { get; private set; } = new();

    [BindProperty]
    public string? NewName { get; set; }
    [BindProperty]
    public int? NewParentId { get; set; }

    [BindProperty]
    public int MoveId { get; set; }
    [BindProperty]
    public int? MoveParentId { get; set; }

    [BindProperty]
    public int DeleteId { get; set; }

    public string? Error { get; set; }
    public string? Message { get; set; }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostCreateAsync()
    {
        try
        {
            var cat = await _categories.CreateAsync(NewName ?? string.Empty, NewParentId);
            Message = $"Δημιουργήθηκε η κατηγορία «{cat.Name}».";
        }
        catch (CategoryValidationException ex) { Error = ex.Message; }
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostMoveAsync()
    {
        try
        {
            await _categories.MoveAsync(MoveId, MoveParentId);
            Message = "Η κατηγορία μετακινήθηκε.";
        }
        catch (CategoryValidationException ex) { Error = ex.Message; }
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        try
        {
            await _categories.DeleteAsync(DeleteId);
            Message = "Η κατηγορία διαγράφηκε.";
        }
        catch (CategoryValidationException ex) { Error = ex.Message; }
        await LoadAsync();
        return Page();
    }

    private async Task LoadAsync() => Options = await _categories.GetOptionsAsync();
}
