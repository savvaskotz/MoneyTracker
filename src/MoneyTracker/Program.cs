using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MoneyTracker.Data;
using MoneyTracker.Services.Categorization;
using MoneyTracker.Services.Dashboard;
using MoneyTracker.Services.Duplicates;
using MoneyTracker.Services.Import;

var builder = WebApplication.CreateBuilder(args);

// ---- Database ----
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

// ---- Authentication (single hard-coded user, credentials from configuration) ----
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/Login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
    });

// Everything requires an authenticated user by default (spec §18).
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AllowAnonymousToPage("/Account/Login");
    options.Conventions.AllowAnonymousToPage("/Error");
});

// Limit upload size (spec §18).
var maxUpload = builder.Configuration.GetValue<long?>("Upload:MaxFileSizeBytes") ?? 5_242_880;
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = maxUpload;
});

// ---- Application services ----
builder.Services.AddScoped<INormalizationService, NormalizationService>();
builder.Services.AddScoped<IFingerprintService, FingerprintService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<ICategorySuggestionService, CategorySuggestionService>();
builder.Services.AddScoped<ICategoryLearningService, CategoryLearningService>();
builder.Services.AddScoped<IExcelStatementReader, ExcelStatementReader>();
builder.Services.AddScoped<IImportService, ImportService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();

var app = builder.Build();

// ---- Create/seed database ----
// v1 uses EnsureCreated() so the app runs without EF tooling. To move to versioned
// migrations, see docs/DESIGN.md §4.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    DbSeeder.Seed(db);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

app.Run();
