using PersonalLedgerLab.Core;
using PersonalLedgerLab.Data;

namespace PersonalLedgerLab.Tests;

public class SqliteLedgerStoreTests : IDisposable
{
    private readonly string _path;
    private readonly SqliteLedgerStore _store;

    public SqliteLedgerStoreTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"pll-test-{Guid.NewGuid():N}.sqlite");
        _store = new SqliteLedgerStore(_path);
    }

    [Fact]
    public void Fresh_store_seeds_chart_of_accounts_and_rules()
    {
        var accounts = _store.LoadAccounts();
        Assert.Contains(accounts, a => a.Code == "1000");
        Assert.Contains(accounts, a => a.Code == "3999");
        Assert.Contains(accounts, a => a.Code == "5100");
        Assert.True(_store.LoadRules().Count > 0);
    }

    [Fact]
    public void Save_transaction_round_trips_postings()
    {
        var bank = _store.LoadAccounts().First(a => a.Code == "1000");
        var groceries = _store.LoadAccounts().First(a => a.Code == "5000");
        var tx = new Transaction(0, new DateOnly(2025, 1, 4), "SuperValu", null, "import")
        {
            AppliedRuleId = 1,
            RuleExplanation = "matched"
        };
        tx.Postings.Add(new Posting(0, 0, bank.Id, -50_00, "USD"));
        tx.Postings.Add(new Posting(0, 0, groceries.Id, 50_00, "USD"));

        _store.SaveTransaction(tx);
        var loaded = _store.LoadTransactions();
        var got = Assert.Single(loaded);
        Assert.True(got.IsBalanced);
        Assert.Equal(2, got.Postings.Count);
        Assert.Equal("matched", got.RuleExplanation);
    }

    [Fact]
    public void Unbalanced_transaction_rejected()
    {
        var bank = _store.LoadAccounts().First(a => a.Code == "1000");
        var tx = new Transaction(0, new DateOnly(2025, 1, 5), "Bad", null, "import");
        tx.Postings.Add(new Posting(0, 0, bank.Id, 10_00, "USD"));
        Assert.Throws<LedgerBalanceException>(() => _store.SaveTransaction(tx));
    }

    [Fact]
    public void Add_update_delete_rule_persists()
    {
        var category = _store.LoadAccounts().First(a => a.Code == "5200");
        var id = _store.AddRule(new CategoryRule
        {
            Name = "Test",
            Priority = 99,
            MatchType = RuleMatchType.DescriptionContains,
            MatchValue = "latte",
            CategoryAccountId = category.Id,
            CategoryAccountCode = category.Code
        });
        Assert.True(id > 0);

        var stored = _store.LoadRules().Single(r => r.Id == id);
        Assert.Equal("latte", stored.MatchValue);

        stored.Name = "Test v2";
        Assert.True(_store.UpdateRule(stored));
        Assert.Equal("Test v2", _store.LoadRules().Single(r => r.Id == id).Name);

        Assert.True(_store.DeleteRule(id));
        Assert.DoesNotContain(_store.LoadRules(), r => r.Id == id);
    }

    [Fact]
    public void Clear_transactions_empties_ledger()
    {
        var bank = _store.LoadAccounts().First(a => a.Code == "1000");
        var salary = _store.LoadAccounts().First(a => a.Code == "4000");
        var tx = new Transaction(0, new DateOnly(2025, 1, 3), "Salary", "PAYROLL", "import");
        tx.Postings.Add(new Posting(0, 0, bank.Id, 100_00, "USD"));
        tx.Postings.Add(new Posting(0, 0, salary.Id, -100_00, "USD"));
        _store.SaveTransaction(tx);

        _store.ClearTransactions();
        Assert.Empty(_store.LoadTransactions());
    }

    [Fact]
    public void Second_open_is_not_fresh()
    {
        _store.Dispose();
        using var reopened = new SqliteLedgerStore(_path);
        Assert.False(reopened.IsFreshDatabase);
    }

    [Fact]
    public void Fresh_store_seeds_january_budgets()
    {
        var budgets = _store.LoadBudgets();
        Assert.NotEmpty(budgets);
        Assert.Contains(budgets, b => b.Year == 2025 && b.Month == 1 && b.CategoryAccountCode == "5000");
        Assert.Contains(budgets, b => b.Year == 2025 && b.Month == 1 && b.CategoryAccountCode == "4000");
    }

    [Fact]
    public void Add_update_delete_budget_persists()
    {
        var groceries = _store.LoadAccounts().First(a => a.Code == "5000");
        var id = _store.AddBudget(new Budget
        {
            Name = "Test budget",
            Year = 2025,
            Month = 2,
            CategoryAccountId = groceries.Id,
            CategoryAccountCode = groceries.Code,
            LimitMinor = 300_00L,
            CurrencyCode = "USD",
            Note = "test"
        });
        Assert.True(id > 0);

        var stored = _store.LoadBudgets().Single(b => b.Id == id);
        Assert.Equal(300_00L, stored.LimitMinor);
        Assert.Equal(2025, stored.Year);
        Assert.Equal(2, stored.Month);

        stored.Name = "Test budget v2";
        stored.LimitMinor = 275_00L;
        Assert.True(_store.UpdateBudget(stored));
        var after = _store.LoadBudgets().Single(b => b.Id == id);
        Assert.Equal("Test budget v2", after.Name);
        Assert.Equal(275_00L, after.LimitMinor);

        Assert.True(_store.DeleteBudget(id));
        Assert.DoesNotContain(_store.LoadBudgets(), b => b.Id == id);
    }

    public void Dispose()
    {
        _store.Dispose();
        try { File.Delete(_path); } catch { }
    }
}