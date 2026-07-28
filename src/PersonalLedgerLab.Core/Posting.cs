using System.Globalization;

namespace PersonalLedgerLab.Core;

/// <summary>
/// Shared math and formatting helpers for minor-unit money.
/// </summary>
public static class AccountMath
{
    /// <summary>
    /// Returns the balance of an account from a sequence of postings.
    /// Debit-normal accounts sum raw amounts. Credit-normal accounts invert the sign.
    /// </summary>
    public static long Balance(IEnumerable<Posting> postings, Account account)
    {
        long sum = postings
            .Where(p => p.AccountId == account.Id)
            .Sum(p => p.AmountMinor);

        return account.IsDebitNormal ? sum : -sum;
    }

    /// <summary>
    /// Verifies that a set of postings has a zero signed sum.
    /// </summary>
    public static bool IsBalanced(IEnumerable<Posting> postings)
    {
        long sum = postings.Sum(p => p.AmountMinor);
        return sum == 0;
    }

    public static string FormatSigned(long minorUnits, string currency)
    {
        decimal major = minorUnits / 100m;
        string formatted = Math.Abs(major).ToString("0.00", CultureInfo.InvariantCulture);
        return $"{(minorUnits < 0 ? "-" : "+")}{formatted} {currency}";
    }
}

/// <summary>
/// One leg of a balanced transaction. A positive amount is a debit.
/// A negative amount is a credit. The sum of postings in a transaction is zero.
/// </summary>
public sealed class Posting
{
    public long Id { get; set; }
    public long TransactionId { get; set; }
    public long AccountId { get; set; }
    public long AmountMinor { get; set; }
    public string CurrencyCode { get; set; } = "USD";

    public Posting() { }

    public Posting(long id, long transactionId, long accountId, long amountMinor, string currencyCode)
    {
        Id = id;
        TransactionId = transactionId;
        AccountId = accountId;
        AmountMinor = amountMinor;
        CurrencyCode = string.IsNullOrWhiteSpace(currencyCode)
            ? "USD"
            : currencyCode.ToUpperInvariant();
    }

    public bool IsDebit => AmountMinor >= 0;
    public bool IsCredit => AmountMinor < 0;
    public Money Amount => new(AmountMinor, CurrencyCode);

    public override string ToString() =>
        $"{(IsDebit ? "Dr" : "Cr")} {AccountMath.FormatSigned(AmountMinor, CurrencyCode)}";
}