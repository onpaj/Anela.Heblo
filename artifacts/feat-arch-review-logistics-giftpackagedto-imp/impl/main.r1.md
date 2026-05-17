All tasks complete. Writing the output artifact.

---

# Implementation: Remove Logistics → Purchase Module Coupling in GiftPackageDto

## What was implemented

Introduced a Logistics-owned `GiftPackageSeverity` enum (3 members: `Critical`, `Severe`, `Optimal`) and removed all compile-time dependencies from the Logistics module on the Purchase module's `StockSeverity`. Added a reflection-based architecture test to enforce the boundary in CI. Updated frontend components to use the new enum and removed dead filter UI that referenced Purchase-only severity values.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Contracts/GiftPackageSeverity.cs` — new enum, namespace `Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts`
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Contracts/GiftPackageDto.cs` — `Severity` property retyped to `GiftPackageSeverity`, Purchase `using` removed
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs` — `CalculateSeverity` return type and values swapped to `GiftPackageSeverity`, Purchase `using` removed
- `backend/src/Anela.Heblo.Application/Features/Logistics/DashboardTiles/CriticalGiftPackagesTile.cs` — `GiftPackageSeverity.Critical` reference, Purchase `using` removed
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — new `[Fact]` `Logistics_types_should_not_reference_Purchase_owned_namespaces`
- `frontend/src/api/generated/api-client.ts` — regenerated; now contains `GiftPackageSeverity` alongside unchanged `StockSeverity`
- `frontend/src/components/pages/GiftPackageManufacturing/GiftPackageManufacturingList.tsx` — imports `GiftPackageSeverity`, `GiftPackageSummary` trimmed to 4 fields, helpers trimmed to 3-value switch, console.log removed, missing sort cases added
- `frontend/src/components/pages/GiftPackageManufacturing/GiftPackageManufacturingSummary.tsx` — imports `GiftPackageSeverity`, dead Low/Overstocked/NotConfigured filter buttons removed from both compact and full views, unused `Settings` import removed

## Tests

- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — `Logistics_types_should_not_reference_Purchase_owned_namespaces` passes (0 violations)
- Both architecture tests (Leaflet + Logistics) pass: 2/2

## How to verify

```bash
# Backend build (0 errors)
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj

# dotnet format (no changes needed)
dotnet format backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --verify-no-changes

# Architecture tests
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Architecture.ModuleBoundariesTests"

# Frontend build (clean)
cd frontend && npm run build

# Lint — modified files are clean; pre-existing errors in unrelated test files not introduced by this change
npx eslint src/components/pages/GiftPackageManufacturing/GiftPackageManufacturingList.tsx \
           src/components/pages/GiftPackageManufacturing/GiftPackageManufacturingSummary.tsx

# Verify no StockSeverity references remain in Logistics
grep -r "StockSeverity" backend/src/Anela.Heblo.Application/Features/Logistics/
# → should return empty

# Verify GiftPackageSeverity exists in generated TS client
grep "GiftPackageSeverity" frontend/src/api/generated/api-client.ts
```

## Notes

- The arch-review noted that `JsonStringEnumConverter` is globally registered in `Program.cs`, so the wire format is string-based (`"Critical"`, `"Severe"`, `"Optimal"`). No per-enum `[JsonConverter]` attribute was needed.
- The arch-review's recommended enum location (`UseCases/GiftPackageManufacture/Contracts/`) was used instead of the spec's `Features/Logistics/Contracts/` to keep vertical-slice cohesion.
- The frontend `GiftPackageSummary` interface dropped `lowStockCount`, `overstockedCount`, `notConfiguredCount` as the backend never emits those values for gift packages.
- `npm run lint` reports 97 pre-existing errors in terminal and leaflet test files — none in the files modified by this change.

## PR Summary

Removed the cross-module compile-time dependency from Logistics on Purchase's `StockSeverity` by introducing a Logistics-owned `GiftPackageSeverity` enum (Critical, Severe, Optimal). The Logistics module now owns its severity classification contract end-to-end; Purchase's enum is untouched. A new reflection-based architecture test enforces this boundary in CI, mirroring the existing Leaflet→KnowledgeBase test. The frontend was updated to use the regenerated client type and trimmed of dead filter UI that referenced Purchase-only severity buckets the gift-package backend never emits.

### Changes
- `GiftPackageSeverity.cs` — new Logistics-owned enum (3 members)
- `GiftPackageDto.cs` — `Severity` property retyped; Purchase `using` removed
- `GiftPackageManufactureService.cs` — `CalculateSeverity` swapped to `GiftPackageSeverity`
- `CriticalGiftPackagesTile.cs` — `GiftPackageSeverity.Critical` reference; Purchase `using` removed
- `ModuleBoundariesTests.cs` — `Logistics_types_should_not_reference_Purchase_owned_namespaces` fact added
- `api-client.ts` — regenerated with `GiftPackageSeverity` enum
- `GiftPackageManufacturingList.tsx` — 3-value type, trimmed helpers, fixed sort cases, removed console.log
- `GiftPackageManufacturingSummary.tsx` — 3-value filter buttons, removed dead Low/Overstocked/NotConfigured UI

## Status
DONE