using PersonalLedgerLab.Core;

namespace PersonalLedgerLab.Tests;

public class RuleEngineTests
{
    private readonly Account _salary = new(1, "4000", "Salary", AccountType.Income, "USD");
    private readonly Account _groceries = new(2, "5000", "Groceries", AccountType.Expense, "USD");
    private readonly Account _coffee = new(3, "5200", "Coffee", AccountType.Expense, "USD");
    private readonly Account _transport = new(4, "5300", "Transport", AccountType.Expense, "USD");

    private List<CategoryRule> Rules() => new()
    {
        new(1, "Coffee", 20, RuleMatchType.DescriptionContains, "coffee", 3, "5200"),
        new(2, "Groceries", 10, RuleMatchType.DescriptionContains, "supervalu", 2, "5000"),
        new(3, "Transport", 30, RuleMatchType.DescriptionRegex, "(uber|metro)", 4, "5300"),
        new(4, "Payroll", 5, RuleMatchType.ReferenceEquals, "PAYROLL", 1, "4000")
    };

    private static Transaction Tx(string description, string? reference) =>
        new(0, new DateOnly(2025, 1, 1), description, reference, "test");

    [Fact]
    public void Higher_priority_lower_number_wins()
    {
        var engine = new RuleEngine();
        var result = engine.Categorize(Tx("supervalu coffee", null), Rules());
        Assert.True(result.Matched);
        Assert.Equal(2, result.AppliedRuleId);
    }

    [Fact]
    public void Reference_match_is_case_insensitive()
    {
        var engine = new RuleEngine();
        var result = engine.Categorize(Tx("Anything", "payroll"), Rules());
        Assert.True(result.Matched);
        Assert.Equal(4, result.AppliedRuleId);
    }

    [Fact]
    public void Regex_match_distinguishes_terms()
    {
        var engine = new RuleEngine();
        var metro = engine.Categorize(Tx("City Metro monthly", null), Rules());
        var other = engine.Categorize(Tx("Barber shop", null), Rules());
        Assert.True(metro.Matched);
        Assert.Equal(3, metro.AppliedRuleId);
        Assert.False(other.Matched);
    }

    [Fact]
    public void No_match_returns_explainable_result()
    {
        var engine = new RuleEngine();
        var result = engine.Categorize(Tx("Unknown shop", null), Rules());
        Assert.False(result.Matched);
        Assert.Contains("No rule matched 'Unknown shop'", result.Explanation);
    }

    [Fact]
    public void Each_decision_includes_human_readable_reason()
    {
        var engine = new RuleEngine();
        var result = engine.Categorize(Tx("SuperValu groceries", null), Rules());
        Assert.Contains("Groceries", result.Explanation);
        Assert.Contains("supervalu", result.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("5000", result.Explanation);
    }

    [Fact]
    public void Invalid_regex_does_not_break_engine()
    {
        var rules = new List<CategoryRule>
        {
            new(9, "Bad", 1, RuleMatchType.DescriptionRegex, "(", 2, "5000")
        };
        var engine = new RuleEngine();
        var result = engine.Categorize(Tx("Whatever", null), rules);
        Assert.False(result.Matched);
    }

    [Fact]
    public void CategorizeMany_preserves_order()
    {
        var txs = new List<Transaction>
        {
            Tx("Coffee Time", null),
            Tx("SuperValu", null),
            Tx("Unknown", null)
        };
        var engine = new RuleEngine();
        var results = engine.CategorizeMany(txs, Rules());
        Assert.Equal(3, results.Count);
        Assert.True(results[0].Matched);
        Assert.True(results[1].Matched);
        Assert.False(results[2].Matched);
    }
}