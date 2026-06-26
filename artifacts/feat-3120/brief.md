## Module / File
backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Stock/FlexiStockTakingDomainService.cs

## Coverage
Line coverage: 16.7% (filter threshold: 60%)

## What's not tested
`FlexiStockTakingDomainService.SubmitStockTakingAsync` has four independent decision points, none of which are covered by tests:

1. **`SoftStockTaking` branch**: When `order.SoftStockTaking == true`, the method skips all ERP calls and returns a record where `AmountNew == AmountOld`. The real ERP path (creating a header, adding items, submitting) is also untested.
2. **`RemoveMissingLots` conditional**: When true, fetches current items and adds missing lots to the ERP document — this external-call sequence is untested.
3. **`DryRun` conditional**: When true, skips the `SubmitAsync` call, meaning the ERP document is created but not finalized. The dryrun vs. real submit distinction is never asserted.
4. **Exception path**: When any ERP call throws, the catch block returns a `StockTakingRecord` with an `Error` property set instead of re-throwing. Callers that check only for null (not for `Error != null`) would silently miss the failure.

## Why it matters
Stock taking is a write operation against a live ERP (FlexiBee). The silent `catch` at line 106 is especially risky: an exception from `CreateHeaderAsync` or `SubmitAsync` currently produces a record indistinguishable from a soft-stock-taking result — `AmountOld` is set to the requested amount and the `Error` field contains the message. If callers don't inspect `Error`, failed stock takes are silently treated as successful soft takes.

## Suggested approach
Unit tests mocking `IStockTakingClient` / `IStockTakingItemsClient`:
1. `SoftStockTaking=true` → no ERP calls, returned record has `AmountNew == AmountOld`
2. `SoftStockTaking=false, DryRun=false` → `SubmitAsync` called once
3. `SoftStockTaking=false, DryRun=true` → `SubmitAsync` NOT called
4. `RemoveMissingLots=true` → `AddMissingLotsAsync` called after adding items
5. ERP throws → returned record has `Error` field set (not null/empty) and no repository save attempt

Effort: ~2–3h.

---
_Filed by weekly coverage-gap routine on 2026-06-15. Based on CI run #27416879267 (3a6b7f99ee715caf82fc0efa17de5a5ede7b46fb)._