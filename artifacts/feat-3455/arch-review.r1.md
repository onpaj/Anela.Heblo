# Architecture Review: Open/Closed DQT Runner Dispatch (RunDqtHandler / GetDqtRunDetailHandler)

## Skip Design: true

## Architectural Fit Assessment

This is a narrow, well-contained internal refactor inside a single vertical slice (`DataQuality`), with no HTTP contract, DTO, DB schema, or frontend surface change. It extends an already-proven pattern in this codebase — the "discriminator/predicate + resolve-from-`IEnumerable<T>`-collection" shape used by `IDriftDqtComparer` (`TestType` property, resolved via `.SingleOrDefault` in `DriftDqtJobRunner`) — up one dispatch level, from "which comparer handles this drift sub-type" to "which runner handles this top-level test type." That symmetry is the right instinct and the spec's proposed `IDqtJobRunner` sits naturally alongside the existing `Services/` folder contents (`IDriftDqtComparer`, `IDriftDqtJobRunner`, `IInvoiceDqtJobRunner`, plus their implementations).

Verified against the actual source (not assumed):
- `RunDqtHandler.Handle` (lines 46–59) does exactly the binary `if request.TestType == IssuedInvoiceComparison / else` dispatch described, inside the fire-and-forget `Task.Run` that already creates its own DI scope via `_scopeFactory.CreateScope()`.
- `GetDqtRunDetailHandler.Handle` (lines 38–57) has the described `if (invoice) return ...` with unconditional fallthrough to drift-shaped response — no `else`, confirmed.
- `DriftDqtJobRunner` (`Services/DriftDqtJobRunner.cs`) already injects `IEnumerable<IDriftDqtComparer>` and resolves via `_comparers.SingleOrDefault(c => c.TestType == run.TestType) ?? throw new InvalidOperationException(...)` — this is the existing pattern FR-1/FR-3 extend upward.
- `DataQualityModule.AddDataQualityModule` registers `IInvoiceDqtJobRunner → InvoiceDqtJobRunner`, `IDriftDqtJobRunner → DriftDqtJobRunner`, and two `IDriftDqtComparer` implementations (`ProductPairingDqtComparer`, `StockWriteBackDqtComparer`) — confirmed additive registration is straightforward, no conflicting bindings.
- `DqtTestType` (`Domain/Features/DataQuality/DqtTestType.cs`) has exactly the 3 values the spec describes.
- `ErrorCodes.cs` reserves `22XX` for DataQuality, currently populated with `DqtRunNotFound = 2201`, `DqtInvalidDateRange = 2202`, `DqtExternalServiceError = 2203`. Next free slot: `2204`.
- `IInvoiceDqtJobRunner` and `IDriftDqtJobRunner` are referenced only inside the `DataQuality` module: by their own implementation classes, by `RunDqtHandler`, and by `InvoiceDqtJobTests.cs` / `ProductPairingDqtJobTests.cs` / `StockWriteBackDqtJobTests.cs`, which construct/test `InvoiceDqtJobRunner` and `DriftDqtJobRunner` directly through their narrow interfaces. No other module or test depends on them — confirms removing them is genuinely optional (not a hidden requirement) and keeping them costs nothing.

No violation of `development_guidelines.md` module-boundary rules is introduced: everything stays inside `Anela.Heblo.Application/Features/DataQuality/Services/`, DI registration stays inside `DataQualityModule.cs` (consistent with ADR-004 — repo/service bindings live in the owning module, not `PersistenceModule`), and no cross-module contract is touched.

## Proposed Architecture

### Component Overview

