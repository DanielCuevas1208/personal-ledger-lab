namespace PersonalLedgerLab.Core;

/// <summary>
/// A monthly transaction template. The day clamps to the last day of shorter months.
/// </summary>
public sealed class RecurringTransaction
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public int DayOfMonth { get; set; }
    public long DebitAccountId { get; set; }
    public long CreditAccountId { get; set; }
    public long AmountMinor { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public string? Note { get; set; }

    public RecurringTransaction() { }

    public RecurringTransaction(
        long id,
        string name,
        string description,
        string? reference,
        int dayOfMonth,
        long debitAccountId,
        long creditAccountId,
        long amountMinor,
        string currencyCode,
        string? note = null)
    {
        Id = id;
        Name = name;
        Description = description;
        Reference = reference;
        DayOfMonth = dayOfMonth;
        DebitAccountId = debitAccountId;
        CreditAccountId = creditAccountId;
        AmountMinor = amountMinor;
        CurrencyCode = currencyCode;
        Note = note;
        Validate();
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            throw new ArgumentException("A recurring transaction name is required.", nameof(Name));
        if (string.IsNullOrWhiteSpace(Description))
            throw new ArgumentException("A recurring transaction description is required.", nameof(Description));
        if (DayOfMonth is < 1 or > 31)
            throw new ArgumentOutOfRangeException(nameof(DayOfMonth), "The day of month must be between 1 and 31.");
        if (DebitAccountId <= 0)
            throw new ArgumentOutOfRangeException(nameof(DebitAccountId));
        if (CreditAccountId <= 0)
            throw new ArgumentOutOfRangeException(nameof(CreditAccountId));
        if (DebitAccountId == CreditAccountId)
            throw new ArgumentException("Debit and credit accounts must differ.");
        if (AmountMinor <= 0)
            throw new ArgumentOutOfRangeException(nameof(AmountMinor), "The amount must be positive.");
        if (string.IsNullOrWhiteSpace(CurrencyCode) || CurrencyCode.Length != 3)
            throw new ArgumentException("The currency code must have three letters.", nameof(CurrencyCode));

        CurrencyCode = CurrencyCode.ToUpperInvariant();
    }
}

/// <summary>
/// One generated occurrence, with its durable posting state.
/// </summary>
public sealed record RecurringOccurrence(
    long TemplateId,
    DateOnly Date,
    string Name,
    string Description,
    string? Reference,
    long DebitAccountId,
    long CreditAccountId,
    long AmountMinor,
    string CurrencyCode,
    bool IsPosted,
    long? PostedTransactionId);

/// <summary>
/// The result of posting a selected recurring preview.
/// </summary>
public sealed record RecurringPostResult(
    int PostedCount,
    int AlreadyPostedCount,
    IReadOnlyList<long> TransactionIds);
