Now I have enough context. Writing the architecture review.

# Architecture Review: Remove dead PlaceholderStockValueService and simplify StockValueService DI registration

## Skip Design: true

## Architectural Fit Assessment

This is a pure backend cleanup that aligns perfectly with existing patterns. The repository's Vertical Slice Architecture (see `docs/architecture/development_guidelines.md`) already favors per-module DI registration via `*Module.AddXxxModule` extensions, and other modules in the codebase use the standard typed `services.AddScoped<TInterface, TImpl>()` form. The current factory lambda is the outlier — removing it brings `FinancialOverviewModule` in line with how every other module registers its services.

Integration points are minimal and contained:
- `FinancialOverviewModule.AddFinancialOverviewModule` (production wiring)
- Two test files that currently hand-roll `PlaceholderStockValueService` (verified by grep — exactly 3 files reference the type, including the placeholder itself)

No public contracts, domain types, MediatR handlers, controllers, or API surface area are affected. `IStockValueService` lives in `Anela.Heblo.Domain.Features.FinancialOverview` and stays untouched.

## Proposed Architecture

### Component Overview

```
Production DI (after change):
┌────────────────────────────────────────────┐
│ FinancialOverviewModule                    │
│   services.AddScoped<                      │
│     IStockValueService, StockValueService> │
└────────────────────────┬───────────────────┘
                         │ resolves
                         ▼
              ┌──────────────────────┐
              │ StockValueService    │◄── IErpStockClient
              │                      │◄── IProductPriceErpClient
              │                      │◄── ILogger<StockValueService>
              └──────────────────────┘

Test-time override (both test sites):
┌────────────────────────────────────────────┐
│ Test scaffolding                           │
│   1. AddFinancialOverviewModule(...)       │
│   2. Remove existing IStockValueService    │
│      descriptor                             │
│   3. Register Moq<IStockValueService>      │
│      stub returning empty MonthlyStockChange│
└────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Standard typed registration over factory lambda
**Options considered:**
- (A) Keep factory lambda, just delete `PlaceholderStockValueService`.
- (B) Replace factory with `services.AddScoped<IStockValueService, StockValueService>()`.
- (C) Move the factory behind an `IStockValueServiceFactory` abstraction.

**Chosen approach:** (B).

**Rationale:** The factory exists solely to manually wire three constructor parameters the DI container already knows how to resolve. (C) is YAGNI — there is no second implementation and no construction logic beyond `new`. (A) preserves a maintenance trap where new `StockValueService` constructor params silently fall out of sync. (B) is the convention used elsewhere in the codebase and is what `csharp-patterns.md` prescribes.

#### Decision 2: Inline Moq stubs over a shared test fake
**Options considered:**
- (A) Inline `Mock<IStockValueService>` in each of the two test files.
- (B) Introduce a `FakeStockValueService` in `Anela.Heblo.Tests.Common`.
- (C) Move `PlaceholderStockValueService` to the test project.

**Chosen approach:** (A).

**Rationale:** Two call sites do not justify a shared abstraction (DRY-but-not-speculative). Moq is already referenced by the test project; the stub is two lines per site. (B) becomes worthwhile when a third caller appears — defer until then. (C) preserves a named test type that adds no value over `Mock.Of<IStockValueService>()`.

#### Decision 3: Rename rather than delete the "factory pattern" test
**Options considered:**
- (A) Delete `AddFinancialOverviewModule_UsesFactoryPattern_AvoidsServiceProviderAntipattern`.
- (B) Rename and keep the body.

**Chosen approach:** (B).

**Rationale:** The body verifies a meaningful invariant — the module can be registered, built, and resolved without an antipattern (e.g. `BuildServiceProvider` during registration). The name lies once factory wiring is gone; the body does not. Rename per the spec (`_RegistersIStockValueService_WithoutBuildServiceProviderAntipattern`).

## Implementation Guidance

### Directory / Module Structure

No new files. Net file change:

- **Delete:** `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/PlaceholderStockValueService.cs`
- **Edit:** `backend/src/Anela.Heblo.Application/Features/FinancialOverview/FinancialOverviewModule.cs`
- **Edit:** `backend/test/Anela.Heblo.Tests/Application/FinancialOverview/FinancialOverviewModuleTests.cs`
- **Edit:** `backend/test/Anela.Heblo.Tests/Features/FinancialOverviewTests.cs`

### Interfaces and Contracts

No interface or contract changes. `IStockValueService` in `Anela.Heblo.Domain.Features.FinancialOverview` is unchanged.

### Data Flow

Unchanged at runtime. The DI container resolves `IStockValueService` → `StockValueService` directly, with constructor params (`IErpStockClient`, `IProductPriceErpClient`, `ILogger<StockValueService>`) auto-injected. Under tests, the override pattern (remove descriptor → add stub) bypasses production wiring before the test host is built.

### Implementation Notes

1. **FR-1 edit window** — `FinancialOverviewModule.cs:18-25`. Replace the entire factory block (including the misleading "tests can override this" comment) with the one-liner. Remove the now-unused `using Microsoft.Extensions.Logging;` only if no other reference remains in the file (grep first).
2. **FR-3a using cleanup** — In `FinancialOverviewTests.cs`, the placeholder is the only consumer of `using Anela.Heblo.Application.Features.FinancialOverview.Services;` based on the file head shown. Verify before removing — `FinancialOverviewTestFactory` may still need types from elsewhere.
3. **FR-3b test name** — Rename file-internal usages only; this is a private test method so no external callers exist.
4. **Test order discipline** — Both test overrides must call `AddFinancialOverviewModule(...)` first, then locate-and-remove the `IStockValueService` descriptor, then add the Moq stub. Reversing the order leaves the real registration in place and the stub becomes a no-op append (DI resolves the first matching descriptor).
5. **Mock return type** — Use `Array.Empty<MonthlyStockChange>()` rather than `new List<MonthlyStockChange>()` to match `IReadOnlyList<T>` without an extra heap allocation. Matches the spec example.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Removing the descriptor in test overrides silently no-ops if the production registration changes lifetime (e.g., singleton) | Low | The override code already searches by `ServiceType` regardless of lifetime; standard typed registration preserves Scoped lifetime. Existing assertions in `AddFinancialOverviewModule_RegistersDefaultRealService` would catch a lifetime regression. |
| Other tests or code paths quietly reference `PlaceholderStockValueService` and break the build | Low | Grep confirms exactly 3 files reference the type. Spec already covers all three. Run `dotnet build` after deletion (already in the project's mandatory completion checklist). |
| Removing the `using Microsoft.Extensions.Logging;` from `FinancialOverviewModule.cs` if other code in the file uses `ILogger<>` | Low | File only used `ILogger<StockValueService>` inside the deleted lambda. Verify via grep before removing the using. If unsure, leave the using — analyzer/`dotnet format` will flag if unused. |
| Test-only `using` for `Services` namespace still required by `StockValueService`-typed assertions elsewhere in the test file | Low | `FinancialOverviewModuleTests.cs:42, 94` assert `.BeOfType<StockValueService>()` — keep the `Services` using in that file. Only remove from `FinancialOverviewTests.cs` if grep confirms no remaining reference. |

## Specification Amendments

The spec is implementable as written. Two minor clarifications worth folding in:

1. **FR-3b ordering** — The spec's suggested code for `AddFinancialOverviewModule_CanOverrideStockValueService_ForTesting` correctly registers ERP clients **before** calling `AddFinancialOverviewModule`. This is necessary now (was not before): with the factory removed, the standard typed registration validates dependencies at resolve time, and `AddFinancialOverviewModule_RegistersDefaultRealService` already relies on ERP clients being pre-registered. Confirm the test does the same — the spec's snippet does, but call this out explicitly so a developer does not omit it.

2. **FR-1 — `ILedgerService` is irrelevant to this module's registration**. `FinancialOverviewModuleTests.cs:28` registers it as a `Mock.Of<ILedgerService>()` to satisfy `IFinancialAnalysisService`. Keep that registration in the rewritten FR-3b test (the spec snippet already does). No change required, just noting it so the test continues to resolve `IFinancialAnalysisService` cleanly even though the renamed test focuses on `IStockValueService`.

No other amendments. Out-of-scope items remain out of scope.

## Prerequisites

None. No migrations, infrastructure, configuration, secrets, or package additions. `Moq` and `FluentAssertions` are already test-project dependencies. Work can start immediately on the existing branch.