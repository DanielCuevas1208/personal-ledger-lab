using System.Globalization;

namespace PersonalLedgerLab.Core;

/// <summary>
/// An exchange rate between two currencies on one effective date.
/// RatePpm stores how many to-currency major units equal one from-currency major unit,
/// scaled by one million. One EUR at 1.085 USD is stored as 1_085_000.
/// </summary>
public sealed class ExchangeRate
{
    public long Id { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public string FromCurrency { get; set; } = string.Empty;
    public string ToCurrency { get; set; } = string.Empty;
    public long RatePpm { get; set; }

    public ExchangeRate() { }

    public ExchangeRate(long id, DateOnly effectiveDate, string fromCurrency, string toCurrency, long ratePpm)
    {
        Id = id;
        EffectiveDate = effectiveDate;
        FromCurrency = NormalizeCurrency(fromCurrency);
        ToCurrency = NormalizeCurrency(toCurrency);
        RatePpm = ratePpm;
    }

    public decimal RateAsDecimal => RatePpm / (decimal)ExchangeRateMath.PpmScale;

    public override string ToString() =>
        $"{EffectiveDate:yyyy-MM-dd} 1 {FromCurrency} = {RateAsDecimal:0.000000} {ToCurrency}";

    private static string NormalizeCurrency(string code) =>
        string.IsNullOrWhiteSpace(code) ? "USD" : code.ToUpperInvariant();
}

/// <summary>
/// Deterministic conversion helpers for exchange rates stored as parts per million.
/// </summary>
public static class ExchangeRateMath
{
    public const long PpmScale = 1_000_000L;
    public const long IdentityRatePpm = PpmScale;

    public static long FromDecimal(decimal rate) =>
        (long)Math.Round(rate * PpmScale, MidpointRounding.ToEven);

    public static decimal ToDecimal(long ratePpm) => ratePpm / (decimal)PpmScale;

    public static long ConvertMinor(long fromMinor, long ratePpm) =>
        (long)Math.Round(fromMinor * (decimal)ratePpm / PpmScale, MidpointRounding.ToEven);

    public static long InvertRatePpm(long ratePpm)
    {
        if (ratePpm <= 0)
            throw new ArgumentOutOfRangeException(nameof(ratePpm), "The exchange rate must be positive.");
        return (long)Math.Round((decimal)PpmScale * PpmScale / ratePpm, MidpointRounding.ToEven);
    }
}

/// <summary>
/// Looks up exchange rates and converts minor-unit amounts between currencies.
/// </summary>
public sealed class ExchangeRateService
{
    public ExchangeRateLookupResult FindRate(
        IReadOnlyList<ExchangeRate> rates,
        DateOnly asOfDate,
        string fromCurrency,
        string toCurrency)
    {
        if (rates is null) throw new ArgumentNullException(nameof(rates));
        string from = Normalize(fromCurrency);
        string to = Normalize(toCurrency);

        if (string.Equals(from, to, StringComparison.Ordinal))
        {
            return new ExchangeRateLookupResult(
                new ExchangeRate(0, asOfDate, from, to, ExchangeRateMath.IdentityRatePpm),
                "Same currency. No conversion was applied.",
                false);
        }

        var direct = rates
            .Where(r => string.Equals(r.FromCurrency, from, StringComparison.Ordinal)
                        && string.Equals(r.ToCurrency, to, StringComparison.Ordinal)
                        && r.EffectiveDate <= asOfDate)
            .OrderByDescending(r => r.EffectiveDate)
            .ThenByDescending(r => r.Id)
            .FirstOrDefault();
        if (direct is not null)
        {
            return new ExchangeRateLookupResult(
                direct,
                $"Used rate {direct.RateAsDecimal:0.000000} {to} per 1 {from} on {direct.EffectiveDate:yyyy-MM-dd}.",
                false);
        }

        var inverse = rates
            .Where(r => string.Equals(r.FromCurrency, to, StringComparison.Ordinal)
                        && string.Equals(r.ToCurrency, from, StringComparison.Ordinal)
                        && r.EffectiveDate <= asOfDate)
            .OrderByDescending(r => r.EffectiveDate)
            .ThenByDescending(r => r.Id)
            .FirstOrDefault();
        if (inverse is not null)
        {
            long invertedPpm = ExchangeRateMath.InvertRatePpm(inverse.RatePpm);
            var synthetic = new ExchangeRate(0, inverse.EffectiveDate, from, to, invertedPpm);
            return new ExchangeRateLookupResult(
                synthetic,
                $"Used inverted rate {synthetic.RateAsDecimal:0.000000} {to} per 1 {from} from {inverse.EffectiveDate:yyyy-MM-dd}.",
                true);
        }

        throw new InvalidOperationException(
            $"No exchange rate exists from {from} to {to} on or before {asOfDate:yyyy-MM-dd}.");
    }

    public long ConvertMinor(long fromMinor, ExchangeRate rate) =>
        ExchangeRateMath.ConvertMinor(fromMinor, rate.RatePpm);

    private static string Normalize(string code) =>
        string.IsNullOrWhiteSpace(code) ? "USD" : code.ToUpperInvariant();
}

/// <summary>
/// The exchange rate chosen for one conversion.
/// </summary>
public sealed class ExchangeRateLookupResult
{
    public ExchangeRateLookupResult(ExchangeRate rate, string explanation, bool inverted)
    {
        Rate = rate ?? throw new ArgumentNullException(nameof(rate));
        Explanation = explanation ?? string.Empty;
        Inverted = inverted;
    }

    public ExchangeRate Rate { get; }
    public string Explanation { get; }
    public bool Inverted { get; }
}
