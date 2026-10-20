namespace PersonalLedgerLab.Core;

/// <summary>
/// A balanced exchange of value between two or more accounts.
/// </summary>
public sealed class Transaction
{
    public long Id { get; set; }
    public DateOnly Date { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? RuleExplanation { get; set; }
    public long? AppliedRuleId { get; set; }
    public string? SourceCurrencyCode { get; set; }
    public long? SourceAmountMinor { get; set; }
    public long? ExchangeRatePpm { get; set; }
    public long? RecurringTemplateId { get; set; }
    public DateOnly? RecurringOccurrenceDate { get; set; }
    public List<Posting> Postings { get; set; } = new();

    public Transaction() { }

    public Transaction(long id, DateOnly date, string description, string? reference, string source)
    {
        Id = id;
        Date = date;
        Description = description ?? string.Empty;
        Reference = reference;
        Source = source ?? string.Empty;
    }

    public bool IsBalanced => AccountMath.IsBalanced(Postings);

    public IEnumerable<Posting> PostingsByAccount(long accountId) =>
        Postings.Where(p => p.AccountId == accountId);

    public override string ToString() =>
        $"{Date:yyyy-MM-dd} {Description} ({Postings.Count} legs)";
}
