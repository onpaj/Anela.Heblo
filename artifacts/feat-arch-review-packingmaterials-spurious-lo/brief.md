## Module
PackingMaterials

## Finding
`ConsumptionCalculationService.ProcessDailyConsumptionAsync` uses a fake log entry on an arbitrary material to record that a day has been processed:

```csharp
// backend/src/Anela.Heblo.Application/Features/PackingMaterials/Services/ConsumptionCalculationService.cs:70-74
// Relies on EF change tracking — GetAllWithAllocationsAsync must NOT use AsNoTracking
if (processedCount == 0 && materials.Count > 0)
{
    var marker = materials[0];
    marker.UpdateQuantity(marker.CurrentQuantity, processingDate, LogEntryType.AutomaticConsumption);
}
```

`HasDailyProcessingBeenRunAsync` (repository line 34-38) detects re-runs by checking for any `PackingMaterialLog` entry with `LogType == AutomaticConsumption` for the given date. When there are no invoices (i.e. no materials are consumed), no natural log entry is written — so the service mutates `materials[0]` with a zero net change purely to leave the sentinel entry behind.

Two concrete problems:

1. **Misleading audit data.** The log entry shows `OldQuantity == NewQuantity` for `materials[0]` — a manual reader of the log table cannot distinguish a real no-op update from this idempotency marker without knowing this implementation detail.

2. **Silent edge-case failure.** If the materials list is empty (stock not yet set up), the `if (materials.Count > 0)` guard prevents the marker from being written. `HasDailyProcessingBeenRunAsync` will return `false` the next time the job runs for that date, so the job executes again, finds no materials, and the cycle repeats indefinitely.

## Why it matters
- Audit log integrity is core to this module — operators rely on log entries to understand inventory changes. A phantom entry undermines that trust.
- The idempotency guarantee silently breaks in the empty-stock case, which could be a real scenario during onboarding.
- The invariant `"GetAllWithAllocationsAsync must NOT use AsNoTracking"` is a hidden, load-bearing constraint scattered in a comment — future refactoring of the repository could violate it silently.

## Suggested fix
Track daily run completion in a dedicated table or column, not in the log:

**Option A (minimal):** Add a `PackingMaterialDailyRun` table with `Date` (unique) and `ProcessedAt`. `HasDailyProcessingBeenRunAsync` queries this table instead of `PackingMaterialLog`. The service inserts one row after completing each day, regardless of whether any consumption occurred. Remove the marker hack.

**Option B (simpler if migrations are cheap):** Add a `bool WasProcessed` + `DateOnly LastProcessedDate` pair to a dedicated `PackingMaterialsSettings` singleton row. The semantics are explicit and require no marker entries.

Either approach removes the coupling between idempotency tracking and the audit log, and eliminates the edge-case failure.

---
_Filed by daily arch-review routine on 2026-05-20._