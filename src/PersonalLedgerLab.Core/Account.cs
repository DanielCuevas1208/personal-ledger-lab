namespace PersonalLedgerLab.Core;

/// <summary>
/// A ledger account. Postings move money between accounts.
/// </summary>
public sealed class Account
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AccountType Type { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public string? Note { get; set; }

    public Account() { }

    public Account(long id, string code, string name, AccountType type, string currencyCode, string? note = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Account code is required.", nameof(code));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Account name is required.", nameof(name));

        Id = id;
        Code = code;
        Name = name;
        Type = type;
        CurrencyCode = string.IsNullOrWhiteSpace(currencyCode) ? "USD" : currencyCode.ToUpperInvariant();
        Note = note;
    }

    public bool IsDebitNormal => Type.NormalSide() == NormalBalance.Debit;

    public override string ToString() => $"{Code} {Name} ({Type})";
}

/// <summary>
/// The five account types used in double-entry bookkeeping.
/// </summary>
public enum AccountType
{
    Asset = 0,
    Liability = 1,
    Equity = 2,
    Income = 3,
    Expense = 4
}

/// <summary>
/// The side that increases an account balance.
/// </summary>
public enum NormalBalance
{
    Debit = 1,
    Credit = -1
}

/// <summary>
/// Helpers that map an account type to its normal balance side.
/// </summary>
public static class AccountTypeExtensions
{
    /// <summary>
    /// Returns the side that increases the account balance.
    /// </summary>
    public static NormalBalance NormalSide(this AccountType type) => type switch
    {
        AccountType.Asset or AccountType.Expense => NormalBalance.Debit,
        AccountType.Liability or AccountType.Equity or AccountType.Income => NormalBalance.Credit,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown account type.")
    };
}