# Implementation: module-boundary-enforcement

## What was implemented

Updated the reflection-based architecture test (`ModuleBoundariesTests.cs`) to pin the
`IEshopOrderClient` decoupling completed by the earlier tasks in this feature:

- Removed the now-stale `IEshopOrderClient` allowlist entries for `ScanPackingOrderHandler`
  and `CompletePackingOrderHandler` under `PackagingShoptetOrdersAllowlist` — both handlers
  no longer reference `IEshopOrderClient` after the `packaging-handlers` task.
- Updated the doc comment above `PackagingShoptetOrdersAllowlist` to reflect that Packaging
  now only legitimately consumes `IPackingOrderClient` (plus its DTOs), and explicitly calls
  out `IEshopOrderClient` as now-forbidden, consumed instead via Packaging's own
  `IPackedOrderStatusUpdater` contract.
- Added a new empty `ExpeditionListShoptetOrdersAllowlist` and a new
  `"ExpeditionList -> ShoptetOrders"` rule to `Rules()`, proving `PrintExpeditionOrderHandler`
  no longer references `Anela.Heblo.Application.Features.ShoptetOrders` at all (it now
  consumes the ExpeditionList-owned `IOrderStatusReader` contract).

## Files created/modified

- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — removed 2 stale
  allowlist entries, updated 1 doc comment, added 1 new allowlist field + 1 new
  `ModuleBoundaryRule` entry.

## Tests

`ModuleBoundariesTests` — all 37 `[Theory]` cases pass, including the new
`"ExpeditionList -> ShoptetOrders"` rule and the edited `"Packaging -> ShoptetOrders"` rule.

## How to verify

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ModuleBoundariesTests"
grep -rn "IEshopOrderClient" src/ --include=*.cs | grep -v "^src/Anela.Heblo.Application/Features/ShoptetOrders/" | grep -v "^src/Adapters/Anela.Heblo.Adapters.ShoptetApi/"
dotnet build   # (from repo root: dotnet build Anela.Heblo.sln)
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
dotnet format --verify-no-changes   # (from repo root: dotnet format Anela.Heblo.sln --verify-no-changes)
```

Results:
- `ModuleBoundariesTests`: 37/37 passed.
- Full solution build (`dotnet build Anela.Heblo.sln` from repo root — no `.sln`/`.csproj`
  exists directly under `backend/`, so the build/format commands were run against the
  solution at the repo root instead of literally `cd backend && dotnet build`/`dotnet format`
  as written in the task steps): 0 errors, 91 pre-existing nullable-reference warnings
  unrelated to this change.
- Full unfiltered test suite (`dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`):
  7287 passed, 110 failed, 4 skipped. All 110 failures are pre-existing integration/SQL-shape
  tests that require a live Postgres via Testcontainers/Docker (`System.ArgumentException:
  Docker is either not running or misconfigured...`) — this sandbox has no Docker daemon.
  None of the failures touch Packaging, ExpeditionList, ShoptetOrders, or
  `ModuleBoundariesTests`; this matches the environment limitation already implicit in the
  prior `expeditionlist-handler` task, which avoided the unfiltered suite for the same reason.
- `dotnet format Anela.Heblo.sln --verify-no-changes`: exit 0, no violations.
- The grep for stray `IEshopOrderClient` references outside `ShoptetOrders`/`ShoptetApi`
  found 2 matches — both are XML doc-comment prose (in `IPackedOrderStatusUpdater.cs` and
  `IOrderStatusReader.cs`, added by the earlier `packaging-contract`/`expeditionlist-contract`
  tasks) explaining that each contract "mirrors" the corresponding `IEshopOrderClient` method
  by name. These are documentation only, not code coupling — the reflection-based
  `ModuleBoundariesTests` (which is the actual enforcement mechanism for FR-7) confirms no
  real reference remains. Out of scope to edit per this task's file list (only
  `ModuleBoundariesTests.cs`).

## Notes

Two deviations from the literal task steps, both environment/tooling artifacts rather than
functional issues:
1. `cd backend && dotnet build` / `dotnet format --verify-no-changes` as literally written
   fail with "no MSBuild project or solution file" because the `.sln` lives at the repo root,
   not under `backend/`. Ran `dotnet build Anela.Heblo.sln` / `dotnet format Anela.Heblo.sln
   --verify-no-changes` from the repo root instead — same effect, correct target.
2. The grep in step 5 turns up 2 doc-comment matches rather than zero output, but both are
   prose, not code references; see "How to verify" above for the detailed justification.

No other deviations. All 8 steps in the task context were otherwise followed exactly.

## PR Summary

Pinned the `IEshopOrderClient` decoupling (completed across the `packaging-*` and
`expeditionlist-*` tasks earlier in this feature) into the reflection-based module-boundary
architecture test: removed the two stale `IEshopOrderClient` allowlist entries from
`Packaging -> ShoptetOrders`, and added a new `ExpeditionList -> ShoptetOrders` rule (empty
allowlist) proving `PrintExpeditionOrderHandler` no longer references `ShoptetOrders` at all.
This is the last task for this feature — FR-7 (verify no remaining direct coupling) is now
enforced by an automated test, not just manual inspection.

### Changes
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — removed stale
  `IEshopOrderClient` allowlist entries, updated doc comment, added
  `ExpeditionListShoptetOrdersAllowlist` and the new `"ExpeditionList -> ShoptetOrders"` rule.

## Status
DONE
