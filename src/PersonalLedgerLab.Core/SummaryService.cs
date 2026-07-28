namespace PersonalLedgerLab.Core;

/// <summary>
/// A monthly total for one account.
/// </summary>
public sealed class MonthlyAccountTotal
{
    public int Year { get; init; }
    public int Month { get; init; }
    public long AccountId { get; init; }
    public string AccountCode { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;
    public AccountType AccountType { get; init; }
    public long DebitMinor { get; init; }
    public long CreditMinor { get; init; }
    public long NetMinor { get; init; }
    public string CurrencyCode { get; init; } = "USD";
}

/// <summary>
/// A monthly summary for the whole ledger.
/// </summary>
public sealed class MonthlySummary
{
    public int Year { get; init; }
    public int Month { get; init; }
    public IReadOnlyList<MonthlyAccountTotal> Items { get; init; } = Array.Empty<MonthlyAccountTotal>();
    public long IncomeMinor { get; init; }
    public long ExpenseMinor { get; init; }
    public long NetIncomeMinor { get; init; }
    public string CurrencyCode { get; init; } = "USD";
}

/// <summary>
/// Produces monthly summaries by grouping all postings.
/// </summary>
public static class SummaryService
{
    public static IReadOnlyList<MonthlySummary> Build(
        IReadOnlyList<Transaction> transactions,
        IReadOnlyList<Account> accounts,
        string currency)
    {
        if (transactions is null) throw new ArgumentNullException(nameof(transactions));
        if (accounts is null) throw new ArgumentNullException(nameof(accounts));

        var accountsById = new Dictionary<long, Account>();
        foreach (var a in accounts)
            accountsById[a.Id] = a;

        var accountMonthGroups = transactions
            .SelectMany(t => t.Postings, (t, p) => (t.Date, p))
            .GroupBy(x => new MonthAccountKey(x.Date.Year, x.Date.Month, x.p.AccountId), x => x.p)
            .ToList();

        var byMonth = accountMonthGroups
            .GroupBy(g => new MonthKey(g.Key.Year, g.Key.Month))
            .OrderBy(m => m.Key.Year).ThenBy(m => m.Key.Month);

        var summaries = new List<MonthlySummary>();
        foreach (var month in byMonth)
        {
            var items = new List<MonthlyAccountTotal>();
            long income = 0;
            long expense = 0;

            var orderedGroups = month
                .OrderBy(g => accountsById.TryGetValue(g.Key.AccountId, out var a) ? a.Code : "9999");

            foreach (var acctGroup in orderedGroups)
            {
                if (!accountsById.TryGetValue(acctGroup.Key.AccountId, out var account))
                    continue;

                long debit = 0;
                long credit = 0;
                foreach (var posting in acctGroup)
                {
                    if (posting.AmountMinor > 0) debit += posting.AmountMinor;
                    else credit -= posting.AmountMinor;
                }

                long net = account.IsDebitNormal ? debit - credit : credit - debit;

                items.Add(new MonthlyAccountTotal
                {
                    Year = month.Key.Year,
                    Month = month.Key.Month,
                    AccountId = account.Id,
                    AccountCode = account.Code,
                    AccountName = account.Name,
                    AccountType = account.Type,
                    DebitMinor = debit,
                    CreditMinor = credit,
                    NetMinor = net,
                    CurrencyCode = currency
                });

                if (account.Type == AccountType.Income) income += net;
                if (account.Type == AccountType.Expense) expense += net;
            }

            summaries.Add(new MonthlySummary
            {
                Year = month.Key.Year,
                Month = month.Key.Month,
                Items = items,
                IncomeMinor = income,
                ExpenseMinor = expense,
                NetIncomeMinor = income - expense,
                CurrencyCode = currency
            });
        }

        return summaries;
    }

    public static string FormatAmount(long minorUnits, string currency) =>
        (minorUnits / 100m).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " " + currency;

    public static string MonthLabel(int year, int month) =>
        $"{year:D4}-{month:D2}";

    private readonly record struct MonthKey(int Year, int Month);
    private readonly record struct MonthAccountKey(int Year, int Month, long AccountId);
}