# Implementation: update-architecture-boundary-allowlist

## What was implemented
Removed the now-stale `ProductPairingDqtComparer -> ICatalogResilienceService` entry from the `DataQualityCatalogAllowlist` in the reflection-based architecture-boundary test, and trimmed the explanatory comment above the first four remaining entries so it no longer references the resilience wrapping that was removed in the prior task (switch-product-pairing-comparer-to-dqt-resilience-contract). The four genuinely out-of-scope entries (`IEshopStockClient`, `IErpStockClient`, `ErpStock`, `ProductType`) and the `EshopStock` compiler-generated-state-machine entry were left untouched, as was the block comment documenting the `IProductPairingQuery` follow-up.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — removed the `ICatalogResilienceService` line from `DataQualityCatalogAllowlist` (was lines 151-157, now lines 149-160) and shortened the comment above the first entry from "reads eshop/erp catalog clients to compare product pairing, wrapped in ICatalogResilienceService for transient-fault protection." to "reads eshop/erp catalog clients to compare product pairing."

## Tests
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — the reflection-based `"DataQuality -> Catalog"` rule (and 26 other architecture rules in the same file) verified: 27/27 passed, confirming `ProductPairingDqtComparer` no longer references `ICatalogResilienceService` or any other non-allowlisted Catalog Application-layer type.

## How to verify
1. `cd backend` (or repo root) and run `dotnet build Anela.Heblo.sln` — build succeeds, 0 errors.
2. `dotnet test Anela.Heblo.sln --no-build --filter "FullyQualifiedName~ModuleBoundariesTests"` — 27 passed, 0 failed.
3. `grep -n "ICatalogResilienceService" backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — returns no matches.
4. `dotnet format Anela.Heblo.sln --verify-no-changes` — exits 0, no formatting issues.

## Notes
Ran the full solution test suite (`dotnet test Anela.Heblo.sln --no-build`) as instructed. It reported failures in `Anela.Heblo.Tests`, `Anela.Heblo.Adapters.Flexi.Tests`, and `Anela.Heblo.Adapters.Shoptet.Tests`, but every failure is a pre-existing sandbox/environment limitation unrelated to this change:
- Tests using Testcontainers (Postgres) fail with "Docker is either not running or misconfigured" — no Docker daemon available in this sandbox.
- Flexi/Shoptet integration tests fail because they require a live connection to external Flexi/Shoptet APIs, which aren't reachable from this sandbox.
Grepped the failure list for `ProductPairing|ModuleBoundaries|DataQuality` — zero matches, confirming none of the failures relate to this change or the architecture-boundary rules. The scoped `ModuleBoundariesTests` filter run (step 3) passed cleanly with 27/27, which is the authoritative verification for this task.
An unrelated pre-existing modification to `artifacts/feat-3454/state.json` was present in the worktree before this task started; it was left unstaged/uncommitted per the "surgical changes" rule, since the task only specifies committing `ModuleBoundariesTests.cs`.

## PR Summary
This change is a small cleanup that removes a stale allowlist entry from the `ModuleBoundariesTests` architecture-boundary suite. The `DataQuality -> Catalog` allowlist previously permitted `ProductPairingDqtComparer` to reference `Anela.Heblo.Application.Features.Catalog.Infrastructure.ICatalogResilienceService`, a dependency introduced when the comparer used a resilience wrapper around catalog clients. A prior task in this same feature branch replaced that Catalog-owned resilience contract with a DataQuality-owned equivalent, so `ProductPairingDqtComparer` no longer touches `ICatalogResilienceService` at all — leaving the allowlist entry in place would have been misleading and contrary to the file's convention of removing entries once the underlying violation is fixed. The explanatory comment above the entry was also trimmed to drop the now-inaccurate reference to resilience wrapping. Verified via the full `ModuleBoundariesTests` suite (27/27 passing), a grep confirming zero remaining references to `ICatalogResilienceService` in the file, a clean `dotnet format --verify-no-changes`, and a full-solution test run (failures observed there are pre-existing sandbox limitations — no Docker daemon and no live Flexi/Shoptet API access — unrelated to this change).

## Status
DONE
