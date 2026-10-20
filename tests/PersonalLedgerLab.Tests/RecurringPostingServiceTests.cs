using PersonalLedgerLab.Core;
using PersonalLedgerLab.Data;

namespace PersonalLedgerLab.Tests;

public sealed class RecurringPostingServiceTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"pll-recurring-{Guid.NewGuid():N}.sqlite");
    private readonly SqliteLedgerStore _store;

    public RecurringPostingServiceTests()
    {
        _store = new SqliteLedgerStore(_path);
    }

    [Fact]
    public void Day_31_preview_clamps_to_each_month_end()
    {
        var template = Template(day: 31, id: 7);
        var service = new RecurringPostingService();

        var result = service.Preview(
            new[] { template },
            Array.Empty<Transaction>(),
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 4, 30));

        Assert.Equal(
            new[]
            {
                new DateOnly(2025, 1, 31),
                new DateOnly(2025, 2, 28),
                new DateOnly(2025, 3, 31),
                new DateOnly(2025, 4, 30)
            },
            result.Select(o => o.Date));
    }

    [Fact]
    public void Day_29_preview_includes_a_leap_day()
    {
        var template = Template(day: 29, id: 8);
        var service = new RecurringPostingService();

        var leapYear = service.Preview(
            new[] { template },
            Array.Empty<Transaction>(),
            new DateOnly(2024, 2, 1),
            new DateOnly(2024, 2, 29));
        var commonYear = service.Preview(
            new[] { template },
            Array.Empty<Transaction>(),
            new DateOnly(2025, 2, 1),
            new DateOnly(2025, 2, 28));

        Assert.Equal(new[] { new DateOnly(2024, 2, 29) }, leapYear.Select(o => o.Date));
        Assert.Equal(new[] { new DateOnly(2025, 2, 28) }, commonYear.Select(o => o.Date));
    }

    [Fact]
    public void Preview_rejects_an_inverted_date_range()
    {
        var service = new RecurringPostingService();

        Assert.Throws<ArgumentException>(() => service.Preview(
            new[] { Template(day: 1, id: 9) },
            Array.Empty<Transaction>(),
            new DateOnly(2025, 2, 1),
            new DateOnly(2025, 1, 31)));
    }

    [Fact]
    public void Repeated_posting_is_idempotent_and_keeps_account_totals_stable()
    {
        var checking = _store.LoadAccounts().Single(a => a.Code == "1000");
        var utilities = _store.LoadAccounts().Single(a => a.Code == "5400");
        long templateId = _store.AddRecurringTransaction(new RecurringTransaction(
            0, "Test utilities", "Test utilities", "TEST-UTILITIES", 31,
            utilities.Id, checking.Id, 95_00L, "USD"));
        var template = _store.LoadRecurringTransactions().Single(t => t.Id == templateId);
        var service = new RecurringPostingService();
        var preview = service.Preview(
            new[] { template },
            _store.LoadTransactions(),
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 3, 31));

        var first = service.PostSelected(
            new[] { template },
            _store.LoadTransactions(),
            preview,
            _store.SaveTransaction);
        var transactionsAfterFirstPost = _store.LoadTransactions();
        long checkingBalanceAfterFirstPost = AccountMath.Balance(
            transactionsAfterFirstPost.SelectMany(t => t.Postings), checking);

        var second = service.PostSelected(
            new[] { template },
            transactionsAfterFirstPost,
            preview,
            _store.SaveTransaction);

        Assert.Equal(3, first.PostedCount);
        Assert.Equal(0, first.AlreadyPostedCount);
        Assert.Equal(0, second.PostedCount);
        Assert.Equal(3, second.AlreadyPostedCount);
        Assert.Equal(3, transactionsAfterFirstPost.Count);
        Assert.Equal(transactionsAfterFirstPost.Count, _store.LoadTransactions().Count);
        Assert.Equal(
            checkingBalanceAfterFirstPost,
            AccountMath.Balance(_store.LoadTransactions().SelectMany(t => t.Postings), checking));
        Assert.All(_store.LoadTransactions(), transaction => Assert.True(transaction.IsBalanced));

        var replay = service.Preview(
            new[] { template },
            _store.LoadTransactions(),
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 3, 31));
        Assert.All(replay, occurrence =>
        {
            Assert.True(occurrence.IsPosted);
            Assert.NotNull(occurrence.PostedTransactionId);
        });
    }

    private static RecurringTransaction Template(int day, long id) => new(
        id,
        "Monthly test",
        "Monthly test",
        null,
        day,
        1,
        2,
        10_00L,
        "USD");

    public void Dispose()
    {
        _store.Dispose();
        try { File.Delete(_path); } catch { }
    }
}
