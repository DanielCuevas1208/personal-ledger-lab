using PersonalLedgerLab.Core;

namespace PersonalLedgerLab.Tests;

public class SummaryServiceTests
{
    private static LedgerBook Build()
    {
        var book = new LedgerBook();
        book.AddAccount(new Account(1, "1000", "Checking", AccountType.Asset, "USD"));
        book.AddAccount(new Account(2, "4000", "Salary", AccountType.Income, "USD"));
        book.AddAccount(new Account(3, "5000", "Groceries", AccountType.Expense, "USD"));
        book.AddTransaction(new DateOnly(2025, 1, 3), "Salary", "PAYROLL", "csv",
            new[] { (1L, 150000L, "USD"), (2L, -150000L, "USD") });
        book.AddTransaction(new DateOnly(2025, 1, 4), "Groceries", null, "csv",
            new[] { (1L, -50000L, "USD"), (3L, 50000L, "USD") });
        book.AddTransaction(new DateOnly(2025, 2, 5), "Groceries", null, "csv",
            new[] { (1L, -30000L, "USD"), (3L, 30000L, "USD") });
        return book;
    }

    [Fact]
    public void Summary_groups_by_month()
    {
        var book = Build();
        var summaries = SummaryService.Build(book.Transactions, book.Accounts, "USD");
        Assert.Equal(2, summaries.Count);
        Assert.Equal(1, summaries[0].Month);
        Assert.Equal(2, summaries[1].Month);
    }

    [Fact]
    public void Expense_net_is_debit_minus_credit()
    {
        var book = Build();
        var summaries = SummaryService.Build(book.Transactions, book.Accounts, "USD");
        var jan = summaries.Single(s => s.Month == 1);
        var groceries = jan.Items.Single(i => i.AccountCode == "5000");
        Assert.Equal(50000L, groceries.NetMinor);
        Assert.Equal(50000L, jan.ExpenseMinor);
    }

    [Fact]
    public void Income_net_is_credit_minus_debit()
    {
        var book = Build();
        var summaries = SummaryService.Build(book.Transactions, book.Accounts, "USD");
        var jan = summaries.Single(s => s.Month == 1);
        var salary = jan.Items.Single(i => i.AccountCode == "4000");
        Assert.Equal(150000L, salary.NetMinor);
        Assert.Equal(150000L, jan.IncomeMinor);
    }

    [Fact]
    public void Net_income_subtracts_expense_from_income()
    {
        var book = Build();
        var summaries = SummaryService.Build(book.Transactions, book.Accounts, "USD");
        var jan = summaries.Single(s => s.Month == 1);
        Assert.Equal(100000L, jan.NetIncomeMinor);
    }

    [Fact]
    public void Month_label_formats_year_month()
    {
        Assert.Equal("2025-02", SummaryService.MonthLabel(2025, 2));
    }
}