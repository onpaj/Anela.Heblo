# Specification: Remove `HasDayAlreadyBeenProcessedAsync` from `IConsumptionCalculationService`

## Summary
`IConsumptionCalculationService` currently exposes `HasDayAlreadyBeenProcessedAsync` as a public interface member, even though it is only ever used as an internal idempotency check inside `ConsumptionCalculationService.ProcessDailyConsumptionAsync`. No production caller invokes it through the interface. This is an Interface Segregation Principle (ISP) violation: the interface's public surface is wider than what any real consumer needs. This change removes the method from the interface, makes it a private implementation detail of `ConsumptionCalculationService`, and refactors the one test that exercises it directly so the same behavior is verified indirectly through `ProcessDailyConsumptionAsync`.

## Background
`IConsumptionCalculationService` is the contract used by `ProcessDailyConsumptionHandler` (via constructor injection, registered in `PackingMaterialsModule.cs`) to trigger daily packing-material consumption processing. The interface declares two methods:

```csharp
public interface IConsumptionCalculationService
{
    Task<ProcessDailyConsumptionResult> ProcessDailyConsumptionAsync(DateOnly processingDate, CancellationToken cancellationToken = default);
    Task<bool> HasDayAlreadyBeenProcessedAsync(DateOnly date, CancellationToken cancellationToken = default);
}
```

Codebase inspection (grep across `backend/`) confirms:
- `ProcessDailyConsumptionAsync` is called by `ProcessDailyConsumptionHandler.Handle` (`backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/ProcessDailyConsumption/ProcessDailyConsumptionHandler.cs`) — the only production caller of the interface.
- `HasDayAlreadyBeenProcessedAsync` is called in exactly two places:
  1. `ConsumptionCalculationService.ProcessDailyConsumptionAsync` line 28, as a same-class call (`await HasDayAlreadyBeenProcessedAsync(...)`, no `this.` interface indirection) used purely as an idempotency guard before doing any work.
  2. `ConsumptionCalculationServiceTests.HasDayAlreadyBeenProcessedAsync_ShouldReturnCorrectValue` (line 245 area), which instantiates the concrete `ConsumptionCalculationService` and calls the method directly to assert it forwards to `IPackingMaterialRepository.HasDailyProcessingBeenRunAsync`.
- `ProcessDailyConsumptionHandlerTests` mocks `IConsumptionCalculationService` (via `Mock<IConsumptionCalculationService>`) but never sets up or asserts against `HasDayAlreadyBeenProcessedAsync` — its removal from the interface has zero impact on that test file.
- No other file in the repository (production or test) references `HasDayAlreadyBeenProcessedAsync` or constructs a mock/stub of `IConsumptionCalculationService` that stubs it.

Because the method is purely an implementation detail of how `ConsumptionCalculationService` achieves idempotent daily processing, promoting it to the public interface is misleading: it implies external callers (or alternative implementations of the interface) should treat it as an independently useful operation, when in reality it exists solely to guard `ProcessDailyConsumptionAsync`'s own body. Keeping it on the interface also forces any future test double or alternate implementation of `IConsumptionCalculationService` to carry a method nothing needs, which is exactly the ISP smell the arch-review routine flagged.

This is a same-day, same-PR mechanical refactor with no behavior change and no new feature — the "why" is captured above for the architect/planner; the sections below still follow the standard spec structure per process, kept minimal since there is exactly one functional requirement.

## Functional Requirements

### FR-1: Remove `HasDayAlreadyBeenProcessedAsync` from the public interface and make it private
`IConsumptionCalculationService` must declare only `ProcessDailyConsumptionAsync`. `HasDayAlreadyBeenProcessedAsync` must become a `private` method on `ConsumptionCalculationService`, retaining its exact current implementation (a direct pass-through to `_repository.HasDailyProcessingBeenRunAsync(date, cancellationToken)`).

**Acceptance criteria:**
- `IConsumptionCalculationService.cs` declares exactly one member: `ProcessDailyConsumptionAsync`.
- `ConsumptionCalculationService.HasDayAlreadyBeenProcessedAsync` is declared `private` (not `public`, not part of any interface).
- `ConsumptionCalculationService.ProcessDailyConsumptionAsync`'s internal call to `HasDayAlreadyBeenProcessedAsync` (line 28, `if (await HasDayAlreadyBeenProcessedAsync(processingDate, cancellationToken))`) is unchanged — it already calls the method as a same-class member, so no code change is needed there beyond the access modifier on the method declaration itself.
- The method body and signature (parameter list, return type `Task<bool>`, default `CancellationToken cancellationToken = default`) are otherwise unchanged.
- No other production code references `HasDayAlreadyBeenProcessedAsync` through `IConsumptionCalculationService` — verified by the pre-refactor grep in Background; this must remain true after the change (i.e., a post-change build must not reveal any caller that was relying on the interface member).

### FR-2: Refactor the direct unit test to verify idempotency indirectly through `ProcessDailyConsumptionAsync`
The existing test `ConsumptionCalculationServiceTests.HasDayAlreadyBeenProcessedAsync_ShouldReturnCorrectValue`, which calls the (soon to be private) method directly on the concrete instance, must be replaced with a test that verifies the same idempotency behavior by calling `ProcessDailyConsumptionAsync` twice for the same date and asserting the second call reports `WasRun: false`.

