# [coverage-gap] Packaging/PackingStatsTile: Shoptet API failure isolation path untested

## Module / File
`backend/src/Anela.Heblo.Application/Features/Packaging/DashboardTiles/PackingStatsTile.cs`

## Coverage
Line coverage: 0% (filter threshold: 60%)

## What's not tested
The tile has two distinct failure modes that are never exercised:

**Shoptet API failure (inner catch, lines 52–60):** When `GetOrdersBeingPackedCountAsync` or `GetOrdersBeingProcessedCountAsync` throws, the counts stay `null` and the overall tile response is still `{status: "success"}` with `ordersBeingPackedCount: null`. This is the intended graceful degradation — the tile should still show "packed today" data even when Shoptet is unreachable.

**Repository failure (outer catch):** When `GetPackedTodayByPackerAsync` throws, the tile returns `{status: "error"}`. This is a total failure, not degradation.

No test currently verifies that these two paths produce different shapes, or that the Shoptet failure is truly isolated (i.e., the packer breakdown data still appears).

## Why it matters
If someone refactors the inner try-catch away, or promotes the Shoptet exception to the outer scope, `ordersBeingPackedCount` would change from `null` to an error response — a breaking API contract change the dashboard client would not handle correctly.

## Suggested approach
Two unit tests with mocked dependencies (≈ low effort):
1. `GetPackedTodayByPackerAsync` returns data; Shoptet client throws — verify response status is `"success"`, `ordersBeingPackedCount` is `null`, and packer list is populated.
2. `GetPackedTodayByPackerAsync` throws — verify response status is `"error"`.

---
_Filed by weekly coverage-gap routine on 2026-07-20. Based on CI run #29525794843 (bba537b141de1dba71a2c6853c4ff3f7e96153b2)._
