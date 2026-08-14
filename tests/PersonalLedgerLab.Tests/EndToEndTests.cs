using PersonalLedgerLab.Core;
using PersonalLedgerLab.Data;

namespace PersonalLedgerLab.Tests;

public class EndToEndTests : IDisposable
{
    private readonly string _path;
    private readonly SqliteLedgerStore _store;

    public EndToEndTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"pll-e2e-{Guid.NewGuid():N}.sqlite");
        _store = new SqliteLedgerStore(_path);
    }

    private static string SamplePath =>
        Path.Combine(AppContext.BaseDirectory, "samples", "sample-statement.csv");

    private static string OfxSamplePath =>
        Path.Combine(AppContext.BaseDirectory, "samples", "sample-statement.ofx");

    [Fact]
    public void Sample_statement_import_balance_and_summary_match()
    {
        var accounts = _store.LoadAccounts();
        var rules = _store.LoadRules();
        var byId = accounts.ToDictionary(a => a.Id);
        var bank = accounts.First(a => a.Code == "1000");
        var suspense = accounts.First(a => a.Code == "3999");

        var rows = new CsvStatementImporter().ReadFile(SamplePath);
        var book = new LedgerBook();
        foreach (var a in accounts)
            book.AddAccount(a);

        var result = new LedgerImportRunner().Import(book, bank, suspense, rows, rules, byId, Array.Empty<ExchangeRate>());

        Assert.Equal(15, result.TransactionCount);
        Assert.True(result.Reconciles);
        Assert.Equal(1000_00L, result.OpeningBalanceMinor);
        Assert.Equal(1807_48L, result.ActualClosingBalanceMinor);
        Assert.Empty(result.Warnings);

        foreach (var tx in book.Transactions)
            _store.SaveTransaction(tx);

        var loaded = _store.LoadTransactions();
        Assert.Equal(15, loaded.Count);
        Assert.All(loaded, t => Assert.True(t.IsBalanced));

        var summaries = SummaryService.Build(loaded, accounts, "USD");
        var jan = Assert.Single(summaries);
        Assert.Equal(1500_00L, jan.IncomeMinor);
        Assert.True(jan.ExpenseMinor > 600_00L && jan.ExpenseMinor < 700_00L);

        var grocery = jan.Items.Single(i => i.AccountCode == "5000");
        Assert.Equal(203_08L, grocery.NetMinor);
        var coffee = jan.Items.Single(i => i.AccountCode == "5200");
        Assert.Equal(15_25L, coffee.NetMinor);
    }

    [Fact]
    public void Seeded_budgets_explain_variance_for_sample_month()
    {
        var accounts = _store.LoadAccounts();
        var rules = _store.LoadRules();
        var budgets = _store.LoadBudgets();
        var bank = accounts.First(a => a.Code == "1000");
        var suspense = accounts.First(a => a.Code == "3999");
        var byId = accounts.ToDictionary(a => a.Id);

        var rows = new CsvStatementImporter().ReadFile(SamplePath);
        var book = new LedgerBook();
        foreach (var a in accounts)
            book.AddAccount(a);

        new LedgerImportRunner().Import(book, bank, suspense, rows, rules, byId, Array.Empty<ExchangeRate>());
        foreach (var tx in book.Transactions)
            _store.SaveTransaction(tx);

        var summaries = SummaryService.Build(_store.LoadTransactions(), accounts, "USD");
        var variance = BudgetService.Build(summaries, budgets, accounts, "USD");

        Assert.NotEmpty(variance);
        var salary = variance.Single(v => v.AccountCode == "4000");
        Assert.Equal(1500_00L, salary.LimitMinor);
        Assert.Equal(1500_00L, salary.ActualMinor);
        Assert.Equal(0L, salary.VarianceMinor);

        var groceries = variance.Single(v => v.AccountCode == "5000");
        Assert.Equal(250_00L, groceries.LimitMinor);
        Assert.Equal(203_08L, groceries.ActualMinor);
        Assert.Equal(203_08L - 250_00L, groceries.VarianceMinor);

        var transport = variance.Single(v => v.AccountCode == "5300");
        Assert.Equal(100_15L, transport.ActualMinor);
        Assert.True(transport.VarianceMinor > 0);
        Assert.Contains("over budget", transport.Explanation);
        Assert.Contains("under budget", groceries.Explanation);
    }

    [Fact]
    public void Sample_import_attaches_rule_explanations()
    {
        var accounts = _store.LoadAccounts();
        var rules = _store.LoadRules();
        var bank = accounts.First(a => a.Code == "1000");
        var suspense = accounts.First(a => a.Code == "3999");
        var rows = new CsvStatementImporter().ReadFile(SamplePath);
        var book = new LedgerBook();
        foreach (var a in accounts)
            book.AddAccount(a);

        new LedgerImportRunner().Import(book, bank, suspense, rows, rules, accounts.ToDictionary(a => a.Id), Array.Empty<ExchangeRate>());

        var salaryTx = book.Transactions.Single(t => t.Description.StartsWith("Salary"));
        Assert.NotNull(salaryTx.AppliedRuleId);
        Assert.Contains("Salary", salaryTx.RuleExplanation!);
    }

    [Fact]
    public void Eur_sample_imports_into_eur_savings_account()
    {
        var accounts = _store.LoadAccounts();
        var rules = _store.LoadRules();
        var rates = _store.LoadExchangeRates();
        var bank = accounts.First(a => a.Code == "1050");
        var suspense = accounts.First(a => a.Code == "3999");
        var path = Path.Combine(AppContext.BaseDirectory, "samples", "sample-statement-eur.csv");
        var rows = new CsvStatementImporter().ReadFile(path);
        var book = new LedgerBook();
        foreach (var account in accounts)
            book.AddAccount(account);

        var result = new LedgerImportRunner().Import(
            book, bank, suspense, rows, rules, accounts.ToDictionary(a => a.Id), rates);

        Assert.Equal(3, result.TransactionCount);
        Assert.True(result.Reconciles);
        Assert.All(book.Transactions, t => Assert.True(t.IsBalanced));
        Assert.All(book.Transactions, t => Assert.All(t.Postings, p => Assert.Equal("EUR", p.CurrencyCode)));
    }

    [Fact]
    public void Sample_ofx_import_uses_the_same_ledger_pipeline()
    {
        var accounts = _store.LoadAccounts();
        var rules = _store.LoadRules();
        var bank = accounts.First(a => a.Code == "1000");
        var suspense = accounts.First(a => a.Code == "3999");
        var statement = new StatementImporterRegistry().ReadFile(OfxSamplePath);
        var book = new LedgerBook();
        foreach (var account in accounts)
            book.AddAccount(account);

        var result = new LedgerImportRunner().Import(
            book, bank, suspense, statement, rules, accounts.ToDictionary(a => a.Id), _store.LoadExchangeRates());

        Assert.Equal("OFX", result.FormatName);
        Assert.Equal(2, result.TransactionCount);
        Assert.True(result.Reconciles);
        Assert.Equal(1000_00L, result.OpeningBalanceMinor);
        Assert.Equal(2415_63L, result.ActualClosingBalanceMinor);
        Assert.Contains(result.Warnings, warning => warning.Contains("inferred", StringComparison.OrdinalIgnoreCase));
        Assert.All(book.Transactions, transaction => Assert.Equal("ofx-import", transaction.Source));
    }

    public void Dispose()
    {
        _store.Dispose();
        try { File.Delete(_path); } catch { }
    }
}