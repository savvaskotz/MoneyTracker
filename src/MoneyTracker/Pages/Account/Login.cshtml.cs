using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MoneyTracker.Pages.Account;

[AllowAnonymous]
public class LoginModel : PageModel
{
    private readonly IConfiguration _config;

    public LoginModel(IConfiguration config) => _config = config;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? Error { get; set; }

    public class InputModel
    {
        [Required(ErrorMessage = "Το όνομα χρήστη είναι υποχρεωτικό.")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Ο κωδικός είναι υποχρεωτικός.")]
        public string Password { get; set; } = string.Empty;
    }

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToPage("/Dashboard/Index");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        var expectedUser = _config["Auth:Username"] ?? "admin";
        var expectedPass = _config["Auth:Password"] ?? string.Empty;

        var userOk = string.Equals(Input.Username.Trim(), expectedUser, StringComparison.Ordinal);
        // Constant-time-ish compare to avoid trivial timing leaks.
        var passOk = CryptographicEquals(Input.Password, expectedPass);

        if (!userOk || !passOk)
        {
            Error = "Λάθος όνομα χρήστη ή κωδικός.";
            return Page();
        }

        var claims = new List<Claim> { new(ClaimTypes.Name, expectedUser) };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });

        if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
            return LocalRedirect(ReturnUrl);
        return RedirectToPage("/Dashboard/Index");
    }

    private static bool CryptographicEquals(string a, string b)
    {
        var ba = System.Text.Encoding.UTF8.GetBytes(a);
        var bb = System.Text.Encoding.UTF8.GetBytes(b);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Security.Cryptography.SHA256.HashData(ba),
            System.Security.Cryptography.SHA256.HashData(bb));
    }
}
