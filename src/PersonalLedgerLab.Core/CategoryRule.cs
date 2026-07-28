namespace PersonalLedgerLab.Core;

/// <summary>
/// The condition that a rule tests against a transaction.
/// </summary>
public enum RuleMatchType
{
    /// <summary>The transaction description contains a phrase (case-insensitive).</summary>
    DescriptionContains = 0,
    /// <summary>The transaction description matches a regular expression.</summary>
    DescriptionRegex = 1,
    /// <summary>The transaction reference equals a value (case-insensitive).</summary>
    ReferenceEquals = 2
}

/// <summary>
/// A categorization rule. Each rule maps a matched transaction to a category account.
/// Lower priority numbers apply first. Rules are pure data: they never change state.
/// </summary>
public sealed class CategoryRule
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Priority { get; set; }
    public RuleMatchType MatchType { get; set; }
    public string MatchValue { get; set; } = string.Empty;
    public long CategoryAccountId { get; set; }
    public string? CategoryAccountCode { get; set; }

    public CategoryRule() { }

    public CategoryRule(
        long id, string name, int priority, RuleMatchType matchType,
        string matchValue, long categoryAccountId, string? categoryAccountCode)
    {
        Id = id;
        Name = name ?? string.Empty;
        Priority = priority;
        MatchType = matchType;
        MatchValue = matchValue ?? string.Empty;
        CategoryAccountId = categoryAccountId;
        CategoryAccountCode = categoryAccountCode;
    }
}