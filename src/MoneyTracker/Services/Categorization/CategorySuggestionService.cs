using Microsoft.EntityFrameworkCore;
using MoneyTracker.Data;
using MoneyTracker.Domain;

namespace MoneyTracker.Services.Categorization;

/// <summary>Caches used across a whole import so suggestion doesn't re-query per row.</summary>
public class SuggestionContext
{
    public List<CategoryRule> UserRules { get; init; } = new();
    public List<CategoryRule> LearnedRules { get; init; } = new();
    /// <summary>NormalizedDescription -> most frequently used CategoryId in history.</summary>
    public Dictionary<string, int> History { get; init; } = new();
}

public interface ICategorySuggestionService
{
    Task<SuggestionContext> CreateContextAsync();

    (int? CategoryId, SuggestionSource Source) Suggest(
        SuggestionContext ctx, string normalizedDescription, int? bankCategoryId);
}

/// <summary>
/// Suggests a category following the priority order in spec §12:
/// user rule → history → learned rule → bank category → uncategorized.
/// </summary>
public class CategorySuggestionService : ICategorySuggestionService
{
    private readonly AppDbContext _db;

    public CategorySuggestionService(AppDbContext db) => _db = db;

    public async Task<SuggestionContext> CreateContextAsync()
    {
        var rules = await _db.CategoryRules.AsNoTracking().ToListAsync();

        // Build history: for each normalized description, the most frequently chosen category.
        var history = await _db.Transactions.AsNoTracking()
            .Where(t => t.CategoryId != null)
            .GroupBy(t => new { t.NormalizedDescription, t.CategoryId })
            .Select(g => new { g.Key.NormalizedDescription, CategoryId = g.Key.CategoryId!.Value, Count = g.Count() })
            .ToListAsync();

        var historyMap = history
            .GroupBy(x => x.NormalizedDescription)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.Count).ThenByDescending(x => x.CategoryId).First().CategoryId);

        return new SuggestionContext
        {
            UserRules = rules.Where(r => r.Source == RuleSource.UserDefined).ToList(),
            LearnedRules = rules.Where(r => r.Source == RuleSource.Learned).ToList(),
            History = historyMap
        };
    }

    public (int? CategoryId, SuggestionSource Source) Suggest(
        SuggestionContext ctx, string normalizedDescription, int? bankCategoryId)
    {
        var n = (normalizedDescription ?? string.Empty).Trim().ToUpperInvariant();

        // 1) Strong, user-defined rules.
        var user = MatchRule(ctx.UserRules, n);
        if (user != null) return (user.CategoryId, SuggestionSource.UserRule);

        // 2) History of previously categorized transactions.
        if (n.Length > 0 && ctx.History.TryGetValue(n, out var histCat))
            return (histCat, SuggestionSource.History);

        // 3) Learned rules (from earlier corrections).
        var learned = MatchRule(ctx.LearnedRules, n);
        if (learned != null) return (learned.CategoryId, SuggestionSource.LearnedRule);

        // 4) Bank-provided category (strong for the very first import).
        if (bankCategoryId is int bid) return (bid, SuggestionSource.BankCategory);

        // 5) Uncategorized — do not guess (spec §12).
        return (null, SuggestionSource.None);
    }

    private static CategoryRule? MatchRule(List<CategoryRule> rules, string n)
    {
        if (rules.Count == 0 || n.Length == 0) return null;

        // Evaluate strongest match types first; break ties by Priority then HitCount.
        foreach (var type in new[] { MatchType.Exact, MatchType.StartsWith, MatchType.Contains })
        {
            var match = rules
                .Where(r => r.MatchType == type && Matches(type, n, r.Pattern))
                .OrderByDescending(r => r.Priority)
                .ThenByDescending(r => r.HitCount)
                .FirstOrDefault();
            if (match != null) return match;
        }
        return null;
    }

    private static bool Matches(MatchType type, string n, string pattern)
    {
        pattern = (pattern ?? string.Empty).Trim().ToUpperInvariant();
        if (pattern.Length == 0) return false;
        return type switch
        {
            MatchType.Exact => n == pattern,
            MatchType.StartsWith => n.StartsWith(pattern, StringComparison.Ordinal),
            MatchType.Contains => n.Contains(pattern, StringComparison.Ordinal),
            _ => false
        };
    }
}

public interface ICategoryLearningService
{
    /// <summary>Upsert a learned Exact rule mapping a normalized description to a category (spec §11).</summary>
    Task LearnAsync(string normalizedDescription, int categoryId);
}

public class CategoryLearningService : ICategoryLearningService
{
    private readonly AppDbContext _db;

    public CategoryLearningService(AppDbContext db) => _db = db;

    public async Task LearnAsync(string normalizedDescription, int categoryId)
    {
        var pattern = (normalizedDescription ?? string.Empty).Trim().ToUpperInvariant();
        if (pattern.Length == 0) return;

        var rule = await _db.CategoryRules
            .FirstOrDefaultAsync(r => r.Pattern == pattern && r.MatchType == MatchType.Exact);

        var now = DateTime.UtcNow;
        if (rule == null)
        {
            _db.CategoryRules.Add(new CategoryRule
            {
                Pattern = pattern,
                MatchType = MatchType.Exact,
                CategoryId = categoryId,
                Priority = 100,
                Source = RuleSource.Learned,
                HitCount = 1,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        else
        {
            rule.CategoryId = categoryId;
            rule.HitCount += 1;
            rule.UpdatedAt = now;
            if (rule.Source == RuleSource.Learned) rule.Priority = 100;
        }
        // Saved by the caller within the confirm transaction.
    }
}
