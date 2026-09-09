using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MoneyTracker.Services.Duplicates;

public interface IFingerprintService
{
    /// <summary>
    /// Deterministic SHA-256 fingerprint for duplicate detection (spec §7).
    /// The occurrence ordinal keeps genuinely-identical same-day transactions distinct
    /// while making a re-import of the same file reproduce the same fingerprints.
    /// </summary>
    string Compute(int accountId, DateTime date, decimal signedAmount,
        string normalizedDescription, string? reference, int occurrenceOrdinal);
}

public class FingerprintService : IFingerprintService
{
    public string Compute(int accountId, DateTime date, decimal signedAmount,
        string normalizedDescription, string? reference, int occurrenceOrdinal)
    {
        var canonical = string.Join("|",
            accountId.ToString(CultureInfo.InvariantCulture),
            date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            signedAmount.ToString("0.00", CultureInfo.InvariantCulture),
            (normalizedDescription ?? string.Empty).Trim().ToUpperInvariant(),
            (reference ?? string.Empty).Trim(),
            occurrenceOrdinal.ToString(CultureInfo.InvariantCulture));

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes).ToLowerInvariant(); // 64 hex chars
    }
}
