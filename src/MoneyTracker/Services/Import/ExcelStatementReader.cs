using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using MoneyTracker.Domain;

namespace MoneyTracker.Services.Import;

public interface IExcelStatementReader
{
    StatementReadResult Read(Stream excelStream, ImportProfile profile);
}

/// <summary>
/// Reads an Excel bank statement into a common model using an <see cref="ImportProfile"/>.
/// It knows nothing bank-specific beyond what the profile describes (spec §6).
/// </summary>
public class ExcelStatementReader : IExcelStatementReader
{
    private static readonly Regex CardPattern = new(@"\d{4,6}\*{2,}\d{2,4}", RegexOptions.Compiled);

    public StatementReadResult Read(Stream excelStream, ImportProfile profile)
    {
        var result = new StatementReadResult();

        using var wb = new XLWorkbook(excelStream);
        var ws = ResolveWorksheet(wb, profile.SheetName);
        if (ws == null)
        {
            result.Errors.Add("Δεν βρέθηκε το φύλλο εργασίας.");
            return result;
        }

        // Card/account identifier from the preamble above the header row.
        result.AccountIdentifier = FindAccountIdentifier(ws, profile.HeaderRowIndex);

        // Map header text -> column number.
        var columns = MapColumns(ws, profile.HeaderRowIndex);

        int Col(string? header)
        {
            if (string.IsNullOrWhiteSpace(header)) return 0;
            return columns.TryGetValue(header.Trim().ToUpperInvariant(), out var c) ? c : 0;
        }

        int dateCol = Col(profile.DateColumn);
        int descCol = Col(profile.DescriptionColumn);
        int amountCol = Col(profile.AmountColumn);
        int debitCol = Col(profile.DebitColumn);
        int creditCol = Col(profile.CreditColumn);
        int catCol = Col(profile.CategoryColumn);
        int refCol = Col(profile.ReferenceColumn);
        int curCol = Col(profile.CurrencyColumn);

        if (dateCol == 0) result.Errors.Add($"Δεν βρέθηκε η στήλη ημερομηνίας «{profile.DateColumn}».");
        if (descCol == 0) result.Errors.Add($"Δεν βρέθηκε η στήλη περιγραφής «{profile.DescriptionColumn}».");
        if (amountCol == 0 && debitCol == 0 && creditCol == 0)
            result.Errors.Add("Δεν βρέθηκε στήλη ποσού (Amount ή Debit/Credit).");
        if (result.HasErrors) return result;

        var numberFormat = new NumberFormatInfo
        {
            NumberDecimalSeparator = profile.DecimalSeparator,
            NumberGroupSeparator = profile.GroupSeparator
        };

        int lastRow = ws.LastRowUsed()?.RowNumber() ?? profile.HeaderRowIndex;
        int rowIndex = 0;

        for (int r = profile.HeaderRowIndex + 1; r <= lastRow; r++)
        {
            var dateText = ws.Cell(r, dateCol).GetString().Trim();
            if (dateText.Length == 0) continue; // blank spacer row

            if (!TryParseDate(ws.Cell(r, dateCol), profile.DateFormat, out var date))
                continue; // footer / non-data row (e.g. "Ημερομηνία Ενημέρωσης:")

            decimal signed;
            if (amountCol != 0)
            {
                var amtText = ws.Cell(r, amountCol).GetString().Trim();
                if (!TryParseAmount(ws.Cell(r, amountCol), amtText, numberFormat, out signed))
                {
                    result.Errors.Add($"Γραμμή {r}: μη έγκυρο ποσό «{amtText}».");
                    continue;
                }
            }
            else
            {
                decimal debit = 0, credit = 0;
                if (debitCol != 0) TryParseAmount(ws.Cell(r, debitCol), ws.Cell(r, debitCol).GetString(), numberFormat, out debit);
                if (creditCol != 0) TryParseAmount(ws.Cell(r, creditCol), ws.Cell(r, creditCol).GetString(), numberFormat, out credit);
                // Debit is an outflow. Represent as positive-expense convention below.
                signed = credit - debit;
                if (profile.PositiveIsExpense) signed = -signed;
            }

            var isExpense = profile.PositiveIsExpense ? signed >= 0 : signed < 0;
            var type = isExpense ? TransactionType.Expense : TransactionType.Income;

            var desc = ws.Cell(r, descCol).GetString().Trim();
            string? bankCat = catCol != 0 ? NullIfEmpty(ws.Cell(r, catCol).GetString().Trim()) : null;
            string? reference = refCol != 0 ? NullIfEmpty(ws.Cell(r, refCol).GetString().Trim()) : null;
            string currency = curCol != 0
                ? NullIfEmpty(ws.Cell(r, curCol).GetString().Trim()) ?? profile.DefaultCurrency
                : profile.DefaultCurrency;

            result.Rows.Add(new StatementRow
            {
                RowIndex = ++rowIndex,
                TransactionDate = date,
                SignedAmount = signed,
                Amount = Math.Abs(signed),
                Type = type,
                OriginalDescription = desc,
                BankCategoryPath = bankCat,
                Reference = reference,
                CurrencyCode = currency
            });
        }

        if (result.Rows.Count == 0 && !result.HasErrors)
            result.Errors.Add("Δεν βρέθηκαν έγκυρες κινήσεις στο αρχείο.");

        return result;
    }

    private static IXLWorksheet? ResolveWorksheet(XLWorkbook wb, string? sheetName)
    {
        if (!string.IsNullOrWhiteSpace(sheetName))
        {
            var match = wb.Worksheets.FirstOrDefault(w =>
                string.Equals(w.Name.Trim(), sheetName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }
        return wb.Worksheets.FirstOrDefault();
    }

    private static string? FindAccountIdentifier(IXLWorksheet ws, int headerRowIndex)
    {
        for (int r = 1; r < headerRowIndex; r++)
        {
            foreach (var cell in ws.Row(r).CellsUsed())
            {
                var text = cell.GetString();
                var m = CardPattern.Match(text);
                if (m.Success) return m.Value;
            }
        }
        return null;
    }

    private static Dictionary<string, int> MapColumns(IXLWorksheet ws, int headerRowIndex)
    {
        var map = new Dictionary<string, int>();
        foreach (var cell in ws.Row(headerRowIndex).CellsUsed())
        {
            var key = cell.GetString().Trim().ToUpperInvariant();
            if (key.Length > 0 && !map.ContainsKey(key))
                map[key] = cell.Address.ColumnNumber;
        }
        return map;
    }

    private static bool TryParseDate(IXLCell cell, string format, out DateTime date)
    {
        var v = cell.Value;
        if (v.IsDateTime)
        {
            date = v.GetDateTime().Date;
            return true;
        }
        var s = cell.GetString().Trim();
        if (DateTime.TryParseExact(s, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            date = date.Date;
            return true;
        }
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static bool TryParseAmount(IXLCell cell, string text, NumberFormatInfo nfi, out decimal value)
    {
        var v = cell.Value;
        if (v.IsNumber)
        {
            value = (decimal)v.GetNumber();
            return true;
        }
        text = (text ?? string.Empty).Trim().Replace("€", "").Replace("EUR", "").Trim();
        return decimal.TryParse(text, NumberStyles.Number | NumberStyles.AllowLeadingSign, nfi, out value);
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
