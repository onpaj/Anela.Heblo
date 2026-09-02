# Architecture Review: Remove `HasDayAlreadyBeenProcessedAsync` from `IConsumptionCalculationService`

## Skip Design: true

## Architectural Fit Assessment
This is a pure backend refactor confined to a single vertical slice (`PackingMaterials`) and touches no cross-module boundary, no contract consumed by another module, no HTTP/MediatR-facing DTO, and no UI. It aligns cleanly with `docs/architecture/development_guidelines.md`'s module-contract rules: `IConsumptionCalculationService` is an internal service interface of the `PackingMaterials` module (registered in `PackingMaterialsModule.cs`, consumed only by `ProcessDailyConsumptionHandler` within the same module), not a cross-module `contracts/` interface like `IProductQueryService`. Shrinking it does not affect module independence or any other module's ability to communicate with `PackingMaterials`.

Verified by direct inspection:
- `IConsumptionCalculationService` has exactly one production consumer: `ProcessDailyConsumptionHandler` (constructor-injected), which calls only `ProcessDailyConsumptionAsync`.
- DI registration in `PackingMaterialsModule.cs` (`services.AddScoped<IConsumptionCalculationService, ConsumptionCalculationService>();`) is untouched by this change — registration is by concrete type against the interface; removing a member from the interface does not affect binding.
- `HasDayAlreadyBeenProcessedAsync` is called today only as a same-class member call inside `ConsumptionCalculationService.ProcessDailyConsumptionAsync` (`if (await HasDayAlreadyBeenProcessedAsync(...))`, no interface indirection, no `this.` needed) — this call site compiles unchanged whether the method is public, private, or anything in between, because C# resolves unqualified same-class calls against the concrete type's member table, not through the interface.
- The only other reference is the test `ConsumptionCalculationServiceTests.HasDayAlreadyBeenProcessedAsync_ShouldReturnCorrectValue`, which will fail to compile once the method is private (CS0122) — this is expected and is exactly what FR-2 replaces.

This confirms the spec's premise is accurate: there is no hidden caller that would break.

## Proposed Architecture

### Component Overview
No component, module, or dependency graph changes. The shape of the system before and after:

```
ProcessDailyConsumptionHandler  ---uses--->  IConsumptionCalculationService
                                                  ^
                                                  | implements
                                              ConsumptionCalculationService
                                                  |
                                                  +--> IPackingMaterialRepository
                                                  +--> IInvoiceConsumptionSource
                                                  +--> ILogger<ConsumptionCalculationService>
```

Before: `IConsumptionCalculationService` = { `ProcessDailyConsumptionAsync`, `HasDayAlreadyBeenProcessedAsync` }.
After: `IConsumptionCalculationService` = { `ProcessDailyConsumptionAsync` }; `HasDayAlreadyBeenProcessedAsync` becomes a private helper on `ConsumptionCalculationService`, called only from within `ProcessDailyConsumptionAsync`.

No new classes, no new files, no new DI registrations.

### Key Design Decisions

#### Decision 1: Interface shrink vs. extracting a separate `IIdempotencyChecker`-style abstraction
**Options considered:**
1. Remove the method from the interface and make it `private` (as the issue suggests).
2. Extract idempotency checking into a small standalone internal collaborator (e.g., an `IDailyRunIdempotencyGuard`) injected into `ConsumptionCalculationService`.
3. Leave it on the interface but mark it `[Obsolete]` as a soft deprecation.

**Chosen approach:** Option 1 — remove from the interface, make `private`.

**Rationale:** The method is a two-line pass-through to `_repository.HasDailyProcessingBeenRunAsync`, used exactly once, entirely inside the class that owns the idempotency concern. Option 2 (extraction) would be over-engineering for a single call site with no reuse pressure and no other implementation of `IConsumptionCalculationService` on the horizon — it adds an interface, a DI registration, and a constructor parameter for zero behavioral or testability gain, and nothing in the spec or codebase calls for it. Option 3 (obsolete) leaves the ISP violation in place and only defers the cleanup; the arch-review finding is unambiguous that no consumer needs it through the interface, so there's nothing to deprecate gracefully for — a hard removal is correct. This project's guidelines favor small, cohesive interfaces scoped to actual consumers (see the `IProductQueryService`-style contract examples in `development_guidelines.md`); Option 1 is the direct, minimal application of that principle.

#### Decision 2: Test refactor shape — two-call sequence vs. reusing existing pre-seeded-mock tests
**Options considered:**
1. Delete the direct test outright, relying on the two *already-existing* tests that pre-seed `SetHasDailyProcessingBeenRun(date, true)` and assert `WasRun == false` on a single call (`ProcessDailyConsumptionAsync_ReturnsWasRunFalse_WhenAlreadyProcessed`, `ProcessDailyConsumptionAsync_SecondRun_ReturnsWasRunFalse_WithoutMutating`).
2. Replace it with a new test that performs a genuine two-call sequence against the same `MockPackingMaterialRepository` instance — call `ProcessDailyConsumptionAsync(date)` once (real first run), then call it again for the same `date` (real second run), asserting `WasRun: false` on the second call — per the issue's own suggested fix.

**Chosen approach:** Option 2.

