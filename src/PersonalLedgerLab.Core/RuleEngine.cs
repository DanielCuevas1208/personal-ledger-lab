using System.Globalization;
using System.Text.RegularExpressions;

namespace PersonalLedgerLab.Core;

/// <summary>
/// The outcome of attempting to categorize one transaction.
/// </summary>
public readonly struct RuleResult
{
    public bool Matched { get; }
    public long? AppliedRuleId { get; }
    public long? CategoryAccountId { get; }
    public string Explanation { get; }

    public RuleResult(bool matched, long? appliedRuleId, long? categoryAccountId, string explanation)
    {
        Matched = matched;
        AppliedRuleId = appliedRuleId;
        CategoryAccountId = categoryAccountId;
        Explanation = explanation ?? string.Empty;
    }

    public static RuleResult NoMatch(string transactionDescription) =>
        new(false, null, null,
            $"No rule matched '{transactionDescription}'. The transaction stays in the suspense account.");
}

/// <summary>
/// Applies categorization rules to transactions and explains each decision.
/// The engine is deterministic and has no side effects.
/// </summary>
public sealed class RuleEngine
{
    /// <summary>
    /// Finds the first rule that matches the transaction, ordered by priority then id.
    /// </summary>
    public RuleResult Categorize(Transaction transaction, IReadOnlyList<CategoryRule> rules)
    {
        if (transaction is null) throw new ArgumentNullException(nameof(transaction));
        if (rules is null) throw new ArgumentNullException(nameof(rules));

        var ordered = rules
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.Id)
            .ToList();

        foreach (var rule in ordered)
        {
            if (Matches(transaction, rule, out var detail))
            {
                string explanation = string.Format(
                    CultureInfo.InvariantCulture,
                    "Rule '{0}' (priority {1}) matched because {2}. The offset moves to account {3}.",
                    rule.Name,
                    rule.Priority,
                    detail,
                    string.IsNullOrEmpty(rule.CategoryAccountCode) ? $"id {rule.CategoryAccountId}" : rule.CategoryAccountCode);

                return new RuleResult(true, rule.Id, rule.CategoryAccountId, explanation);
            }
        }

        return RuleResult.NoMatch(transaction.Description);
    }

    /// <summary>
    /// Tests every rule against every ordered transaction. Returns one result per transaction.
    /// </summary>
    public IReadOnlyList<RuleResult> CategorizeMany(
        IReadOnlyList<Transaction> transactions, IReadOnlyList<CategoryRule> rules) =>
        transactions.Select(t => Categorize(t, rules)).ToList();

    private static bool Matches(Transaction transaction, CategoryRule rule, out string detail)
    {
        bool matched;
        string formatted;
        switch (rule.MatchType)
        {
            case RuleMatchType.DescriptionContains:
                matched = transaction.Description.Contains(rule.MatchValue, StringComparison.OrdinalIgnoreCase);
                formatted = $"description '{transaction.Description}' contains '{rule.MatchValue}'";
                break;
            case RuleMatchType.DescriptionRegex:
                try
                {
                    matched = Regex.IsMatch(transaction.Description ?? string.Empty, rule.MatchValue,
                        RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
                    formatted = $"description '{transaction.Description}' fits '{rule.MatchValue}'";
                }
                catch (RegexMatchTimeoutException)
                {
                    matched = false;
                    formatted = $"regex '{rule.MatchValue}' timed out";
                }
                catch (ArgumentException)
                {
                    matched = false;
                    formatted = $"regex '{rule.MatchValue}' is invalid";
                }
                break;
            case RuleMatchType.ReferenceEquals:
                matched = string.Equals(transaction.Reference, rule.MatchValue, StringComparison.OrdinalIgnoreCase);
                formatted = $"reference '{transaction.Reference ?? ""}' equals '{rule.MatchValue}'";
                break;
            default:
                matched = false;
                formatted = $"unknown match type {rule.MatchType}";
                break;
        }

        detail = formatted;
        return matched;
    }
}