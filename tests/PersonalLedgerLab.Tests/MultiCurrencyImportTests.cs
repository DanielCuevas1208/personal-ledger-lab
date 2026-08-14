using PersonalLedgerLab.Core;

namespace PersonalLedgerLab.Tests;

public class MultiCurrencyImportTests
{
    private static readonly IReadOnlyList<ExchangeRate> Rates = new[]
    {
        new ExchangeRate(1, new DateOnly(2025, 1, 1), "EUR", "USD", ExchangeRateMath.FromDecimal(1.085000m))
    };

    private static LedgerBook NewBook(out Account bank, out Account suspense, out Dictionary<long, Account> categories)
    {
        var book = new LedgerBook();
        bank = new Account(1, "1000", "Checking", AccountType.Asset, "USD");
        var groceries = new Account(3, "5000", "Groceries", AccountType.Expense, "USD");
        suspense = new Account(99, "3999", "Suspense", AccountType.Equity, "USD");
        book.AddAccount(bank);
        book.AddAccount(groceries);
        book.AddAccount(suspense);
        categories = new Dictionary<long, Account> { [3] = groceries };
        return book;
    }

    [Fact]
    public void Import_converts_foreign_currency_rows_to_bank_currency()
    {
        var book = NewBook(out var bank, out var suspense, out var cats);
        var rows = new List<ImportedRow>
        {
            new()
            {
                Date = new DateOnly(2025, 1, 8),
                Description = "Paris supervalu groceries",
                AmountMinor = -38_25,
                RunningBalanceMinor = 461_75,
                CurrencyCode = "EUR"
            }
        };

        var rules = new List<CategoryRule>
        {
            new(1, "Groceries", 10, RuleMatchType.DescriptionContains, "supervalu", 3, "5000")
        };

        var result = new LedgerImportRunner().Import(book, bank, suspense, rows, rules, cats, Rates);
        var tx = Assert.Single(book.Transactions);

        Assert.Equal(-41_50L, tx.Postings.Single(p => p.AccountId == bank.Id).AmountMinor);
        Assert.Equal(41_50L, tx.Postings.Single(p => p.AccountId == 3).AmountMinor);
        Assert.Equal("EUR", tx.SourceCurrencyCode);
        Assert.Equal(-38_25L, tx.SourceAmountMinor);
        Assert.Contains("Converted", tx.RuleExplanation);
        Assert.Equal(1, result.TransactionCount);
    }
}
