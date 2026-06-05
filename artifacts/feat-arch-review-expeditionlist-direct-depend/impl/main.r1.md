All 10 tasks complete. Let me write the output summary.

---

# Implementation: Invert ExpeditionList → Logistics Picking Dependency

## What was implemented

Inverted the dependency between `ExpeditionList` and `Logistics.Picking` so ExpeditionList now owns the contract and Logistics provides a translating adapter. Added a CI-enforced architecture boundary test to prevent regression.

## Files created/modified

### New files
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/IExpeditionPickingSource.cs` — consumer-owned interface with `CreatePickingListAsync`
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ExpeditionPickingRequest.cs` — minimal request DTO (class, not record); owns `DefaultCarriers` static
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ExpeditionPickingResult.cs` — minimal result DTO with `ExportedFiles` + `TotalCount` (no `OrderIds`)
- `backend/src/Anela.Heblo.Application/Features/Logistics/Infrastructure/LogisticsExpeditionPickingAdapter.cs` — `internal sealed` bridge adapter implementing `IExpeditionPickingSource`, delegating to Logistics-namespaced `IPickingListSource`
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Infrastructure/LogisticsExpeditionPickingAdapterTests.cs` — 4 unit tests covering field translation, callback passthrough, and cancellation token forwarding

### Modified files
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/IExpeditionListService.cs` — uses `ExpeditionPickingRequest`/`ExpeditionPickingResult`
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/ExpeditionListService.cs` — depends on `IExpeditionPickingSource`
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Infrastructure/Jobs/PrintPickingListJob.cs` — uses `ExpeditionPickingRequest`
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/RunExpeditionListPrintFix/RunExpeditionListPrintFixHandler.cs` — uses `ExpeditionPickingRequest`
- `backend/src/Anela.Heblo.Application/Features/Logistics/LogisticsModule.cs` — registers `IExpeditionPickingSource → LogisticsExpeditionPickingAdapter`
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/ExpeditionListServiceOrderStateTests.cs` — retargeted to `IExpeditionPickingSource`
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/ExpeditionListServicePrintSinkTests.cs` — retargeted to `IExpeditionPickingSource`
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — new `ExpeditionList → Logistics` rule with `Carriers` allowlist

## Tests

- `LogisticsExpeditionPickingAdapterTests` (4 tests) — new, testing all translation behavior
- `ExpeditionListServiceOrderStateTests` (5 tests) — existing, retargeted to new interface
- `ExpeditionListServicePrintSinkTests` (2 tests) — existing, retargeted to new interface
- `ModuleBoundariesTests` (new theory case) — enforces the new boundary; proven non-vacuous by deliberate violation test

All 157 targeted tests pass. Full suite: 4377 pass (38 pre-existing Docker/Testcontainers failures unrelated to this change).

## How to verify

```bash
# All targeted tests
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ExpeditionList|FullyQualifiedName~LogisticsExpeditionPickingAdapter|FullyQualifiedName~ModuleBoundariesTests"

# FR-3: no Logistics imports in ExpeditionList source
grep -rn "Anela.Heblo.Application.Features.Logistics" \
  backend/src/Anela.Heblo.Application/Features/ExpeditionList/
# Expected: zero output
```

## Notes

- Logistics-namespaced types (`IPickingListSource`, `PrintPickingListRequest`, `PrintPickingListResult`) are untouched per spec Out-of-Scope.
- `Anela.Heblo.Domain.Features.Logistics.Carriers` remains referenced in `ExpeditionPickingRequest` — allowlisted in the boundary rule with justification (stable widely-used enum; duplication would create sync burden).
- The 38 test failures in the full suite are pre-existing Docker/Testcontainers issues unrelated to this PR.
- Commits follow TDD order: contract types → failing test → adapter implementation → DI registration → service migration → job/handler migration → architecture test.

## PR Summary

Inverted the `ExpeditionList → Logistics.Picking` dependency so ExpeditionList owns its contract (`IExpeditionPickingSource`, `ExpeditionPickingRequest`, `ExpeditionPickingResult`) and Logistics provides a translating adapter (`LogisticsExpeditionPickingAdapter`). Added a CI-enforced architecture boundary test with a single justified allowlist entry for the `Carriers` domain enum.

The refactor follows the established consumer-owned-contract pattern already used for `ILeafletKnowledgeSource`, `ICatalogTransportSource`, and others. It removes the silent coupling that caused `ExpeditionList` to break whenever Logistics-side picking types were renamed or reshaped, and locks the inversion in so the violation cannot grow back.

### Changes
- `Features/ExpeditionList/Contracts/` — new `IExpeditionPickingSource`, `ExpeditionPickingRequest`, `ExpeditionPickingResult`
- `Features/Logistics/Infrastructure/LogisticsExpeditionPickingAdapter.cs` — new `internal sealed` adapter; DI registration in `LogisticsModule`
- `Features/ExpeditionList/Services/`, `Jobs/`, `UseCases/` — migrated off Logistics.Picking types (4 files)
- `Architecture/ModuleBoundariesTests.cs` — new `ExpeditionList → Logistics` rule enforced in CI

## Status
DONE