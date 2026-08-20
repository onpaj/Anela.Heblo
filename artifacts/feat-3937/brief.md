## Module / File
`backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/RefreshOrphanContacts/RefreshOrphanContactsHandler.cs`

## Coverage
Line coverage: 24.5% (filter threshold: 60%)

## What's not tested
1. **Remote contact null path** — when `_apiClient.GetConversationAsync` returns a response with `ContactId == null`, the handler increments `SkippedNoContactId` and continues. No test covers this.
2. **Local conversation not found** — when the EF query finds no local conversation matching the ID, the handler again increments `SkippedNoContactId` and continues. No test covers this distinct path.
3. **Per-item exception isolation** — when `_contactEnricher.EnrichContactAsync` or `_repository.UpsertConversationAsync` throws, the handler increments `Failed`, adds to `FailedIds`, and calls `_db.ChangeTracker.Clear()` before continuing. No test verifies: (a) the loop continues after a failure, (b) `ChangeTracker.Clear()` is called to prevent EF state corruption, or (c) the `Updated` counter is not incremented for the failed item.

## Why it matters
`ChangeTracker.Clear()` is the critical fix for EF tracking corruption across items. If it is ever removed, a failed item's partial EF state bleeds into the next item's save, potentially corrupting the database row. The skip counters drive the operational log summary — incorrect counting means silent misreporting of the backfill's progress.

## Suggested approach
Unit test with mocked `ISmartsuppRepository`, `ISmartsuppApiClient`, `ISmartsuppContactEnricher`, and an in-memory `ApplicationDbContext`:
- Case: GetConversationAsync returns null ContactId → SkippedNoContactId incremented, loop continues
- Case: EF finds no local conversation → SkippedNoContactId incremented
- Case: EnrichContactAsync throws → Failed incremented, FailedIds contains the ID, Updated not incremented, loop continues to next item
~2 h effort.

---
_Filed by weekly coverage-gap routine on 2026-08-17. Based on CI run #31804633307 (6f781d410eb84616c8decb088d6d18cd1de01fb8)._
