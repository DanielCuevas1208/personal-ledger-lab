using PersonalLedgerLab.Core;

namespace PersonalLedgerLab.Tests;

public class LedgerImportRunnerTests
{
    private static LedgerBook NewBook(out Account bank, out Account suspense, out Dictionary<long, Account> categories)
    {
        var book = new LedgerBook();
        bank = new Account(1, "1000", "Checking", AccountType.Asset, "USD");
        var salary = new Account(2, "4000", "Salary", AccountType.Income, "USD");
        var groceries = new Account(3, "5000", "Groceries", AccountType.Expense, "USD");
        suspense = new Account(99, "3999", "Suspense", AccountType.Equity, "USD");
        book.AddAccount(bank);
        book.AddAccount(salary);
        book.AddAccount(groceries);
        book.AddAccount(suspense);
        categories = new Dictionary<long, Account> { [2] = salary, [3] = groceries };
        return book;
    }

    private static List<CategoryRule> Rules() => new()
    {
        new(1, "Groceries", 10, RuleMatchType.DescriptionContains, "supervalu", 3, "5000"),
        new(2, "Salary", 5, RuleMatchType.ReferenceEquals, "PAYROLL", 2, "4000")
    };

    private static List<ImportedRow> Rows()
    {
        return new()
        {
            new ImportedRow { Date = new DateOnly(2025, 1, 3), Reference = "PAYROLL", Description = "Salary ACME", AmountMinor = 150000, RunningBalanceMinor = 250000, CurrencyCode = "USD" },
            new ImportedRow { Date = new DateOnly(2025, 1, 4), Description = "SuperValu groceries", AmountMinor = -8437, RunningBalanceMinor = 241563, CurrencyCode = "USD" },
            new ImportedRow { Date = new DateOnly(2025, 1, 5), Description = "Mystery charge", AmountMinor = -2000, RunningBalanceMinor = 239563, CurrencyCode = "USD" }
        };
    }

    [Fact]
    public void Import_balances_every_transaction()
    {
        var book = NewBook(out var bank, out var suspense, out var cats);
        var result = new LedgerImportRunner().Import(book, bank, suspense, Rows(), Rules(), cats);
        Assert.Equal(3, result.TransactionCount);
        Assert.All(book.Transactions, t => Assert.True(t.IsBalanced));
    }

    [Fact]
    public void Reconciliation_uses_running_balance()
    {
        var book = NewBook(out var bank, out var suspense, out var cats);
        var result = new LedgerImportRunner().Import(book, bank, suspense, Rows(), Rules(), cats);
        Assert.True(result.Reconciles);
        Assert.Equal(100000L, result.OpeningBalanceMinor);
        Assert.Equal(239563L, result.ActualClosingBalanceMinor);
        Assert.Equal(239563L, result.ExpectedClosingBalanceMinor);
    }

    [Fact]
    public void Unmatched_rows_go_to_suspense_with_explanation()
    {
        var book = NewBook(out var bank, out var suspense, out var cats);
        var result = new LedgerImportRunner().Import(book, bank, suspense, Rows(), Rules(), cats);
        var mystery = book.Transactions.Single(t => t.Description == "Mystery charge");
        Assert.Contains(mystery.Postings, p => p.AccountId == suspense.Id);
        Assert.Contains("No rule matched", mystery.RuleExplanation);
    }

    [Fact]
    public void Matched_rows_apply_explainable_rule()
    {
        var book = NewBook(out var bank, out var suspense, out var cats);
        new LedgerImportRunner().Import(book, bank, suspense, Rows(), Rules(), cats);
        var salary = book.Transactions.Single(t => t.Description == "Salary ACME");
        Assert.Equal(2, salary.AppliedRuleId);
        Assert.Equal(2, salary.Postings.First(p => p.AccountId == 2).AccountId);
    }

    [Fact]
    public void Broken_statement_running_balance_reports_warning_and_disagreement()
    {
        var book = NewBook(out var bank, out var suspense, out var cats);
        var rows = new List<ImportedRow>
        {
            new() { Date = new DateOnly(2025,1,3), Description = "In", AmountMinor = 10000, RunningBalanceMinor = 20000, CurrencyCode = "USD" },
            new() { Date = new DateOnly(2025,1,4), Description = "Out", AmountMinor = -5000, RunningBalanceMinor = 100000, CurrencyCode = "USD" }
        };
        var result = new LedgerImportRunner().Import(book, bank, suspense, rows, Rules(), cats);
        Assert.False(result.Reconciles);
        Assert.NotEmpty(result.Warnings);
    }
}