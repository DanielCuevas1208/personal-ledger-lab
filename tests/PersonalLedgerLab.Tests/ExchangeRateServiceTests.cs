using PersonalLedgerLab.Core;

namespace PersonalLedgerLab.Tests;

public class ExchangeRateServiceTests
{
    private static readonly IReadOnlyList<ExchangeRate> SampleRates = new[]
    {
        new ExchangeRate(1, new DateOnly(2025, 1, 1), "EUR", "USD", ExchangeRateMath.FromDecimal(1.085000m)),
        new ExchangeRate(2, new DateOnly(2025, 1, 15), "EUR", "USD", ExchangeRateMath.FromDecimal(1.090000m))
    };

    [Fact]
    public void Same_currency_returns_identity_rate()
    {
        var service = new ExchangeRateService();
        var result = service.FindRate(SampleRates, new DateOnly(2025, 1, 10), "USD", "USD");
        Assert.Equal(ExchangeRateMath.IdentityRatePpm, result.Rate.RatePpm);
    }

    [Fact]
    public void Direct_rate_uses_latest_on_or_before_date()
    {
        var service = new ExchangeRateService();
        var result = service.FindRate(SampleRates, new DateOnly(2025, 1, 20), "EUR", "USD");
        Assert.Equal(1_090_000L, result.Rate.RatePpm);
    }

    [Fact]
    public void Inverse_rate_converts_usd_to_eur()
    {
        var service = new ExchangeRateService();
        var result = service.FindRate(SampleRates, new DateOnly(2025, 1, 10), "USD", "EUR");
        long eurMinor = service.ConvertMinor(108_50, result.Rate);
        Assert.Equal(100_00L, eurMinor);
    }

    [Fact]
    public void Convert_minor_rounds_to_even()
    {
        var service = new ExchangeRateService();
        var result = service.FindRate(SampleRates, new DateOnly(2025, 1, 10), "EUR", "USD");
        long usdMinor = service.ConvertMinor(100_00, result.Rate);
        Assert.Equal(108_50L, usdMinor);
    }

    [Fact]
    public void Missing_rate_throws()
    {
        var service = new ExchangeRateService();
        Assert.Throws<InvalidOperationException>(() =>
            service.FindRate(SampleRates, new DateOnly(2024, 12, 31), "EUR", "USD"));
    }
}
