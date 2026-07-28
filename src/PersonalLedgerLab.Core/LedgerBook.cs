namespace PersonalLedgerLab.Core;

/// <summary>
/// Thrown when a transaction cannot be balanced or violates ledger rules.
/// </summary>
public sealed class LedgerBalanceException : InvalidOperationException
{
    public LedgerBalanceException(string message) : base(message) { }
}

/// <summary>
/// An in-memory ledger book. It keeps accounts and transactions,
/// validates that every new transaction is balanced, and reconciles balances.
/// The data layer uses the same model when it persists records.
/// </summary>
public sealed class LedgerBook
{
    private readonly Dictionary<long, Account> _accountsById = new();
    private readonly Dictionary<string, Account> _accountsByCode = new(StringComparer.Ordinal);
    private readonly List<Transaction> _transactions = new();
    private long _nextTransactionId;
    private long _nextPostingId;

    public IReadOnlyList<Account> Accounts => _accountsById.Values.ToList();
    public IReadOnlyList<Transaction> Transactions => _transactions;

    public LedgerBook AddAccount(Account account)
    {
        if (account is null) throw new ArgumentNullException(nameof(account));
        if (_accountsByCode.ContainsKey(account.Code))
            throw new InvalidOperationException($"Account code {account.Code} already exists.");
        _accountsById[account.Id] = account;
        _accountsByCode[account.Code] = account;
        return this;
    }

    public Account GetAccount(long id) =>
        _accountsById.TryGetValue(id, out var a)
            ? a
            : throw new KeyNotFoundException($"Account {id} not found.");

    public Account GetAccountByCode(string code) =>
        _accountsByCode.TryGetValue(code, out var a)
            ? a
            : throw new KeyNotFoundException($"Account code {code} not found.");

    public bool TryGetAccountByCode(string code, out Account account) =>
        _accountsByCode.TryGetValue(code, out account!);

    public Transaction AddTransaction(
        DateOnly date,
        string description,
        string? reference,
        string source,
        IEnumerable<(long AccountId, long AmountMinor, string Currency)> legs)
    {
        var tx = new Transaction(NextTransactionId(), date, description, reference, source);
        long signed = 0;
        foreach (var (accountId, amount, currency) in legs)
        {
            var account = GetAccount(accountId);
            var posting = new Posting(NextPostingId(), tx.Id, accountId, amount, currency);
            tx.Postings.Add(posting);
            signed += amount;
        }
        if (tx.Postings.Count < 2)
            throw new LedgerBalanceException("A transaction must have at least two legs.");
        if (signed != 0)
            throw new LedgerBalanceException(
                $"Transaction '{description}' is not balanced. Signed sum is {signed}.");
        _transactions.Add(tx);
        return tx;
    }

    public long BalanceOf(long accountId) => AccountMath.Balance(AllPostings(), GetAccount(accountId));

    public long BalanceOf(string accountCode) => AccountMath.Balance(AllPostings(), GetAccountByCode(accountCode));

    public long NetWorth()
    {
        long assets = 0;
        long liabilities = 0;
        foreach (var account in _accountsById.Values)
        {
            long raw = _transactions.SelectMany(t => t.Postings)
                .Where(p => p.AccountId == account.Id)
                .Sum(p => p.AmountMinor);
            long normalBalance = account.IsDebitNormal ? raw : -raw;
            if (account.Type == AccountType.Asset) assets += normalBalance;
            else if (account.Type == AccountType.Liability) liabilities += normalBalance;
        }
        return assets - liabilities;
    }

    public void AttachTransaction(Transaction tx)
    {
        if (tx is null) throw new ArgumentNullException(nameof(tx));
        if (!tx.IsBalanced)
            throw new LedgerBalanceException($"Attached transaction '{tx.Description}' is not balanced.");
        _transactions.Add(tx);
        _nextTransactionId = Math.Max(_nextTransactionId, tx.Id + 1);
        foreach (var p in tx.Postings)
            _nextPostingId = Math.Max(_nextPostingId, p.Id + 1);
    }

    internal long NextTransactionId() => ++_nextTransactionId;
    internal long NextPostingId() => ++_nextPostingId;
    private IEnumerable<Posting> AllPostings() => _transactions.SelectMany(t => t.Postings);
}