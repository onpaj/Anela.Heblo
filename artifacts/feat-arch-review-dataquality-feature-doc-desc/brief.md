## Module
DataQuality

## Finding
`docs/features/data-quality-dqt.md` (lines 34–36) documents the invoice DQT job as weekly:

> **Automatic**: every Monday at 23:00 CEST via Hangfire recurring job (`InvoiceDqtJob`)
> …
> The weekly job compares the previous 7 days by default.

The actual implementation in `InvoiceDqtJob` (`backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/InvoiceDqtJob.cs`, lines 15–22) contradicts this on every dimension:

```csharp
CronExpression = "0 5 * * *",  // Daily at 5:00 AM — not weekly/Monday at 23:00
Description = "Compares issued invoices between Shoptet and ABRA Flexi for the previous day"
```

And the job body (lines 44–45) confirms the single-day window:

```csharp
var yesterday = DateOnly.FromDateTime(DateTime.Today.AddDays(-1));
var run = DqtRun.Start(DqtTestType.IssuedInvoiceComparison, yesterday, yesterday, DqtTriggerType.Scheduled);
```

## Why it matters
- **Operational confusion**: anyone reading the doc to understand when the job runs, or how wide a date range it covers, will reach wrong conclusions. This matters for debugging missing results or scheduling manual re-runs.
- **Stale documentation as a hazard**: the discrepancy suggests the schedule was changed (from weekly to daily) but the doc was not updated. As the gap widens over time it becomes harder to trust any part of the feature doc.

## Suggested fix
Update `docs/features/data-quality-dqt.md` — Schedule section — to reflect the actual behaviour:

```
- **Automatic**: daily at 05:00 UTC via Hangfire recurring job (`InvoiceDqtJob`)
- Each run covers the previous calendar day.
- **Manual trigger**: `POST /api/data-quality/runs`
```

Also update the "Known constraints" bullet that mentions "The weekly job compares the previous 7 days by default."

---
_Filed by daily arch-review routine on 2026-06-02._