```
DataQuality/Services/
├── IDqtJobRunner.cs          [NEW]  shared dispatch-capable interface
├── IInvoiceDqtJobRunner.cs           unchanged, retained
├── IDriftDqtJobRunner.cs             unchanged, retained
├── InvoiceDqtJobRunner.cs    [MOD]  now implements IInvoiceDqtJobRunner, IDqtJobRunner
├── DriftDqtJobRunner.cs      [MOD]  now implements IDriftDqtJobRunner, IDqtJobRunner
├── IDriftDqtComparer.cs              unchanged (existing pattern this mirrors)
├── ProductPairingDqtComparer.cs      unchanged
└── StockWriteBackDqtComparer.cs      unchanged

DataQuality/UseCases/RunDqt/RunDqtHandler.cs           [MOD] dispatch via IEnumerable<IDqtJobRunner>
DataQuality/UseCases/GetDqtRunDetail/GetDqtRunDetailHandler.cs [MOD] explicit 3-way, fail-fast

DataQuality/DataQualityModule.cs   [MOD] additive IDqtJobRunner registrations
Application/Shared/ErrorCodes.cs   [MOD] one new entry, DataQuality's 22XX range
```

No new files beyond `IDqtJobRunner.cs`. No new folders.

### Key Design Decisions

#### Decision 1: `CanHandle(DqtTestType)` vs. a `TestType` property on the shared runner interface

