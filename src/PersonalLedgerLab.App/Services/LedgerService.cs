using System.Globalization;
using System.Text;
using PersonalLedgerLab.Core;
using PersonalLedgerLab.Data;

namespace PersonalLedgerLab.App.Services;

/// <summary>
/// Facade that coordinates persistence, import, rules, summary and export.
/// The view-model calls only this class so the UI stays free of storage detail.
/// </summary>
public sealed class LedgerService
{
    private readonly ILedgerStore _store;
    private readonly LedgerImportRunner _importRunner = new();
    private readonly CsvStatementImporter _importer = new();

    public LedgerService(ILedgerStore store)
    {
        _store = store ?? SqliteLedgerStoreFactory.CreateDefault();
        RefreshCache();
    }

    public IReadOnlyList<Account> Accounts { get; private set; } = Array.Empty<Account>();
    public IReadOnlyList<CategoryRule> Rules { get; private set; } = Array.Empty<CategoryRule>();
    public IReadOnlyList<Transaction> Transactions { get; private set; } = Array.Empty<Transaction>();

    public bool IsFreshDatabase => _store.IsFreshDatabase;
    public string DatabasePath => _store.DatabasePath;

    public Account GetAccount(long id) =>
        Accounts.First(a => a.Id == id);

    public Account? GetAccountByCode(string code) =>
        Accounts.FirstOrDefault(a => string.Equals(a.Code, code, StringComparison.Ordinal));

    public IReadOnlyList<Account> CategorizationTargets =>
        Accounts.Where(a => a.Type == AccountType.Expense || a.Type == AccountType.Income)
            .OrderBy(a => a.Code).ToList();

    public IReadOnlyList<Account> AssetAccounts =>
        Accounts.Where(a => a.Type == AccountType.Asset).OrderBy(a => a.Code).ToList();

    public void RefreshCache()
    {
        Accounts = _store.LoadAccounts();
        Rules = _store.LoadRules();
        Transactions = _store.LoadTransactions();
    }

    public StatementImportResult ImportStatement(string csvPath, long bankAccountId)
    {
        var bankAccount = Accounts.First(a => a.Id == bankAccountId);
        var suspense = Accounts.First(a => a.Code == "3999");
        var categoryById = Accounts.ToDictionary(a => a.Id);

        var rows = _importer.ReadFile(csvPath);

        var book = new LedgerBook();
        foreach (var account in Accounts)
            book.AddAccount(account);

        var import = new ImportedLedger
        {
            Book = book,
            Bank = bankAccount,
            Suspense = suspense,
            Rows = rows,
            Rules = Rules,
            Categories = categoryById
        };

        var result = _importRunner.Import(
            import.Book, import.Bank, import.Suspense,
            import.Rows, import.Rules, import.Categories);

        foreach (var tx in import.Book.Transactions)
            _store.SaveTransaction(tx);

        RefreshCache();
        return result;
    }

    public LedgerBook BuildBook()
    {
        var book = new LedgerBook();
        foreach (var account in Accounts)
            book.AddAccount(account);
        foreach (var tx in Transactions)
            book.AttachTransaction(tx);
        return book;
    }

    public long AccountBalance(long accountId)
    {
        var account = Accounts.First(a => a.Id == accountId);
        long raw = Transactions.SelectMany(t => t.Postings)
            .Where(p => p.AccountId == accountId)
            .Sum(p => p.AmountMinor);
        return account.IsDebitNormal ? raw : -raw;
    }

    public long NetWorth()
    {
        long assets = 0;
        long liabilities = 0;
        foreach (var account in Accounts)
        {
            long raw = Transactions.SelectMany(t => t.Postings)
                .Where(p => p.AccountId == account.Id)
                .Sum(p => p.AmountMinor);
            long balance = account.IsDebitNormal ? raw : -raw;
            if (account.Type == AccountType.Asset) assets += balance;
            else if (account.Type == AccountType.Liability) liabilities += balance;
        }
        return assets - liabilities;
    }

    public IReadOnlyList<MonthlySummary> BuildSummary(string currency = "USD") =>
        SummaryService.Build(Transactions, Accounts, currency);

    public long AddRule(string name, int priority, RuleMatchType matchType, string matchValue, long categoryId)
    {
        var account = Accounts.First(a => a.Id == categoryId);
        var rule = new CategoryRule
        {
            Name = name,
            Priority = priority,
            MatchType = matchType,
            MatchValue = matchValue,
            CategoryAccountId = categoryId,
            CategoryAccountCode = account.Code
        };
        var id = _store.AddRule(rule);
        RefreshCache();
        return id;
    }

    public bool UpdateRule(long id, string name, int priority, RuleMatchType matchType,
        string matchValue, long categoryId)
    {
        var account = Accounts.First(a => a.Id == categoryId);
        var rule = new CategoryRule
        {
            Id = id,
            Name = name,
            Priority = priority,
            MatchType = matchType,
            MatchValue = matchValue,
            CategoryAccountId = categoryId,
            CategoryAccountCode = account.Code
        };
        var ok = _store.UpdateRule(rule);
        RefreshCache();
        return ok;
    }

    public bool DeleteRule(long id)
    {
        var ok = _store.DeleteRule(id);
        RefreshCache();
        return ok;
    }

    public RuleResult PreviewCategorization(string description, string? reference)
    {
        var draft = new Transaction(0, DateOnly.MinValue, description, reference, "preview");
        var engine = new RuleEngine();
        return engine.Categorize(draft, Rules);
    }

    public void ExportLedger(string path)
    {
        using var writer = new StreamWriter(path, false, Encoding.UTF8);
        CsvExporter.ExportLedger(Transactions, Accounts, writer);
    }

    public void ExportSummary(string path)
    {
        using var writer = new StreamWriter(path, false, Encoding.UTF8);
        CsvExporter.ExportSummary(BuildSummary(), writer);
    }

    public string StartupReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Personal Ledger Lab");
        sb.AppendLine($"Database: {DatabasePath}");
        sb.AppendLine($"Accounts: {Accounts.Count}, Rules: {Rules.Count}, Transactions: {Transactions.Count}");
        sb.AppendLine($"Fresh database: {IsFreshDatabase}");
        return sb.ToString();
    }

    public static string FormatMinor(long minor) =>
        (minor / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    private sealed class ImportedLedger
    {
        public LedgerBook Book { get; set; } = new();
        public Account Bank { get; set; } = null!;
        public Account Suspense { get; set; } = null!;
        public IReadOnlyList<ImportedRow> Rows { get; set; } = Array.Empty<ImportedRow>();
        public IReadOnlyList<CategoryRule> Rules { get; set; } = Array.Empty<CategoryRule>();
        public IReadOnlyDictionary<long, Account> Categories { get; set; } = new Dictionary<long, Account>();
    }
}