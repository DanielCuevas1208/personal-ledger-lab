using System.Globalization;

namespace PersonalLedgerLab.Core;

/// <summary>
/// Represents a money amount stored as minor currency units.
/// </summary>
public readonly struct Money : IEquatable<Money>, IComparable<Money>
{
    public long MinorUnits { get; }
    public string CurrencyCode { get; }

    public Money(long minorUnits, string currencyCode)
    {
        if (string.IsNullOrWhiteSpace(currencyCode))
        {
            throw new ArgumentException("Currency code is required.", nameof(currencyCode));
        }
        if (currencyCode.Length != 3)
        {
            throw new ArgumentException("Currency code must have three letters.", nameof(currencyCode));
        }
        MinorUnits = minorUnits;
        CurrencyCode = currencyCode.ToUpperInvariant();
    }

    public static Money Zero(string currency) => new(0, currency);

    public static Money Of(decimal major, string currency)
    {
        long minor = (long)Math.Round(major * 100m, MidpointRounding.ToEven);
        return new Money(minor, currency);
    }

    public decimal ToMajor() => MinorUnits / 100m;

    public static Money operator +(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return new Money(a.MinorUnits + b.MinorUnits, a.CurrencyCode);
    }

    public static Money operator -(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return new Money(a.MinorUnits - b.MinorUnits, a.CurrencyCode);
    }

    public static bool operator ==(Money a, Money b) => a.Equals(b);
    public static bool operator !=(Money a, Money b) => !a.Equals(b);

    public bool Equals(Money other) =>
        MinorUnits == other.MinorUnits && CurrencyCode == other.CurrencyCode;

    public override bool Equals(object? obj) => obj is Money other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(MinorUnits, CurrencyCode);
    public int CompareTo(Money other) => MinorUnits.CompareTo(other.MinorUnits);

    public override string ToString() =>
        (MinorUnits / 100m).ToString("0.00", CultureInfo.InvariantCulture) + " " + CurrencyCode;

    private static void EnsureSameCurrency(Money a, Money b)
    {
        if (!string.Equals(a.CurrencyCode, b.CurrencyCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Cannot combine different currencies: {a.CurrencyCode} and {b.CurrencyCode}.");
        }
    }
}