**Rationale:** Option 1 would leave a real coverage gap: the two pre-seeded tests only prove "if the repository already reports the date as processed, no work happens" — they never exercise the actual mutation the first call makes (calling `AddDailyRunAsync`) followed by a second call observing that mutation's effect through `HasDailyProcessingBeenRunAsync`. That end-to-end idempotency loop — "processing the same date twice in a row is safe" — is the one property this issue's suggested fix explicitly asks to preserve, and it's currently the *only* property asserted by the test being deleted (indirectly, since the old test just checks the pass-through, not full idempotency). A genuine two-call sequence is a **stronger** test than the one being removed and directly matches the issue's suggested fix. This requires confirming `MockPackingMaterialRepository.AddDailyRunAsync` causes a subsequent `HasDailyProcessingBeenRunAsync` call for the same date to return `true` — see Prerequisites below; if the mock does not already track this, use the mock's existing `SetHasDailyProcessingBeenRun` seam between the two calls (still exercising the second `ProcessDailyConsumptionAsync` call as the assertion point, not the private method directly), which satisfies the issue's instructions ("call it twice... assert `WasRun: false` on the second call") equally well without depending on unverified mock plumbing.

## Implementation Guidance

### Directory / Module Structure
No new files or directories. Two existing files are edited in place:
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Services/IConsumptionCalculationService.cs`
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Services/ConsumptionCalculationService.cs`

One existing test file is edited in place:
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/ConsumptionCalculationServiceTests.cs`

### Interfaces and Contracts

`IConsumptionCalculationService.cs` — target shape:
```csharp
namespace Anela.Heblo.Application.Features.PackingMaterials.Services;

public interface IConsumptionCalculationService
{
    Task<ProcessDailyConsumptionResult> ProcessDailyConsumptionAsync(
        DateOnly processingDate,
        CancellationToken cancellationToken = default);
}
```

`ConsumptionCalculationService.cs` — only the access modifier on `HasDayAlreadyBeenProcessedAsync` changes, from `public` to `private`. The method body, signature, parameter defaults, and the call site inside `ProcessDailyConsumptionAsync` (line 28) are otherwise byte-identical:
```csharp
private async Task<bool> HasDayAlreadyBeenProcessedAsync(
    DateOnly date,
    CancellationToken cancellationToken = default)
{
    return await _repository.HasDailyProcessingBeenRunAsync(date, cancellationToken);
}
```
No `override` or explicit interface implementation syntax (`IConsumptionCalculationService.HasDayAlreadyBeenProcessedAsync`) was ever used, so there is nothing to strip beyond the interface member declaration and the access modifier — this is a mechanical two-line diff plus the deleted interface line.

### Data Flow
Unchanged. `ProcessDailyConsumptionHandler.Handle` → `IConsumptionCalculationService.ProcessDailyConsumptionAsync` → (internally) `HasDayAlreadyBeenProcessedAsync` (now private) → `IPackingMaterialRepository.HasDailyProcessingBeenRunAsync`. No new hop, no new branch, no change to `ProcessDailyConsumptionResult`'s shape (`WasRun`, `MaterialsProcessed`).

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Hidden caller of `HasDayAlreadyBeenProcessedAsync` through the interface not caught by grep (e.g. reflection, dynamic dispatch) | Low | A full solution `dotnet build` after the change will fail with CS0122/CS0535-style errors on any missed compile-time reference; no reflection-based access to this method exists in the codebase (verified: no `GetMethod("HasDayAlreadyBeenProcessedAsync")` or dynamic invocation anywhere) |
| `MockPackingMaterialRepository`'s `AddDailyRunAsync` doesn't automatically make a later `HasDailyProcessingBeenRunAsync` call return `true`, so a naive two-call FR-2 test would silently pass without proving idempotency (false positive: second call could return `WasRun:true` again if the mock doesn't track state, and a badly written assertion could still pass) | Medium | Developer must read `MockPackingMaterialRepository`'s existing implementation (in the same test project) before writing the FR-2 test; if `AddDailyRunAsync` doesn't wire this automatically, use the existing `SetHasDailyProcessingBeenRun(date, true)` seam between the two `ProcessDailyConsumptionAsync` calls (already used by two other tests in this same file) rather than inventing new mock behavior |
| Test file ends up with an orphaned `using` or now-unused mock setup helper after deleting the old test | Low | `dotnet format` / build warnings will surface unused usings; review the diff for leftover dead helper code scoped only to the deleted test |

## Specification Amendments
None. The spec (`spec.r1.md`) already correctly scopes FR-1 and FR-2, and its Background section's grep-verified claims match this review's independent verification. No functional requirement needs to change.

One clarification for the planner: FR-2's acceptance criteria already anticipates the mock-state uncertainty called out in Risks above and explicitly permits falling back to the `SetHasDailyProcessingBeenRun` seam — the planner should carry that fallback into the task breakdown rather than treating it as an open question, since Decision 2 above resolves it.

## Prerequisites
None. No migration, no config, no infrastructure change, no new package. Implementation can start immediately; the only pre-implementation step for the developer is to read `MockPackingMaterialRepository`'s `AddDailyRunAsync`/`HasDailyProcessingBeenRunAsync` implementation in the test project to decide which of the two FR-2 mock-state options (auto-tracked vs. explicit `SetHasDailyProcessingBeenRun`) applies, per Decision 2.
