namespace PersonalLedgerLab.Core;

/// <summary>
/// One account balance in native currency and in the ledger base currency.
/// </summary>
public sealed class ConsolidatedAccountBalance
{
    public long AccountId { get; init; }
    public string AccountCode { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;
    public AccountType AccountType { get; init; }
    public long NativeBalanceMinor { get; init; }
    public string NativeCurrencyCode { get; init; } = "USD";
    public long BaseBalanceMinor { get; init; }
    public string BaseCurrencyCode { get; init; } = "USD";
    public long? ExchangeRatePpm { get; init; }
    public string Explanation { get; init; } = string.Empty;
}

/// <summary>
/// A consolidated net-worth snapshot in the ledger base currency.
/// </summary>
public sealed class ConsolidatedBalanceReport
{
    public DateOnly AsOfDate { get; init; }
    public string BaseCurrencyCode { get; init; } = "USD";
    public IReadOnlyList<ConsolidatedAccountBalance> Accounts { get; init; } = Array.Empty<ConsolidatedAccountBalance>();
    public long AssetTotalMinor { get; init; }
    public long LiabilityTotalMinor { get; init; }
    public long NetWorthMinor { get; init; }
}

/// <summary>
/// Converts native account balances into one base currency using stored exchange rates.
/// </summary>
public sealed class ConsolidatedBalanceService
{
    private readonly ExchangeRateService _rates = new();

    public ConsolidatedBalanceReport Build(
        IReadOnlyList<Transaction> transactions,
        IReadOnlyList<Account> accounts,
        IReadOnlyList<ExchangeRate> exchangeRates,
        string baseCurrency,
        DateOnly? asOfDate = null)
    {
        if (transactions is null) throw new ArgumentNullException(nameof(transactions));
        if (accounts is null) throw new ArgumentNullException(nameof(accounts));
        if (exchangeRates is null) throw new ArgumentNullException(nameof(exchangeRates));

        string baseCode = string.IsNullOrWhiteSpace(baseCurrency) ? "USD" : baseCurrency.ToUpperInvariant();
        DateOnly date = asOfDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var items = new List<ConsolidatedAccountBalance>();
        long assets = 0;
        long liabilities = 0;

        foreach (var account in accounts.OrderBy(a => a.Code))
        {
            long native = AccountBalance(transactions, account);
            if (native == 0 && account.Type is not (AccountType.Asset or AccountType.Liability))
                continue;

            var lookup = _rates.FindRate(exchangeRates, date, account.CurrencyCode, baseCode);
            long baseMinor = _rates.ConvertMinor(native, lookup.Rate);

            items.Add(new ConsolidatedAccountBalance
            {
                AccountId = account.Id,
                AccountCode = account.Code,
                AccountName = account.Name,
                AccountType = account.Type,
                NativeBalanceMinor = native,
                NativeCurrencyCode = account.CurrencyCode,
                BaseBalanceMinor = baseMinor,
                BaseCurrencyCode = baseCode,
                ExchangeRatePpm = lookup.Rate.RatePpm,
                Explanation = lookup.Explanation
            });

            if (account.Type == AccountType.Asset) assets += baseMinor;
            else if (account.Type == AccountType.Liability) liabilities += baseMinor;
        }

        return new ConsolidatedBalanceReport
        {
            AsOfDate = date,
            BaseCurrencyCode = baseCode,
            Accounts = items,
            AssetTotalMinor = assets,
            LiabilityTotalMinor = liabilities,
            NetWorthMinor = assets - liabilities
        };
    }

    private static long AccountBalance(IReadOnlyList<Transaction> transactions, Account account)
    {
        long raw = transactions.SelectMany(t => t.Postings)
            .Where(p => p.AccountId == account.Id)
            .Sum(p => p.AmountMinor);
        return account.IsDebitNormal ? raw : -raw;
    }
}
