# Implementation: retire-dataquality-catalog-allowlist

## What was implemented

Emptied the `DataQualityCatalogAllowlist` in the architecture boundary test, removing all
five explicit entries that previously allowed `ProductPairingDqtComparer` to reference
`Anela.Heblo.Domain.Features.Catalog.*` types directly (`IEshopStockClient`, `IErpStockClient`,
`ErpStock`, `ProductType`, `EshopStock`). These entries are now resolved: an earlier task in
this feature (`rewire-product-pairing-comparer`) rewired `ProductPairingDqtComparer` onto the
DataQuality-owned `IDqtEshopStockSource`/`IDqtErpStockSource` contracts, with the Catalog-side
implementations (`DataQualityEshopStockSourceAdapter`, `DataQualityErpStockSourceAdapter`)
living in `Catalog.Infrastructure`. With that decoupling done, no allowlist entries are needed
any more for the `DataQuality -> Catalog` boundary, mirroring the "Empty — ..." comment style
already used elsewhere in this file for other retired allowlists (e.g. `LeafletAllowlist`,
`ArticleAllowlist`).

## Files created/modified

- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — replaced the
  populated `DataQualityCatalogAllowlist` (comment + 5 entries) with an empty
  `HashSet<string>` and an updated comment explaining why it is now empty.

## Tests

No new tests were added — this task only retires stale entries in an existing architecture
test. The existing `ModuleBoundariesTests` theory (parameterized over module boundary pairs,
including `"DataQuality -> Catalog"`) is the verification mechanism itself: it fails if any
disallowed reference reappears against the now-empty allowlist.

## How to verify

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ModuleBoundariesTests"
```

Ran this and confirmed: `Passed! - Failed: 0, Passed: 35, Skipped: 0, Total: 35` — all
`ModuleBoundaryRule` theory cases pass, including `"DataQuality -> Catalog"` with the emptied
allowlist, confirming `ProductPairingDqtComparer` no longer references any Catalog-namespaced
type.

## Notes

Straightforward mechanical change exactly as specified in the task context — no deviations.
The full solution's test project had to cold-build in this worktree (no prior `obj`/`bin` for
`Anela.Heblo.Tests`), which is why the verification run took a long time; the test run itself
completed in under 500ms once the build finished. No behavioral or production code changed —
only test-file bookkeeping that reflects the already-completed decoupling from earlier tasks.

## PR Summary
Retires the last vestige of the `DataQuality -> Catalog` allowlist in `ModuleBoundariesTests`. The allowlist existed to tolerate `ProductPairingDqtComparer` reaching directly into `Catalog` domain types; earlier tasks in this feature moved it onto DataQuality-owned `IDqtEshopStockSource`/`IDqtErpStockSource` contracts backed by Catalog-side adapters, so the exemption is no longer needed. Emptying the allowlist turns the architecture test into a permanent guard against that leak reappearing.

### Changes
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — emptied `DataQualityCatalogAllowlist`, replacing the resolved entries with an explanatory "Empty — ..." comment

## Status
DONE
