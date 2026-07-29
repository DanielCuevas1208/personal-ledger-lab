using System.Globalization;

namespace PersonalLedgerLab.Core;

/// <summary>
/// A spending or earning target for one category account in one month.
/// A specific budget targets a year and a month. A recurring budget uses
/// year 0 and month 0, and applies to any month that has no specific budget.
/// </summary>
public sealed class Budget
{
    public long Id { get; set; }
    public long CategoryAccountId { get; set; }
    public string? CategoryAccountCode { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Month { get; set; }
    public long LimitMinor { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public string? Note { get; set; }

    public Budget() { }

    public Budget(
        long id, long categoryAccountId, string? categoryAccountCode,
        string name, int year, int month, long limitMinor,
        string currencyCode, string? note = null)
    {
        Id = id;
        CategoryAccountId = categoryAccountId;
        CategoryAccountCode = categoryAccountCode;
        Name = name ?? string.Empty;
        Year = year;
        Month = month;
        LimitMinor = limitMinor;
        CurrencyCode = string.IsNullOrWhiteSpace(currencyCode) ? "USD" : currencyCode.ToUpperInvariant();
        Note = note;
    }

    public bool IsRecurring => Year == 0 && Month == 0;

    public override string ToString() =>
        $"{Name} {(IsRecurring ? "recurring" : SummaryService.MonthLabel(Year, Month))} " +
        $"{(LimitMinor / 100m):0.00} {CurrencyCode}";
}

/// <summary>
/// The variance between one budget and the actual month result.
/// VarianceMinor is ActualMinor minus LimitMinor.
/// </summary>
public sealed class BudgetVariance
{
    public int Year { get; init; }
    public int Month { get; init; }
    public long AccountId { get; init; }
    public string AccountCode { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;
    public AccountType AccountType { get; init; }
    public long LimitMinor { get; init; }
    public long ActualMinor { get; init; }
    public long VarianceMinor { get; init; }
    public bool IsRecurring { get; init; }
    public string CurrencyCode { get; init; } = "USD";
    public string Explanation { get; init; } = string.Empty;
}

/// <summary>
/// Builds a variance report from budgets and monthly summaries.
/// The service is deterministic and has no side effects.
/// </summary>
public static class BudgetService
{
    /// <summary>
    /// Compares each budget with the matching monthly total.
    /// A specific budget beats a recurring budget for the same month.
    /// </summary>
    public static IReadOnlyList<BudgetVariance> Build(
        IReadOnlyList<MonthlySummary> summaries,
        IReadOnlyList<Budget> budgets,
        IReadOnlyList<Account> accounts,
        string currency)
    {
        if (summaries is null) throw new ArgumentNullException(nameof(summaries));
        if (budgets is null) throw new ArgumentNullException(nameof(budgets));
        if (accounts is null) throw new ArgumentNullException(nameof(accounts));

        var accountsById = accounts.ToDictionary(a => a.Id);
        var recurring = new Dictionary<long, Budget>();
        var specific = new Dictionary<long, Dictionary<(int Year, int Month), Budget>>();

        foreach (var budget in budgets)
        {
            if (budget.IsRecurring)
                recurring[budget.CategoryAccountId] = budget;
            else
            {
                if (!specific.TryGetValue(budget.CategoryAccountId, out var byMonth))
                {
                    byMonth = new Dictionary<(int, int), Budget>();
                    specific[budget.CategoryAccountId] = byMonth;
                }
                byMonth[(budget.Year, budget.Month)] = budget;
            }
        }

        var accountIdsWithBudgets = new HashSet<long>(recurring.Keys);
        foreach (var key in specific.Keys)
            accountIdsWithBudgets.Add(key);

        var rows = new List<BudgetVariance>();
        foreach (var summary in summaries)
        {
            var totalsByAccount = summary.Items.ToDictionary(i => i.AccountId);

            foreach (var accountId in accountIdsWithBudgets.OrderBy(id => CodeFor(accountsById, id)))
            {
                Budget? chosen = null;
                if (specific.TryGetValue(accountId, out var byMonth) &&
                    byMonth.TryGetValue((summary.Year, summary.Month), out var spec))
                {
                    chosen = spec;
                }
                else if (recurring.TryGetValue(accountId, out var rec))
                {
                    chosen = rec;
                }

                if (chosen is null)
                    continue;

                totalsByAccount.TryGetValue(accountId, out var total);
                long actual = total?.NetMinor ?? 0;
                long variance = actual - chosen.LimitMinor;
                if (!accountsById.TryGetValue(accountId, out var account))
                    continue;

                rows.Add(new BudgetVariance
                {
                    Year = summary.Year,
                    Month = summary.Month,
                    AccountId = accountId,
                    AccountCode = account.Code,
                    AccountName = account.Name,
                    AccountType = account.Type,
                    LimitMinor = chosen.LimitMinor,
                    ActualMinor = actual,
                    VarianceMinor = variance,
                    IsRecurring = chosen.IsRecurring,
                    CurrencyCode = currency,
                    Explanation = Explain(account, chosen, actual, variance, currency, summary.Year, summary.Month)
                });
            }
        }

        return rows;
    }

    /// <summary>
    /// Sums every variance row into one signed total. Positive means the
    /// operator spent more than the limit, or earned less than the target.
    /// </summary>
    public static (long Limit, long Actual, long Variance) Totals(
        IReadOnlyList<BudgetVariance> rows)
    {
        long limit = 0, actual = 0, variance = 0;
        foreach (var row in rows)
        {
            limit += row.LimitMinor;
            actual += row.ActualMinor;
            variance += row.VarianceMinor;
        }
        return (limit, actual, variance);
    }

    public static string FormatMinor(long minor, string currency) =>
        (minor / 100m).ToString("0.00", CultureInfo.InvariantCulture) + " " + currency;

    private static string CodeFor(Dictionary<long, Account> accounts, long id) =>
        accounts.TryGetValue(id, out var a) ? a.Code : "9999";

    private static string Explain(
        Account account, Budget budget, long actual, long variance,
        string currency, int year, int month)
    {
        string period = budget.IsRecurring
            ? "recurring"
            : SummaryService.MonthLabel(year, month);
        string limitText = FormatMinor(budget.LimitMinor, currency);
        string actualText = FormatMinor(Math.Abs(actual), currency);
        string varianceText = FormatMinor(Math.Abs(variance), currency);

        if (account.Type == AccountType.Expense)
        {
            string direction = variance > 0
                ? $"{varianceText} over budget"
                : variance < 0
                    ? $"{varianceText} under budget"
                    : "exactly on budget";
            return
                $"Spent {actualText} against a {limitText} limit for {account.Code} {account.Name} " +
                $"in {period}. The result is {direction}.";
        }

        if (account.Type == AccountType.Income)
        {
            string direction = variance > 0
                ? $"{varianceText} above target"
                : variance < 0
                    ? $"{varianceText} below target"
                    : "exactly on target";
            return
                $"Earned {actualText} against a {limitText} target for {account.Code} {account.Name} " +
                $"in {period}. The result is {direction}.";
        }

        string sign = variance > 0 ? "over" : variance < 0 ? "under" : "on";
        return
            $"Actual {actualText} against a {limitText} budget for {account.Code} {account.Name} " +
            $"in {period}. The result is {varianceText} {sign} the budget.";
    }
}