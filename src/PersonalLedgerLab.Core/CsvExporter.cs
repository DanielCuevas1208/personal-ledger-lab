using System.Globalization;
using System.Text;

namespace PersonalLedgerLab.Core;

/// <summary>
/// Writes ledger data to CSV files for backup and offline review.
/// </summary>
public static class CsvExporter
{
    /// <summary>
    /// Writes the full ledger as one row per posting.
    /// </summary>
    public static void ExportLedger(
        IReadOnlyList<Transaction> transactions,
        IReadOnlyList<Account> accounts,
        TextWriter writer)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));
        var accountsById = accounts.ToDictionary(a => a.Id);

        writer.WriteLine("TransactionId,Date,Description,Reference,Source,AccountId,AccountCode,AccountName,AccountType,DebitMinor,CreditMinor,Currency,RuleId,RuleExplanation");

        foreach (var tx in transactions.OrderBy(t => t.Date).ThenBy(t => t.Id))
        {
            foreach (var p in tx.Postings)
            {
                accountsById.TryGetValue(p.AccountId, out var account);
                long debit = p.AmountMinor > 0 ? p.AmountMinor : 0;
                long credit = p.AmountMinor < 0 ? -p.AmountMinor : 0;
                writer.WriteLine(string.Join(',',
                    tx.Id,
                    tx.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Csv(tx.Description),
                    Csv(tx.Reference ?? string.Empty),
                    Csv(tx.Source),
                    p.AccountId,
                    Csv(account?.Code ?? string.Empty),
                    Csv(account?.Name ?? string.Empty),
                    account?.Type.ToString() ?? string.Empty,
                    debit,
                    credit,
                    Csv(p.CurrencyCode),
                    tx.AppliedRuleId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    Csv(tx.RuleExplanation ?? string.Empty)));
            }
        }
    }

    /// <summary>
    /// Writes a flat monthly summary suitable for spreadsheet review.
    /// </summary>
    public static void ExportSummary(IReadOnlyList<MonthlySummary> summaries, TextWriter writer)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));
        writer.WriteLine("Year,Month,AccountCode,AccountName,AccountType,DebitMinor,CreditMinor,NetMinor,NetMajor,Currency");

        foreach (var summary in summaries)
        {
            foreach (var item in summary.Items)
            {
                writer.WriteLine(string.Join(',',
                    summary.Year,
                    summary.Month,
                    Csv(item.AccountCode),
                    Csv(item.AccountName),
                    item.AccountType,
                    item.DebitMinor,
                    item.CreditMinor,
                    item.NetMinor,
                    (item.NetMinor / 100m).ToString("0.00", CultureInfo.InvariantCulture),
                    Csv(item.CurrencyCode)));
            }
        }
    }

    /// <summary>
    /// Writes the chart of accounts.
    /// </summary>
    public static void ExportAccounts(IReadOnlyList<Account> accounts, TextWriter writer)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));
        writer.WriteLine("Id,Code,Name,Type,CurrencyCode,Note");
        foreach (var account in accounts.OrderBy(a => a.Code))
        {
            writer.WriteLine(string.Join(',',
                account.Id,
                Csv(account.Code),
                Csv(account.Name),
                account.Type,
                Csv(account.CurrencyCode),
                Csv(account.Note ?? string.Empty)));
        }
    }

    /// <summary>
    /// Writes categorization rules.
    /// </summary>
    public static void ExportRules(IReadOnlyList<CategoryRule> rules, TextWriter writer)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));
        writer.WriteLine("Id,Name,Priority,MatchType,MatchValue,CategoryAccountId,CategoryAccountCode");
        foreach (var rule in rules.OrderBy(r => r.Priority).ThenBy(r => r.Id))
        {
            writer.WriteLine(string.Join(',',
                rule.Id,
                Csv(rule.Name),
                rule.Priority,
                rule.MatchType,
                Csv(rule.MatchValue),
                rule.CategoryAccountId,
                Csv(rule.CategoryAccountCode ?? string.Empty)));
        }
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        bool needsQuoting = value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
        if (!needsQuoting)
            return value;
        var builder = new StringBuilder();
        builder.Append('"');
        foreach (char c in value)
        {
            if (c == '"') builder.Append('"');
            builder.Append(c);
        }
        builder.Append('"');
        return builder.ToString();
    }
}