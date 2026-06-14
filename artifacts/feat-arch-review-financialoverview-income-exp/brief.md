## Module
FinancialOverview

## Finding
The block that computes monthly income and expenses from ledger items is copy-pasted verbatim in three private methods of `FinancialAnalysisService`:

1. `RefreshMonthlyDataAsync` — `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs:219-232`
2. `GetHybridWithCurrentMonthAsync` — same file, lines 302-310
3. `GetFinancialOverviewRealTimeAsync` — same file, lines 505-521

In each location the same pattern appears: filter `debitItems` by `DebitAccountNumber?.StartsWith("5")`, filter `creditItems` by `CreditAccountNumber?.StartsWith("5")`, compute `expenses = debit5 - credit5`, then repeat for prefix `"6"` to get `income = credit6 - debit6`.

## Why it matters
This is a real maintenance risk: if the accounting convention changes (e.g. different account prefixes, or a sign-inversion fix) the change must be made in three places in sync. The method `GetFinancialOverviewRealTimeAsync` is already ~147 lines; the duplication inflates all three methods unnecessarily.

## Suggested fix
Extract a single private static method:

```csharp
private static (decimal income, decimal expenses) CalculatePeriodTotals(
    IEnumerable<LedgerItem> debitItems,
    IEnumerable<LedgerItem> creditItems)
{
    var debit5 = debitItems.Where(i => i.DebitAccountNumber?.StartsWith("5") == true).Sum(i => i.Amount);
    var credit5 = creditItems.Where(i => i.CreditAccountNumber?.StartsWith("5") == true).Sum(i => i.Amount);
    var expenses = debit5 - credit5;

    var credit6 = creditItems.Where(i => i.CreditAccountNumber?.StartsWith("6") == true).Sum(i => i.Amount);
    var debit6 = debitItems.Where(i => i.DebitAccountNumber?.StartsWith("6") == true).Sum(i => i.Amount);
    var income = credit6 - debit6;

    return (income, expenses);
}
```

Replace all three call sites with `var (income, expenses) = CalculatePeriodTotals(debitItems, creditItems);`. No behavioral change.

---
_Filed by daily arch-review routine on 2026-06-06._