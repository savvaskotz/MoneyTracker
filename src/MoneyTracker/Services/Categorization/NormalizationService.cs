using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MoneyTracker.Services.Categorization;

public interface INormalizationService
{
    /// <summary>Produces a stable, matching-friendly merchant key from a raw description.</summary>
    string Normalize(string rawDescription);
}

/// <summary>
/// Non-destructive description normalization (spec §10). The original text is always kept
/// on the transaction; this only produces the key used for matching/suggestions.
///
/// Piraeus descriptions look like:
///   "ΑΓΟΡΑ -SKLAVENITIS DRAMA      DRAMA          GR"
/// i.e. a prefix, then the merchant, padded with spaces, then city and country.
/// </summary>
public class NormalizationService : INormalizationService
{
    // Configurable prefixes stripped from the start of the description.
    private static readonly string[] Prefixes =
    {
        "ΑΓΟΡΑ - -", "ΑΓΟΡΑ -", "ΑΓΟΡΑ-", "ΑΓΟΡΑ ",
        "PURCHASE -", "PURCHASE-", "PURCHASE "
    };

    // Configurable legal-entity suffixes removed from the merchant token.
    private static readonly string[] Suffixes =
    {
        " AE", " ΑΕ", " A.E.", " Α.Ε.", " SA", " S.A.", " LTD", " LTD.", " EE", " ΕΕ", " IKE", " ΙΚΕ"
    };

    private static readonly Regex MultiSpace = new(@"\s{2,}", RegexOptions.Compiled);
    private static readonly Regex TrailingDigits = new(@"[\s\-_]*\d+\s*$", RegexOptions.Compiled);

    public string Normalize(string rawDescription)
    {
        if (string.IsNullOrWhiteSpace(rawDescription))
            return string.Empty;

        var text = rawDescription.Trim();

        // 1) Strip a known leading prefix.
        foreach (var p in Prefixes)
        {
            if (text.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            {
                text = text[p.Length..].TrimStart();
                break;
            }
        }

        // 2) The merchant is the first chunk before a run of 2+ spaces (which separates
        //    merchant from city/country in fixed-width statement layouts).
        var parts = MultiSpace.Split(text);
        var merchant = parts.Length > 0 ? parts[0] : text;

        // 3) Uppercase (invariant) and collapse remaining whitespace.
        merchant = merchant.ToUpperInvariant();
        merchant = Regex.Replace(merchant, @"\s+", " ").Trim();

        // 4) Remove trailing store/reference numbers ("GATIDIS 1234" -> "GATIDIS").
        merchant = TrailingDigits.Replace(merchant, string.Empty).Trim();

        // 5) Remove legal suffixes.
        foreach (var s in Suffixes)
        {
            if (merchant.EndsWith(s, StringComparison.OrdinalIgnoreCase))
            {
                merchant = merchant[..^s.Length].Trim();
                break;
            }
        }

        return merchant.Length == 0 ? rawDescription.Trim().ToUpperInvariant() : merchant;
    }
}

/// <summary>Small helper for normalizing arbitrary names (categories, etc.).</summary>
public static class TextNormalizer
{
    public static string Key(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var s = value.Trim().ToUpperInvariant();
        s = Regex.Replace(s, @"\s+", " ");
        return s;
    }
}
