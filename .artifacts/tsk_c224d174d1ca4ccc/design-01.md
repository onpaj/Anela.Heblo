# Design: Remove unused SafeMarginCalculator and SalesCost dead code

No UI is involved — this is a backend-only deletion of unreferenced production code. The UX/UI section is omitted.

## Verification against current source

Re-confirmed directly (not just trusting the plan) immediately before writing this design:

```
$ grep -rn "SafeMarginCalculator" backend/
backend/test/.../SafeMarginCalculatorTests.cs   (test class + 3 usages, all internal to the test)
backend/src/.../CatalogModule.cs:102             services.AddTransient<SafeMarginCalculator>();
backend/src/.../Services/SafeMarginCalculator.cs (class + ctor + logger field, definition only)

$ grep -rn "\bSalesCost\b" backend/
backend/src/.../Services/SalesCost.cs:3          public class SalesCost   (definition only, zero other references)
```

No production caller exists for either type. The plan's premise is correct as of this session; the design below is a direct execution of `plan-01.md` — nothing in that plan needs revision.

## Component design

### Removed components

| Component | File | Responsibility being removed |
|---|---|---|
| `SafeMarginCalculator` | `backend/src/Anela.Heblo.Application/Features/Catalog/Services/SafeMarginCalculator.cs` | Transient service computing a flat `((sellingPrice - cost) / sellingPrice) * 100` margin with null/negative/zero-price guards. Never injected outside its own test. |
| `MarginCalculationResult` | same file (nested type) | Result wrapper (`Success`/`Invalid`/`Error` factory methods) used only as `SafeMarginCalculator`'s return type. No other type produces or consumes it. |
| `SalesCost` | `backend/src/Anela.Heblo.Application/Features/Catalog/Services/SalesCost.cs` | Plain DTO (`Date`, `UnitCost`, `TotalCost`, `AmountSold`). Not constructed anywhere in `backend/src`; distinct from the actively-used `SalesCostProvider`/`ISalesCostCache` family, which is untouched. |
| `SafeMarginCalculatorTests` | `backend/test/Anela.Heblo.Tests/Features/Catalog/SafeMarginCalculatorTests.cs` | Unit tests exercising only the removed calculator. Has no shared fixtures or base classes used by other test files (confirmed by the test class body being self-contained: local `Mock<ILogger<>>` and direct instantiation). |

### Retained components (no interface change)

- `MarginCalculationService` / `MarginLevel.Create` (`backend/.../Services/MarginCalculationService.cs:108-117`) — the authoritative multi-level (M0/M1A/M1B/M2) margin pipeline. Not touched, not extended to "absorb" the deleted logic. Its richer model already supersedes the flat calculation `SafeMarginCalculator` performed, so no consolidation work is needed here.
- `CatalogModule.cs` — loses one DI registration line (`services.AddTransient<SafeMarginCalculator>();` at line 102); every other registration in the module is unaffected.
- `SalesCostProvider` / `ISalesCostProvider` / `SalesCostCache` / `ISalesCostCache` — explicitly out of scope, despite the name collision with `SalesCost`. Verified these are separate, actively-registered types (`CatalogModule.cs:84,96`) that do not reference the DTO being removed.

### Boundary after the change

The Catalog module's public/DI-visible surface shrinks by exactly one transient service registration and one DTO type. No consumer outside the deleted files referenced any of the four removed symbols, so the module's external contract (its MediatR handlers, controllers, and any type consumed by other modules) is unchanged. This is a pure subtraction at the leaf of the dependency graph, not a refactor.

## Data schemas

Not applicable. `MarginCalculationResult` and `SalesCost` are plain in-memory types with no persistence mapping (no EF configuration, no migration) and no API/OpenAPI surface (never returned from a controller or MediatR response DTO). Their removal has no schema, request/response, or event-payload impact.

## Implementation shape

1. Delete `SafeMarginCalculator.cs` (removes both `SafeMarginCalculator` and `MarginCalculationResult` — same file).
2. Delete `SalesCost.cs`.
3. Delete `SafeMarginCalculatorTests.cs`.
4. Remove line `services.AddTransient<SafeMarginCalculator>();` from `CatalogModule.cs:102`.
5. Build and run the full backend test suite to confirm no dangling reference and no broken test collection.
6. `dotnet format`.

This is the same four-file, one-line-removal shape as `plan-01.md`'s rough plan — the design step adds no new components, abstractions, or migrations because none are warranted for a dead-code removal.
