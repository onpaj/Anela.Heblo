# Implementation: fix-log-levels

## What was implemented

1. **CatalogResilienceService** — generic `catch (Exception)` block demoted from `LogError` to `LogDebug`. The `BrokenCircuitException` catch block retains its `LogWarning` unchanged.

2. **ProductPairingDqtComparer** — added canonical caller-side logging:
   - Added `using Microsoft.Extensions.Logging;`
   - Added `private readonly ILogger<ProductPairingDqtComparer> _logger;` field
   - Added `ILogger<ProductPairingDqtComparer> logger` constructor parameter with field assignment
   - Wrapped both `ExecuteWithResilienceAsync` calls in try/catch blocks emitting structured `LogWarning` with `Operation` and `ExceptionType` properties, then re-throw

   One type correction was required: the ERP variable was declared as `List<ErpStock>` in the task spec, but `IErpStockClient.ListAsync` returns `IReadOnlyList<ErpStock>` — corrected to `IReadOnlyList<ErpStock>`.

## Files created/modified

- Modified: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogResilienceService.cs`
- Modified: `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/ProductPairingDqtComparer.cs`

## How to verify

```bash
cd backend
dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --no-restore -v q
# Expect: Build succeeded. 0 Error(s)

dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~CatalogResilience|FullyQualifiedName~ProductPairing" -v n --no-restore
```

At runtime: when a resilience operation exhausts all retries, the log output should show:
- `LogDebug` from `CatalogResilienceService` (low noise, internal detail)
- `LogWarning` from `ProductPairingDqtComparer` (the canonical signal visible in production alerting)

## Notes

- The `IErpStockClient.ListAsync` return type is `IReadOnlyList<ErpStock>`, not `List<ErpStock>` — the rest of `CompareAsync` (`.Where(...).ToList()`) works correctly on `IReadOnlyList<T>` via LINQ.
- `IEshopStockClient.ListAsync` returns `List<EshopStock>` — no type change needed there.
- The DI container will automatically inject `ILogger<ProductPairingDqtComparer>` since the class is registered via the existing DI wiring; no registration changes required.

## Status
DONE
