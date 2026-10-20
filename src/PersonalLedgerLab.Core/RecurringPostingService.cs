namespace PersonalLedgerLab.Core;

/// <summary>
/// Generates monthly occurrences and builds balanced scheduled transactions.
/// </summary>
public sealed class RecurringPostingService
{
    public IReadOnlyList<RecurringOccurrence> Preview(
        IReadOnlyList<RecurringTransaction> templates,
        IReadOnlyList<Transaction> transactions,
        DateOnly from,
        DateOnly through)
    {
        if (templates is null) throw new ArgumentNullException(nameof(templates));
        if (transactions is null) throw new ArgumentNullException(nameof(transactions));
        if (from > through)
            throw new ArgumentException("The preview start date must be on or before the end date.");

        var posted = transactions
            .Where(t => t.RecurringTemplateId.HasValue && t.RecurringOccurrenceDate.HasValue)
            .GroupBy(t => (t.RecurringTemplateId!.Value, t.RecurringOccurrenceDate!.Value))
            .ToDictionary(g => g.Key, g => g.OrderBy(t => t.Id).First());

        var occurrences = new List<RecurringOccurrence>();
        foreach (var template in templates.OrderBy(t => t.Id))
        {
            template.Validate();
            foreach (var date in DatesForMonth(template.DayOfMonth, from, through))
            {
                var key = (template.Id, date);
                var existing = posted.GetValueOrDefault(key);
                occurrences.Add(new RecurringOccurrence(
                    template.Id,
                    date,
                    template.Name,
                    template.Description,
                    template.Reference,
                    template.DebitAccountId,
                    template.CreditAccountId,
                    template.AmountMinor,
                    template.CurrencyCode,
                    existing is not null,
                    existing?.Id));
            }
        }

        return occurrences
            .OrderBy(o => o.Date)
            .ThenBy(o => o.TemplateId)
            .ToList();
    }

    public Transaction BuildTransaction(RecurringTransaction template, DateOnly date)
    {
        if (template is null) throw new ArgumentNullException(nameof(template));
        template.Validate();

        var transaction = new Transaction(
            0,
            date,
            template.Description,
            template.Reference,
            "scheduled-posting")
        {
            RecurringTemplateId = template.Id,
            RecurringOccurrenceDate = date
        };
        transaction.Postings.Add(new Posting(
            0,
            0,
            template.DebitAccountId,
            template.AmountMinor,
            template.CurrencyCode));
        transaction.Postings.Add(new Posting(
            0,
            0,
            template.CreditAccountId,
            -template.AmountMinor,
            template.CurrencyCode));
        return transaction;
    }

    public RecurringPostResult PostSelected(
        IReadOnlyList<RecurringTransaction> templates,
        IReadOnlyList<Transaction> transactions,
        IEnumerable<RecurringOccurrence> selectedOccurrences,
        Func<Transaction, long> save)
    {
        if (templates is null) throw new ArgumentNullException(nameof(templates));
        if (transactions is null) throw new ArgumentNullException(nameof(transactions));
        if (selectedOccurrences is null) throw new ArgumentNullException(nameof(selectedOccurrences));
        if (save is null) throw new ArgumentNullException(nameof(save));

        var templatesById = templates.ToDictionary(t => t.Id);
        var postedKeys = transactions
            .Where(t => t.RecurringTemplateId.HasValue && t.RecurringOccurrenceDate.HasValue)
            .Select(t => (t.RecurringTemplateId!.Value, t.RecurringOccurrenceDate!.Value))
            .ToHashSet();
        var selectedKeys = new HashSet<(long TemplateId, DateOnly Date)>();
        var transactionIds = new List<long>();
        int postedCount = 0;
        int alreadyPostedCount = 0;

        foreach (var occurrence in selectedOccurrences.OrderBy(o => o.Date).ThenBy(o => o.TemplateId))
        {
            var key = (occurrence.TemplateId, occurrence.Date);
            if (!selectedKeys.Add(key))
                continue;
            if (!templatesById.TryGetValue(occurrence.TemplateId, out var template))
                throw new KeyNotFoundException($"Recurring transaction {occurrence.TemplateId} was not found.");

            if (postedKeys.Contains(key))
            {
                alreadyPostedCount++;
                continue;
            }

            var transaction = BuildTransaction(template, occurrence.Date);
            long id = save(transaction);
            postedKeys.Add(key);
            transactionIds.Add(id);
            postedCount++;
        }

        return new RecurringPostResult(postedCount, alreadyPostedCount, transactionIds);
    }

    private static IEnumerable<DateOnly> DatesForMonth(int dayOfMonth, DateOnly from, DateOnly through)
    {
        var month = new DateOnly(from.Year, from.Month, 1);
        var lastMonth = new DateOnly(through.Year, through.Month, 1);

        while (month <= lastMonth)
        {
            int day = Math.Min(dayOfMonth, DateTime.DaysInMonth(month.Year, month.Month));
            var occurrence = new DateOnly(month.Year, month.Month, day);
            if (occurrence >= from && occurrence <= through)
                yield return occurrence;

            if (month.Year == 9999 && month.Month == 12)
                yield break;
            month = month.AddMonths(1);
        }
    }
}