**Acceptance criteria:**
- The old test `HasDayAlreadyBeenProcessedAsync_ShouldReturnCorrectValue` no longer exists (it would not compile once the method is private, since it calls `service.HasDayAlreadyBeenProcessedAsync(date)` directly on the concrete instance from outside the class).
- A new or adapted test calls `ProcessDailyConsumptionAsync(date)` a first time against a `MockPackingMaterialRepository` that has **not** pre-seeded "already processed" state, and asserts `WasRun == true` (or reuses an existing scenario) to establish a realistic "day gets processed" starting condition, matching how the daily-run idempotency actually gets set in production (a first successful run calls `AddDailyRunAsync`, after which `HasDailyProcessingBeenRunAsync` would report `true` for that date against a real repository).
- The same test (or a second `[Fact]`) then calls `ProcessDailyConsumptionAsync(date)` a second time for the **same date**, using a repository state that reflects "already processed" (either because the mock's `AddDailyRunAsync` from the first call causes subsequent `HasDailyProcessingBeenRunAsync` calls to return `true`, or — if `MockPackingMaterialRepository` doesn't already wire that automatically — by using the existing `SetHasDailyProcessingBeenRun(date, true)` mock seam, consistent with the other tests in the file such as `ProcessDailyConsumptionAsync_ReturnsWasRunFalse_WhenAlreadyProcessed` and `ProcessDailyConsumptionAsync_SecondRun_ReturnsWasRunFalse_WithoutMutating`), and asserts `WasRun == false` on that second call.
- Existing coverage is not weakened: the property under test today — "when the repository reports the day as already processed, no work is performed and `WasRun` is `false`" — must still be asserted after the refactor. Note this exact behavior is *already* independently covered by `ProcessDailyConsumptionAsync_ReturnsWasRunFalse_WhenAlreadyProcessed` and `ProcessDailyConsumptionAsync_SecondRun_ReturnsWasRunFalse_WithoutMutating` (both pre-seed `SetHasDailyProcessingBeenRun(date, true)` and assert `WasRun == false` on a single call). The issue's suggested fix ("call it twice... assert `WasRun: false` on the second call") describes a *two-call* sequence specifically so the test demonstrates idempotency end-to-end through the public API, not just via a pre-seeded mock flag — implement it as a genuine two-call sequence rather than deleting the old test with no replacement, to avoid a silent coverage gap on "second real-world invocation for the same date is a no-op."
- No test in the suite still references `HasDayAlreadyBeenProcessedAsync` as a member callable from outside `ConsumptionCalculationService`.

## Non-Functional Requirements

### NFR-1: No behavior change
This is a pure refactor: the runtime behavior of `ProcessDailyConsumptionAsync` (including its idempotency check) must be bit-for-bit identical before and after. No production logic changes.

### NFR-2: Build and test integrity
- `dotnet build` must succeed with no new warnings introduced by this change (e.g., no now-unused `using` directives left behind, no accidental interface-implementation mismatch).
- `dotnet format` must report no issues on the touched files.
- All existing tests in `ConsumptionCalculationServiceTests` and `ProcessDailyConsumptionHandlerTests` must pass, including the new/adapted test from FR-2.

## Data Model
No data model changes. `PackingMaterialDailyRun`, `PackingMaterialConsumption`, and the repository contract (`IPackingMaterialRepository.HasDailyProcessingBeenRunAsync`) are untouched.

## API / Interface Design
- **Removed from public interface:** `IConsumptionCalculationService.HasDayAlreadyBeenProcessedAsync(DateOnly, CancellationToken)`.
- **Unchanged on public interface:** `IConsumptionCalculationService.ProcessDailyConsumptionAsync(DateOnly, CancellationToken)`.
- **New private member:** `ConsumptionCalculationService.HasDayAlreadyBeenProcessedAsync(DateOnly, CancellationToken)` — same signature and body, `private` instead of implicit interface-public.
- No HTTP/MediatR-facing contracts change; `ProcessDailyConsumptionHandler`, `ProcessDailyConsumptionRequest`, and `ProcessDailyConsumptionResult` are all untouched.

## Dependencies
- None beyond the existing `IPackingMaterialRepository` dependency already used by `ConsumptionCalculationService`.
- No dependency on `PackingMaterialsModule.cs` DI registration changes — `IConsumptionCalculationService` continues to be registered exactly as today; only its member list shrinks.

## Out of Scope
- Any change to `ProcessDailyConsumptionAsync`'s algorithm, logging, or the two-`SaveChangesAsync` idempotency-window behavior documented in the existing code comment (lines 69–74 of `ConsumptionCalculationService.cs`) — that is a separate, already-acknowledged concern and not part of this ISP cleanup.
- Any change to `IPackingMaterialRepository` or `HasDailyProcessingBeenRunAsync`.
- Any change to `ProcessDailyConsumptionHandler` or its tests (confirmed unaffected).
- Broader ISP review of other services in the `PackingMaterials` module — this spec covers only the one finding in issue #4025.

## Open Questions
None.

## Status: COMPLETE
