# PR Context

- **PR**: #3060 — feat: Harden ShoptetStockClient.ListAsync against transient HTTP failures
- **URL**: https://github.com/onpaj/Anela.Heblo/pull/3060
- **Branch**: `feat-telemetry-shoptetstockclient-listasync-8` → `main`
- **State**: open
- **Author**: onpaj
- **Changes**: +2067 / -18 across 15 files
- **Absorbed**: backmerged with `main`, all tests passing (4939 passed, 4 skipped), pushed

## Description

Hardens `ShoptetStockClient.ListAsync` against transient HTTP failures (~1.1 failures/day).
Adds Polly retry policy, per-attempt timeout, token redaction in logs, and wraps
`ProductPairingDqtComparer` stock calls with `ICatalogResilienceService`.

Closes #3000.

## Absorb notes

- Backmerged `origin/main` (150-file merge, clean — no conflicts)
- Fixed: `ModuleBoundariesTests` (DataQuality -> Catalog rule) — updated `DataQualityCatalogAllowlist`
  to cover `ICatalogResilienceService` (new Catalog dep in `ProductPairingDqtComparer`) and
  replaced stale `<CompareAsync>d__5` EshopStock entry with a parent-type entry covering
  the new `d__6`/`b__6_1>d` compiler-generated types introduced by the resilience wrapper
- All CI-relevant tests pass locally (4939 passed, 4 skipped, Category!=Integration filter)
