using PersonalLedgerLab.Core;

namespace PersonalLedgerLab.Tests;

public class LedgerBookTests
{
    private static LedgerBook NewBook()
    {
        var book = new LedgerBook();
        book.AddAccount(new Account(1, "1000", "Checking", AccountType.Asset, "USD"));
        book.AddAccount(new Account(2, "3000", "Opening Balances", AccountType.Equity, "USD"));
        book.AddAccount(new Account(3, "4000", "Salary", AccountType.Income, "USD"));
        book.AddAccount(new Account(4, "5000", "Groceries", AccountType.Expense, "USD"));
        return book;
    }

    [Fact]
    public void Balanced_transaction_postings_sum_to_zero()
    {
        var book = NewBook();
        var tx = book.AddTransaction(new DateOnly(2025, 1, 3),
            "Salary", "PAYROLL", "csv-import",
            new[]
            {
                (1L, 1500_00L, "USD"),
                (3L, -1500_00L, "USD")
            });
        Assert.True(tx.IsBalanced);
    }

    [Fact]
    public void Unbalanced_transaction_throws()
    {
        var book = NewBook();
        Assert.Throws<LedgerBalanceException>(() => book.AddTransaction(
            new DateOnly(2025, 1, 4), "Bad", null, "csv-import",
            new[]
            {
                (1L, 100_00L, "USD"),
                (3L, -90_00L, "USD")
            }));
    }

    [Fact]
    public void Single_leg_transaction_throws()
    {
        var book = NewBook();
        Assert.Throws<LedgerBalanceException>(() => book.AddTransaction(
            new DateOnly(2025, 1, 5), "One leg", null, "csv-import",
            new[] { (1L, 5_00L, "USD") }));
    }

    [Fact]
    public void Asset_balance_increases_for_deposits()
    {
        var book = NewBook();
        book.AddTransaction(new DateOnly(2025, 1, 1), "Opening", null, "seed",
            new[] { (1L, 1000_00L, "USD"), (2L, -1000_00L, "USD") });
        Assert.Equal(1000_00L, book.BalanceOf("1000"));
    }

    [Fact]
    public void Income_account_shows_positive_credit_balance()
    {
        var book = NewBook();
        book.AddTransaction(new DateOnly(2025, 1, 3), "Salary", "PAYROLL", "csv-import",
            new[] { (1L, 1500_00L, "USD"), (3L, -1500_00L, "USD") });
        Assert.Equal(1500_00L, book.BalanceOf("4000"));
    }

    [Fact]
    public void Net_worth_excludes_income_and_expense()
    {
        var book = NewBook();
        book.AddTransaction(new DateOnly(2025, 1, 1), "Opening", null, "seed",
            new[] { (1L, 1000_00L, "USD"), (2L, -1000_00L, "USD") });
        book.AddTransaction(new DateOnly(2025, 1, 3), "Salary", "PAYROLL", "import",
            new[] { (1L, 1500_00L, "USD"), (3L, -1500_00L, "USD") });
        book.AddTransaction(new DateOnly(2025, 1, 4), "Groceries", null, "import",
            new[] { (1L, -50_00L, "USD"), (4L, 50_00L, "USD") });
        Assert.Equal(2450_00L, book.NetWorth());
    }

    [Fact]
    public void Account_code_collision_rejected()
    {
        var book = new LedgerBook();
        book.AddAccount(new Account(1, "1000", "A", AccountType.Asset, "USD"));
        Assert.Throws<InvalidOperationException>(() =>
            book.AddAccount(new Account(2, "1000", "B", AccountType.Asset, "USD")));
    }
}