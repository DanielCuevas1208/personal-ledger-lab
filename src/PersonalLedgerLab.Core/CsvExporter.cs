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

    /// <summary>
    /// Writes the budget table.
    /// </summary>
    public static void ExportBudgets(IReadOnlyList<Budget> budgets, IReadOnlyList<Account> accounts, TextWriter writer)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));
        var accountsById = accounts.ToDictionary(a => a.Id);
        writer.WriteLine("Id,Year,Month,Name,AccountCode,AccountName,AccountType,LimitMinor,LimitMajor,Currency,Recurring,Note");

        foreach (var budget in budgets.OrderBy(b => b.Year).ThenBy(b => b.Month).ThenBy(b => b.CategoryAccountCode))
        {
            accountsById.TryGetValue(budget.CategoryAccountId, out var account);
            writer.WriteLine(string.Join(',',
                budget.Id,
                budget.Year,
                budget.Month,
                Csv(budget.Name),
                Csv(account?.Code ?? budget.CategoryAccountCode ?? string.Empty),
                Csv(account?.Name ?? string.Empty),
                account?.Type.ToString() ?? string.Empty,
                budget.LimitMinor,
                (budget.LimitMinor / 100m).ToString("0.00", CultureInfo.InvariantCulture),
                Csv(budget.CurrencyCode),
                budget.IsRecurring,
                Csv(budget.Note ?? string.Empty)));
        }
    }

    /// <summary>
    /// Writes one variance row per budget that matched a month.
    /// </summary>
    public static void ExportBudgetVariance(IReadOnlyList<BudgetVariance> rows, TextWriter writer)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));
        writer.WriteLine("Year,Month,AccountCode,AccountName,AccountType,LimitMinor,ActualMinor,VarianceMinor,VarianceMajor,Currency,Recurring,Explanation");

        foreach (var row in rows.OrderBy(r => r.Year).ThenBy(r => r.Month).ThenBy(r => r.AccountCode))
        {
            writer.WriteLine(string.Join(',',
                row.Year,
                row.Month,
                Csv(row.AccountCode),
                Csv(row.AccountName),
                row.AccountType,
                row.LimitMinor,
                row.ActualMinor,
                row.VarianceMinor,
                (row.VarianceMinor / 100m).ToString("0.00", CultureInfo.InvariantCulture),
                Csv(row.CurrencyCode),
                row.IsRecurring,
                Csv(row.Explanation)));
        }
    }

    /// <summary>
    /// Writes consolidated balances in the ledger base currency.
    /// </summary>
    public static void ExportConsolidatedBalances(ConsolidatedBalanceReport report, TextWriter writer)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));
        writer.WriteLine("AsOfDate,AccountCode,AccountName,AccountType,NativeBalanceMinor,NativeCurrency,BaseBalanceMinor,BaseCurrency,ExchangeRatePpm,Explanation");

        foreach (var row in report.Accounts)
        {
            writer.WriteLine(string.Join(',',
                report.AsOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Csv(row.AccountCode),
                Csv(row.AccountName),
                row.AccountType,
                row.NativeBalanceMinor,
                Csv(row.NativeCurrencyCode),
                row.BaseBalanceMinor,
                Csv(row.BaseCurrencyCode),
                row.ExchangeRatePpm?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                Csv(row.Explanation)));
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