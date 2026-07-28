namespace PersonalLedgerLab.Core;

/// <summary>
/// A persistence boundary for the ledger, implemented by the data layer.
/// </summary>
public interface ILedgerStore
{
    IReadOnlyList<Account> LoadAccounts();
    long AddAccount(Account account);
    bool UpdateAccount(Account account);

    IReadOnlyList<CategoryRule> LoadRules();
    long AddRule(CategoryRule rule);
    bool UpdateRule(CategoryRule rule);
    bool DeleteRule(long ruleId);

    IReadOnlyList<Transaction> LoadTransactions();
    long SaveTransaction(Transaction transaction);
    void ClearTransactions();

    string DatabasePath { get; }
    bool IsFreshDatabase { get; }
}