**Options considered:**
- **A — `bool CanHandle(DqtTestType testType)`** (spec's FR-1 proposal).
- **B — single `DqtTestType TestType { get; }` property**, mirroring `IDriftDqtComparer` exactly, with `DriftDqtJobRunner.TestType` forced to pick/return one arbitrary value (wrong) or throw (useless) since it legitimately covers ≥2 types.
- **C — `TestType` property for genuinely single-type runners, plus a special-cased "catch-all"/"default" runner concept** that always matches whatever nothing else claimed.

**Chosen approach:** **A — `CanHandle(DqtTestType)`.** Confirmed as final, not to be re-litigated.

**Rationale:**
- Option B is provably wrong given the actual `DriftDqtJobRunner` shape verified above: it is one registered instance fronting an injected `IEnumerable<IDriftDqtComparer>` that today covers `ProductPairing` and `StockWriteBackReconciliation`, and will cover any future drift type the moment a new `IDriftDqtComparer` is registered — with zero change to `DriftDqtJobRunner` itself. A scalar `TestType` property has no correct value to return for "I handle N things"; forcing one would either be a lie (return the first) or require `DriftDqtJobRunner` to duplicate the set of types its `_comparers` collection already encodes — a second source of truth that silently drifts out of sync the moment someone adds an `IDriftDqtComparer` without also touching a hardcoded type list. This is exactly the bug class the whole spec exists to eliminate, just moved one level up — unacceptable.
- Option C ("catch-all runner") is worse than A, not just unnecessary: a catch-all inverts the fail-fast goal that is the entire point of this spec. A catch-all silently absorbs any unrecognized `DqtTestType`, which is precisely the current bug (`DriftDqtJobRunner` today behaves like an accidental catch-all for "everything that isn't invoice"). Reintroducing a deliberate catch-all just formalizes the anti-pattern under a nicer name. Reject.
- Option A is a direct, minimal generalization of the existing `IDriftDqtComparer.TestType` pattern's *intent* ("can you handle this discriminator value") without inheriting its structural assumption (exactly one type per instance). `DriftDqtJobRunner.CanHandle` delegates to `_comparers.Any(c => c.TestType == testType)` — single source of truth preserved, zero duplication, and `InvoiceDqtJobRunner.CanHandle` is a trivial one-line equality check with no loss of clarity for the single-type case. This is the standard predicate-based strategy-resolution shape and is the correct generalization here.

#### Decision 2: Keep `IInvoiceDqtJobRunner` / `IDriftDqtJobRunner` as separate interfaces, additive to `IDqtJobRunner`

**Options considered:**
- **A — Additive**: keep both narrow interfaces, add `IDqtJobRunner` as a third, orthogonal interface implemented by both classes (spec's proposal).
- **B — Replace**: delete the two narrow interfaces, use only `IDqtJobRunner` everywhere.

**Chosen approach:** **A — Additive.** Confirmed as final.

**Rationale:** Verified via grep that `IInvoiceDqtJobRunner` and `IDriftDqtJobRunner` are consumed only within the `DataQuality` module — by `RunDqtHandler` (the one call site this spec changes) and by the existing unit tests `InvoiceDqtJobTests.cs`, `ProductPairingDqtJobTests.cs`, `StockWriteBackDqtJobTests.cs`, which construct and exercise `InvoiceDqtJobRunner`/`DriftDqtJobRunner` through their narrow, single-purpose interfaces (this is Interface Segregation working as intended: those tests only need `RunAsync`, not `CanHandle`, and shouldn't have to depend on the multi-implementation interface to get it). Option B would force those three test files to be rewritten for no behavioral gain and would make `RunAsync`-only call sites depend on a `CanHandle` method they never use. Option A costs one extra `: IInterfaceName` on two class declarations and two extra DI lines — negligible, fully backward compatible, and correctly expresses "these two facts about a runner are separable capabilities" (ISP). No call site outside this spec is affected either way, so there is no compatibility risk in choosing on merits.

#### Decision 3: File path, namespace, and DI registration shape

**Chosen approach:**
- New file: `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/IDqtJobRunner.cs`
- Namespace: `Anela.Heblo.Application.Features.DataQuality.Services` — identical to `IDriftDqtComparer`, `IDriftDqtJobRunner`, `IInvoiceDqtJobRunner` in the same folder; no new namespace segment.
- DI: two additional `services.AddScoped<IDqtJobRunner, InvoiceDqtJobRunner>();` / `services.AddScoped<IDqtJobRunner, DriftDqtJobRunner>();` lines added directly beneath the existing `AddScoped<IInvoiceDqtJobRunner, ...>()` / `AddScoped<IDriftDqtJobRunner, ...>()` lines in `DataQualityModule.AddDataQualityModule` — keeps all four registrations for the two classes visually adjacent, so a reader immediately sees "this class is bound under two interfaces" rather than discovering the second binding elsewhere in the file.

**Rationale:** Matches existing file/namespace convention exactly (verified by listing the `Services/` folder); DI binding stays inside the owning module per ADR-004, no `PersistenceModule` involvement (this isn't a repository binding anyway, but the "binding lives with the slice" principle applies equally).

#### Decision 4: `GetDqtRunDetailHandler` dispatch — explicit `if/if/throw` vs. metadata-driven

**Options considered:**
- **A — Explicit `if (invoice) / if (known drift types) / throw NotSupportedException`** (spec FR-4, using `run.TestType is ProductPairing or StockWriteBackReconciliation`).
- **B — Metadata-driven**: extend `IDqtJobRunner` (or `IDriftDqtComparer`) with a "which result shape does this type produce" capability (e.g. an enum `ResultShape { Invoice, Drift }` or similar) and have the handler consult that instead of a hardcoded type list.

**Chosen approach:** **A — Explicit `if/if/throw`, exactly as FR-4 specifies.** Confirmed as final; this closes the spec's Open Question — do not leave it for the implementer to re-decide.

**Rationale:** There are exactly two result *shapes* in the entire domain today (`InvoiceDqtResultDto` list vs. `DqtDriftResultDto` list + total count), driven by a fundamentally different data source (`run.Results` navigation vs. `_repository.GetDriftResultsAsync`), not by an open-ended per-type variation. Option B would add a second cross-cutting abstraction (a "result shape" concept layered on top of `IDqtJobRunner`/`IDriftDqtComparer`) to serve exactly two cases, one of which (`Invoice`) will likely never grow a sibling — introducing indirection with no current or foreseeable second consumer is speculative generality, which this codebase's guidelines explicitly discourage ("don't create shared services/abstractions" beyond what's needed). Critically, Option A already delivers the property that actually matters — **the fail-fast guarantee** — because any `DqtTestType` not explicitly listed now throws `NotSupportedException` instead of silently falling through. The remaining "gap" (a human must add a new drift type to this `if` list) is explicitly and correctly named as an acceptable, smaller Open/Closed residue in the spec's own Open Questions — correct call, confirmed. If a third *shape* (not just a third drift-category type) is ever introduced, that is the trigger to revisit toward Option B, not before.

Implementation detail confirmed: the `NotSupportedException` propagates up to the handler's existing outer `try/catch (Exception ex)` (lines 59–67 in current source) unchanged — no new try/catch needed inside the dispatch block itself.

#### Decision 5: `ErrorCodes` for the `GetDqtRunDetailHandler` fail-fast path

**Options considered:**
- **A — Reuse `ErrorCodes.Exception` (0099)**, spec's stated default, "minimize surface area."
- **B — Add one new, specific code** in DataQuality's own `22XX` range.

**Chosen approach:** **B — Add `DqtUnsupportedTestType = 2204`, `[HttpStatusCode(HttpStatusCode.InternalServerError)]`.** This overrides the spec's stated default. Confirmed as final.

**Rationale:** This is a "pick a default consistent with existing conventions" call, and the actual convention in `ErrorCodes.cs` — verified by reading the full enum — is that **every module gives every anticipated, nameable failure mode its own code**; the generic `Exception = 0099` is reserved for the outer catch-all's truly unclassified case, not reused deliberately by handler logic that already knows exactly what went wrong. DataQuality itself already follows this: `DqtRunNotFound`, `DqtInvalidDateRange`, `DqtExternalServiceError` are all specific, none of DataQuality's own anticipated failure modes fall back to `Exception`. FR-4's own design intent is "make this failure mode loud and diagnosable" (that's the entire reason for throwing a distinctly-named `NotSupportedException` instead of falling through) — collapsing that back into the generic `Exception` code at the API boundary throws away the diagnostic signal the exception type was deliberately chosen to preserve; logs/monitoring would be unable to distinguish "unrecognized `DqtTestType`" from any unrelated bug caught by the same outer `catch`. Cost of the alternative is one enum line — trivially minimal, and directly consistent with the module's own established pattern rather than the generic exception the spec defaulted to for scope-minimization reasons that don't actually hold up against the codebase's real convention.
- Status code: `InternalServerError`, not `BadRequest`/`NotFound` — the caller did nothing wrong (they passed a valid, persisted `DqtRun.Id`); the defect is a server-side gap where a `DqtTestType` enum value exists with no result-shaping logic registered for it. This mirrors `ConfigurationError` (`0012`, `InternalServerError`) — same "server is missing a piece of its own wiring" semantics.

#### Decision 6: `SingleOrDefault` vs. `FirstOrDefault` in `RunDqtHandler`'s new resolution

**Chosen approach:** **`SingleOrDefault`, exactly as spec FR-3 specifies.** Confirmed as final.

**Rationale/tradeoff:** With `CanHandle` being an arbitrary boolean predicate (unlike `IDriftDqtComparer.TestType`'s simple equality-per-instance), it is structurally easier for two runners to accidentally both claim the same `DqtTestType` — e.g., a future drift-adjacent runner copy-pasted from `DriftDqtJobRunner` that queries an overlapping comparer set. `SingleOrDefault` turns that overlap into an immediate, loud `InvalidOperationException` at dispatch time (its built-in "more than one match" check), which is exactly the failure mode this entire spec is designed to surface rather than hide. `FirstOrDefault` would silently pick registration order — non-deterministic-looking, hard to debug, and the same "silent mis-routing" anti-pattern FR-1–FR-4 exist to eliminate. The cost is a second full enumeration of a 2-element `IEnumerable<IDqtJobRunner>` in the worst case (`SingleOrDefault` scans fully to detect a second match even after finding the first) — for a collection of 2 today and realistically low single digits for the foreseeable future, this is immeasurable; NFR-1 in the spec already correctly dismisses this. Keep `SingleOrDefault`.

## Implementation Guidance

### Directory / Module Structure
No structural changes beyond one new file. All changes stay within `backend/src/Anela.Heblo.Application/Features/DataQuality/` (`Services/`, `UseCases/RunDqt/`, `UseCases/GetDqtRunDetail/`, `DataQualityModule.cs`) plus one entry in the shared `Anela.Heblo.Application/Shared/ErrorCodes.cs`. Test changes stay within `backend/test/Anela.Heblo.Tests/Features/DataQuality/`.

### Interfaces and Contracts

```csharp
// backend/src/Anela.Heblo.Application/Features/DataQuality/Services/IDqtJobRunner.cs
namespace Anela.Heblo.Application.Features.DataQuality.Services;

public interface IDqtJobRunner
{
    bool CanHandle(DqtTestType testType);
    Task RunAsync(Guid runId, CancellationToken ct = default);
}
```
(add `using Anela.Heblo.Domain.Features.DataQuality;` for `DqtTestType`, matching `IDriftDqtComparer.cs`'s existing using.)

```csharp
// InvoiceDqtJobRunner.cs — class declaration only
public class InvoiceDqtJobRunner : IInvoiceDqtJobRunner, IDqtJobRunner
{
    // ... existing members unchanged ...
    public bool CanHandle(DqtTestType testType) => testType == DqtTestType.IssuedInvoiceComparison;
    // existing RunAsync(Guid dqtRunId, CancellationToken cancellationToken = default) already satisfies
    // IDqtJobRunner.RunAsync(Guid runId, CancellationToken ct = default) — parameter names don't need to match.
}
```

```csharp
// DriftDqtJobRunner.cs — class declaration only
public class DriftDqtJobRunner : IDriftDqtJobRunner, IDqtJobRunner
{
    // ... existing members unchanged ...
    public bool CanHandle(DqtTestType testType) => _comparers.Any(c => c.TestType == testType);
    // existing RunAsync(Guid runId, CancellationToken ct = default) already satisfies IDqtJobRunner as-is.
}
```

```csharp
// DataQualityModule.cs — additive, directly beneath the existing single-interface registrations
services.AddScoped<IInvoiceDqtJobRunner, InvoiceDqtJobRunner>();
services.AddScoped<IDriftDqtJobRunner, DriftDqtJobRunner>();
services.AddScoped<IDqtJobRunner, InvoiceDqtJobRunner>();
services.AddScoped<IDqtJobRunner, DriftDqtJobRunner>();
```

```csharp
// ErrorCodes.cs — inside "// DataQuality module errors (22XX)" block, after DqtExternalServiceError = 2203
[HttpStatusCode(HttpStatusCode.InternalServerError)]
DqtUnsupportedTestType = 2204,
```

### Data Flow

`RunDqtHandler.Handle` — inside the existing fire-and-forget `Task.Run`, replace the `if/else` (lines 49–58) with:
```csharp
using var scope = _scopeFactory.CreateScope();
var runner = scope.ServiceProvider
    .GetServices<IDqtJobRunner>()
    .SingleOrDefault(r => r.CanHandle(request.TestType))
    ?? throw new InvalidOperationException($"No IDqtJobRunner registered for {request.TestType}");
await runner.RunAsync(run.Id);
```
No change to anything before or after this block (synchronous `try/catch`, response construction, logging).

`GetDqtRunDetailHandler.Handle` — replace lines 38–57 (the `if (invoice) return ...` + unconditional drift fallthrough) with the three-branch structure from spec FR-4 verbatim: `if (IssuedInvoiceComparison) return invoice-shaped;` then `if (run.TestType is ProductPairing or StockWriteBackReconciliation) return drift-shaped;` then `throw new NotSupportedException($"No result-shaping logic registered for DqtTestType {run.TestType}");`. The existing outer `catch (Exception ex)` at lines 59–67 needs no modification — it already logs and returns `Success = false`; only its `ErrorCode` in this new path changes to `ErrorCodes.DqtUnsupportedTestType` instead of the currently-generic `ErrorCodes.Exception`. This requires either (a) a nested `try/catch (NotSupportedException)` inside the new dispatch block that maps to `DqtUnsupportedTestType` before falling into the outer generic handler, or (b) — cleaner — inspecting `ex is NotSupportedException` in the existing outer `catch` to pick the error code. Prefer (b): add one conditional in the existing `catch (Exception ex)` block:
```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "Error getting DQT run detail for {Id}", request.Id);
    return new GetDqtRunDetailResponse
    {
        Success = false,
        ErrorCode = ex is NotSupportedException ? ErrorCodes.DqtUnsupportedTestType : ErrorCodes.Exception
    };
}
```
This keeps the dispatch block itself simple (just three branches + a throw) and centralizes the exception→error-code mapping in the one place that already owns it, rather than duplicating catch logic.

Test updates (FR-5), concretely:
- `RunDqtHandlerTests`: the constructor currently stubs only `IInvoiceDqtJobRunner` on the mocked `IServiceProvider` (`sp.GetService(typeof(IInvoiceDqtJobRunner))`). Since `RunDqtHandler` will call `scope.ServiceProvider.GetServices<IDqtJobRunner>()`, the test setup must instead stub `sp.GetService(typeof(IEnumerable<IDqtJobRunner>))` (this is what `GetServices<T>()` resolves under the hood) to return a list containing mock `IDqtJobRunner` instance(s) with `CanHandle` stubbed appropriately. Add: (1) an invoice-type test asserting the invoice-runner mock's `RunAsync` is invoked when `CanHandle` returns true for it; (2) a drift-type test, same shape, for a mock representing `DriftDqtJobRunner`; (3) a no-match test with a runner set where no mock's `CanHandle` returns true for the given `TestType`, asserting `InvalidOperationException` is thrown inside the background task (note: since this is fire-and-forget, this likely requires either exposing a test hook / awaiting the task via a captured reference, or restructuring the fire-and-forget task to be awaitable in test builds — do not silently skip this acceptance criterion; if the existing fire-and-forget shape makes this genuinely impractical to assert directly, the test should at minimum verify no runner's `RunAsync` was called, via `Task.Delay` + mock `Verify(Times.Never)`, and this limitation should be called out in the PR description, not hidden).
- `GetDqtRunDetailHandlerTests`: add a test constructing a `DqtRun` with a `TestType` value outside `{IssuedInvoiceComparison, ProductPairing, StockWriteBackReconciliation}` — since `DqtTestType` has no such value today, use `(DqtTestType)999` (an explicit out-of-range cast; this is the standard way to test enum-dispatch fail-fast paths without modifying the enum) — asserting `Success == false` and `ErrorCode == ErrorCodes.DqtUnsupportedTestType`.
- Existing invoice-path and drift-path tests in both files: no assertion changes needed beyond whatever mock-wiring changes FR-3 forces in `RunDqtHandlerTests` (see above); `GetDqtRunDetailHandlerTests`' existing `Handle_RunExists_ReturnsMappedDetail` test needs no change since `IssuedInvoiceComparison` still hits the first `if` branch unchanged.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `RunDqtHandlerTests` mock setup (`sp.GetService(typeof(IInvoiceDqtJobRunner))`) silently stops matching after the handler switches to `GetServices<IDqtJobRunner>()`, causing `SingleOrDefault` to throw `InvalidOperationException` (no match) inside the fire-and-forget task at runtime, while the test itself still passes because it never observes that exception | Medium | Test updates in FR-5 must change the mock to stub `IEnumerable<IDqtJobRunner>` resolution, not just add new tests; treat this as a required edit to the *existing* passing tests, not purely additive (see Data Flow section above) |
| Fire-and-forget `Task.Run` still swallows the new `InvalidOperationException` exactly as it swallows today's runner failures — an unmatched `TestType` at `RunDqtHandler` dispatch time produces `Success = true` in the HTTP response with a `DqtRun` that silently never completes or fails | Low (explicitly Out of Scope, but worth flagging to the human reviewer since it weakens this spec's own "fail loud" goal for the `RunDqtHandler` half specifically) | No code change required by this spec (confirmed Out of Scope per spec and this review). If observability of stuck/never-started runs becomes a real problem, the follow-up is a `run.Fail(...)` + `SaveChangesAsync` call wrapping the whole `Task.Run` body in a `try/catch`, tracked as a separate future spec — do not fold into this one |
| `(DqtTestType)999`-style out-of-range enum casts in tests are inherently a bit fragile/unusual for readers unfamiliar with the pattern | Low | Add a one-line comment at the test site explaining why an explicit out-of-range cast is used (no such `DqtTestType` value exists yet — that's the point) |
| Two new interface implementations (`IDqtJobRunner` on both classes) plus two new DI lines increase the chance of a copy-paste DI registration bug (e.g. binding `IDqtJobRunner` to the wrong concrete class) going unnoticed | Low | FR-2's acceptance criterion ("resolving `IEnumerable<IDqtJobRunner>` yields exactly one `InvoiceDqtJobRunner`, one `DriftDqtJobRunner`") should be asserted by an actual DI-container integration test (resolve real `AddDataQualityModule()` registrations, not mocks), not just eyeballed — check whether `DataQualityModuleTests.cs` or similar already exists for this kind of container-resolution assertion pattern elsewhere in the codebase, and follow it |

## Specification Amendments

The spec (`spec.r1.md`) is implementation-ready as written for FR-1 through FR-3 and Decision 1/2/3/6 above with no changes needed. This review makes the following two amendments to close the spec's own Open Questions, superseding the corresponding spec text:

1. **Open Question 1** ("should `GetDqtRunDetailHandler` be metadata-driven?") — **Resolved: No.** Use FR-4's explicit `if/if/throw` exactly as written. See Decision 4 above. Do not implement a metadata-driven `IDqtJobRunner`/`IDriftDqtComparer` result-shape abstraction as part of this spec.
2. **Open Question 2** ("should the fail-fast path use a distinct `ErrorCodes` value?") — **Resolved: Yes**, overriding the spec's stated default. Add `ErrorCodes.DqtUnsupportedTestType = 2204` (`[HttpStatusCode(HttpStatusCode.InternalServerError)]`) in the DataQuality `22XX` block of `Application/Shared/ErrorCodes.cs`, and use it (not `ErrorCodes.Exception`) when the caught exception in `GetDqtRunDetailHandler` is a `NotSupportedException`. See Decision 5 above for the exact `catch`-block wiring. This changes FR-4's acceptance criteria text ("`ErrorCode = ErrorCodes.Exception`") — the correct final acceptance criterion is `ErrorCode = ErrorCodes.DqtUnsupportedTestType`.
3. **Open Question 3** ("should `RunDqtHandler` persist `run.Fail(...)` on no-match?") — **Resolved: No, confirmed Out of Scope**, exactly as the spec's own default assumption states. No amendment; flagged again as a residual risk in the Risks table above for reviewer awareness, not for action in this spec.

FR-5's acceptance criteria bullet referencing `ErrorCode = ErrorCodes.Exception` for the `GetDqtRunDetailHandlerTests` fail-fast test must be read as `ErrorCode = ErrorCodes.DqtUnsupportedTestType` per amendment 2 above.

## Prerequisites
None. No other in-flight spec or migration blocks this work. `DqtTestType`, `IDriftDqtComparer`, `DriftDqtJobRunner`, `InvoiceDqtJobRunner`, and `DataQualityModule.cs` are all in their currently-described state with no pending changes found elsewhere in the branch.
