using PersonalLedgerLab.Core;

namespace PersonalLedgerLab.Tests;

public class BudgetServiceTests
{
    private static (LedgerBook book, List<Account> accounts) Book()
    {
        var book = new LedgerBook();
        var salary = new Account(2, "4000", "Salary", AccountType.Income, "USD");
        var groceries = new Account(3, "5000", "Groceries", AccountType.Expense, "USD");
        var coffee = new Account(4, "5200", "Coffee", AccountType.Expense, "USD");
        var checking = new Account(1, "1000", "Checking", AccountType.Asset, "USD");
        book.AddAccount(checking);
        book.AddAccount(salary);
        book.AddAccount(groceries);
        book.AddAccount(coffee);
        book.AddTransaction(new DateOnly(2025, 1, 3), "Salary", "PAYROLL", "csv",
            new[] { (1L, 150000L, "USD"), (2L, -150000L, "USD") });
        book.AddTransaction(new DateOnly(2025, 1, 4), "SuperValu", null, "csv",
            new[] { (1L, -20308L, "USD"), (3L, 20308L, "USD") });
        book.AddTransaction(new DateOnly(2025, 1, 5), "Coffee", null, "csv",
            new[] { (1L, -1525L, "USD"), (4L, 1525L, "USD") });
        return (book, new List<Account> { checking, salary, groceries, coffee });
    }

    private static List<Budget> JanuaryBudgets() => new()
    {
        new(1, 2, "4000", "Salary target", 2025, 1, 1500_00L, "USD"),
        new(2, 3, "5000", "Groceries cap", 2025, 1, 250_00L, "USD"),
        new(3, 4, "5200", "Coffee cap", 2025, 1, 25_00L, "USD")
    };

    [Fact]
    public void Variance_is_actual_minus_limit_for_expense()
    {
        var (book, accounts) = Book();
        var summaries = SummaryService.Build(book.Transactions, accounts, "USD");
        var rows = BudgetService.Build(summaries, JanuaryBudgets(), accounts, "USD");

        var groceries = rows.Single(r => r.AccountCode == "5000");
        Assert.Equal(250_00L, groceries.LimitMinor);
        Assert.Equal(203_08L, groceries.ActualMinor);
        Assert.Equal(203_08L - 250_00L, groceries.VarianceMinor);
        Assert.Contains("under budget", groceries.Explanation);
    }

    [Fact]
    public void Variance_is_actual_minus_target_for_income()
    {
        var (book, accounts) = Book();
        var summaries = SummaryService.Build(book.Transactions, accounts, "USD");
        var rows = BudgetService.Build(summaries, JanuaryBudgets(), accounts, "USD");

        var salary = rows.Single(r => r.AccountCode == "4000");
        Assert.Equal(1500_00L, salary.LimitMinor);
        Assert.Equal(1500_00L, salary.ActualMinor);
        Assert.Equal(0L, salary.VarianceMinor);
        Assert.Contains("exactly on target", salary.Explanation);
    }

    [Fact]
    public void Over_budget_expense_reports_over_budget()
    {
        var (book, accounts) = Book();
        var budgets = new List<Budget>
        {
            new(10, 4, "5200", "Tight coffee cap", 2025, 1, 10_00L, "USD")
        };
        var summaries = SummaryService.Build(book.Transactions, accounts, "USD");
        var rows = BudgetService.Build(summaries, budgets, accounts, "USD");

        var coffee = rows.Single(r => r.AccountCode == "5200");
        Assert.Equal(15_25L - 10_00L, coffee.VarianceMinor);
        Assert.Contains("over budget", coffee.Explanation);
    }

    [Fact]
    public void Specific_budget_beats_recurring_for_same_month()
    {
        var (book, accounts) = Book();
        var budgets = new List<Budget>
        {
            new(1, 3, "5000", "Recurring groceries", 0, 0, 999_00L, "USD"),
            new(2, 3, "5000", "Jan groceries", 2025, 1, 250_00L, "USD")
        };
        var summaries = SummaryService.Build(book.Transactions, accounts, "USD");
        var rows = BudgetService.Build(summaries, budgets, accounts, "USD");

        var groceries = rows.Single(r => r.AccountCode == "5000");
        Assert.Equal(250_00L, groceries.LimitMinor);
        Assert.False(groceries.IsRecurring);
    }

    [Fact]
    public void Recurring_budget_applies_when_no_specific_budget_in_month()
    {
        var (book, accounts) = Book();
        var budgets = new List<Budget>
        {
            new(1, 4, "5200", "Recurring coffee", 0, 0, 25_00L, "USD")
        };
        var summaries = SummaryService.Build(book.Transactions, accounts, "USD");
        var rows = BudgetService.Build(summaries, budgets, accounts, "USD");

        var coffee = rows.Single(r => r.AccountCode == "5200");
        Assert.True(coffee.IsRecurring);
        Assert.Equal(25_00L, coffee.LimitMinor);
    }

    [Fact]
    public void Totals_sums_every_row()
    {
        var (book, accounts) = Book();
        var summaries = SummaryService.Build(book.Transactions, accounts, "USD");
        var rows = BudgetService.Build(summaries, JanuaryBudgets(), accounts, "USD");
        var (limit, actual, variance) = BudgetService.Totals(rows);

        Assert.Equal(250_00L + 25_00L + 1500_00L, limit);
        Assert.Equal(203_08L + 15_25L + 1500_00L, actual);
        Assert.Equal(actual - limit, variance);
    }

    [Fact]
    public void Budget_with_no_actual_counts_zero_actual()
    {
        var book = new LedgerBook();
        var checking = new Account(1, "1000", "Checking", AccountType.Asset, "USD");
        var dining = new Account(5, "5100", "Dining", AccountType.Expense, "USD");
        book.AddAccount(checking);
        book.AddAccount(dining);
        book.AddTransaction(new DateOnly(2025, 2, 4), "Salary", null, "csv",
            new[] { (1L, 100_00L, "USD"), (1L, -100_00L, "USD") });

        var summaries = SummaryService.Build(book.Transactions, new List<Account> { checking, dining }, "USD");
        var budgets = new List<Budget>
        {
            new(1, 5, "5100", "Feb dining", 2025, 2, 50_00L, "USD")
        };
        var rows = BudgetService.Build(summaries, budgets, new List<Account> { checking, dining }, "USD");

        var row = Assert.Single(rows);
        Assert.Equal(0L, row.ActualMinor);
        Assert.Equal(-50_00L, row.VarianceMinor);
        Assert.Contains("under budget", row.Explanation);
    }

    [Fact]
    public void Null_arguments_throw()
    {
        Assert.Throws<ArgumentNullException>(() =>
            BudgetService.Build(null!, new List<Budget>(), new List<Account>(), "USD"));
        Assert.Throws<ArgumentNullException>(() =>
            BudgetService.Build(new List<MonthlySummary>(), null!, new List<Account>(), "USD"));
        Assert.Throws<ArgumentNullException>(() =>
            BudgetService.Build(new List<MonthlySummary>(), new List<Budget>(), null!, "USD"));
    }
}