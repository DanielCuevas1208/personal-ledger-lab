using PersonalLedgerLab.Core;

namespace PersonalLedgerLab.Tests;

public class ConsolidatedBalanceServiceTests
{
    private static readonly IReadOnlyList<ExchangeRate> Rates = new[]
    {
        new ExchangeRate(1, new DateOnly(2025, 1, 1), "EUR", "USD", ExchangeRateMath.FromDecimal(1.100000m))
    };

    [Fact]
    public void Consolidated_net_worth_converts_foreign_assets()
    {
        var checking = new Account(1, "1000", "Checking", AccountType.Asset, "USD");
        var eurSavings = new Account(2, "1050", "EUR Savings", AccountType.Asset, "EUR");
        var openingUsd = new Account(3, "3000", "Opening Balances", AccountType.Equity, "USD");
        var openingEur = new Account(4, "3001", "Opening Balances EUR", AccountType.Equity, "EUR");
        var accounts = new List<Account> { checking, eurSavings, openingUsd, openingEur };

        var usdTx = new Transaction(1, new DateOnly(2025, 1, 5), "USD opening", null, "test");
        usdTx.Postings.Add(new Posting(1, 1, checking.Id, 100_00, "USD"));
        usdTx.Postings.Add(new Posting(2, 1, openingUsd.Id, -100_00, "USD"));

        var eurTx = new Transaction(2, new DateOnly(2025, 1, 6), "EUR opening", null, "test");
        eurTx.Postings.Add(new Posting(3, 2, eurSavings.Id, 200_00, "EUR"));
        eurTx.Postings.Add(new Posting(4, 2, openingEur.Id, -200_00, "EUR"));

        var service = new ConsolidatedBalanceService();
        var report = service.Build(
            new[] { usdTx, eurTx },
            accounts,
            Rates,
            "USD",
            new DateOnly(2025, 1, 31));

        Assert.Equal(100_00L, report.Accounts.Single(a => a.AccountCode == "1000").BaseBalanceMinor);
        Assert.Equal(220_00L, report.Accounts.Single(a => a.AccountCode == "1050").BaseBalanceMinor);
        Assert.Equal(320_00L, report.NetWorthMinor);
    }
}
