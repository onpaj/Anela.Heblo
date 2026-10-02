# Architecture Review: Polymorphic Result Shaping for GetDqtRunDetailHandler

## Skip Design: true

Pure backend refactor of an internal dispatch mechanism inside an existing MediatR handler. No new/changed UI components, screens, layouts, or visual design decisions — `GetDqtRunDetailResponse` (the contract the frontend consumes) is unchanged in shape. Designer phase can be a no-op pass-through.

## Architectural Fit Assessment

The spec's approach fits the codebase's established Vertical Slice / module conventions cleanly:

- The DataQuality module already has exactly this polymorphic-dispatch pattern for the *execution* concern: `IDqtJobRunner.CanHandle(DqtTestType)` + `services.GetServices<IDqtJobRunner>().SingleOrDefault(r => r.CanHandle(...))`, used in both `RunDqtHandler` (pre-check) and inside the fire-and-forget `Task.Run` (actual dispatch). `DriftDqtJobRunner.CanHandle` itself derives its answer from an injected `IEnumerable<IDriftDqtComparer>` rather than a hardcoded type list — i.e. the codebase already has the "derive CanHandle from a finer-grained collaborator" idiom this spec needs for the drift family.
- Per `docs/architecture/development_guidelines.md`, DI bindings for module-owned services belong in the module's own `{Feature}Module.cs` (here `DataQualityModule.AddDataQualityModule()`), never centrally — the new `IDqtResultShaper` registrations follow the same file and pattern as the existing `IDqtJobRunner` registrations already there.
- `IDqtRunRepository` already exposes both `GetWithResultsAsync` (used for every test type, unconditionally, before any branching) and `GetDriftResultsAsync` (used only by the drift family). No repository interface changes are needed — shapers just call the method their family already calls today.
- `GetDqtRunDetailResponse` is a contract DTO under `Contracts/` — per the DTO rules it must stay a class (already is) and its shape must not change; this refactor only changes *who populates which fields*, not the fields themselves.

Main integration point: `GetDqtRunDetailHandler`'s constructor gains one new dependency (`IEnumerable<IDqtResultShaper>`), and `DataQualityModule.cs` gains two new registrations. No other module is touched.

## Proposed Architecture

### Component Overview

```
GetDqtRunDetailHandler
    ├─ IDqtRunRepository            (unchanged: GetWithResultsAsync call stays here)
    ├─ IMapper                      (unchanged: still maps DqtRunDto for the Run field)
    └─ IEnumerable<IDqtResultShaper>  (NEW — replaces the two `if` blocks + throw)
            │
            ├─ InvoiceDqtResultShaper   CanHandle: TestType == IssuedInvoiceComparison
            │       └─ IMapper (maps run.Results -> List<InvoiceDqtResultDto>)
            │
            └─ DriftDqtResultShaper     CanHandle: any TestType in _comparers (IDriftDqtComparer)
                    ├─ IDqtRunRepository.GetDriftResultsAsync(...)
                    └─ IMapper (maps drift items -> List<DqtDriftResultDto>)
```

Today's `IDqtJobRunner` family (`InvoiceDqtJobRunner`, `DriftDqtJobRunner`) is left completely untouched — it answers "who *runs* this test type"; `IDqtResultShaper` is a new, separate family answering "who *shapes the detail response* for this test type". They are deliberately not merged into one interface (see Decision 1 below).

### Key Design Decisions

#### Decision 1: A new, separate `IDqtResultShaper` interface + new dedicated classes, not "extend `IDqtJobRunner`" and not "make `InvoiceDqtJobRunner`/`DriftDqtJobRunner` implement it"

**Options considered:**
- **(A)** Extend `IDqtJobRunner` itself with the shaping method, as the brief's suggested fix offers as an alternative.
- **(B)** Keep `IDqtJobRunner` as-is; add a new `IDqtResultShaper` interface and have the *existing* `InvoiceDqtJobRunner` / `DriftDqtJobRunner` classes implement it too (as the brief's primary suggestion proposes).
- **(C)** Keep `IDqtJobRunner` as-is; add a new `IDqtResultShaper` interface implemented by two *new*, dedicated classes (`InvoiceDqtResultShaper`, `DriftDqtResultShaper`) that own only the shaping concern.

**Chosen approach:** (C).

**Rationale:**
- (A) is rejected: `IDqtJobRunner.RunAsync` is invoked from a fire-and-forget background `Task.Run` in `RunDqtHandler` with its own scope; the read-path shaping method has a completely different call shape (needs pagination params, returns response data, is invoked synchronously from an HTTP request). Bolting both onto one interface conflates "execute a background job" with "shape a read-model", which is exactly the kind of interface-bloat SRP is meant to prevent — the brief itself only offers this as a secondary option ("or extend IDqtJobRunner"), not the recommended one.
- (B) is rejected on a **blast-radius / surgical-change** basis specific to this codebase: `InvoiceDqtJobRunner` and `DriftDqtJobRunner` each already have dedicated, constructor-order-sensitive unit tests (`InvoiceDqtJobRunnerTests.cs`, `DriftDqtJobRunnerTests.cs`). Neither class currently depends on `IMapper`. Making them implement `IDqtResultShaper` requires adding an `IMapper` constructor dependency to both, which touches every existing test that constructs them — an unrelated, avoidable change. It also mixes two unrelated responsibilities (running a background comparison job; shaping a paginated read-model for an HTTP response) into one class, which is the same "one class knows too much" smell the arch-review finding is trying to eliminate, just relocated.
- (C) keeps the change surgical: two new, small, single-purpose classes are added; zero existing classes' constructors change; `GetDqtRunDetailHandlerTests.cs` is the only existing test file whose mocking strategy changes (it now mocks `IEnumerable<IDqtResultShaper>` instead of relying on the `if` chain — an intended, in-scope test update per spec NFR-1).
- `CanHandle` on `DriftDqtResultShaper` reuses the exact same "derive from `IEnumerable<IDriftDqtComparer>`" idiom already proven in `DriftDqtJobRunner.CanHandle`, so no hardcoded 4-value enum list is introduced anywhere (satisfies spec FR-3's acceptance criterion).

#### Decision 2: Shaper method signature — mutate the response in place, no new value-object DTO

**Options considered:**
- **(A)** `Task<SomeNewValueObject> ShapeAsync(DqtRun run, int page, int pageSize, CancellationToken ct)` — shaper returns a small internal record/class the handler then copies onto `GetDqtRunDetailResponse`.
- **(B)** `Task ShapeAsync(DqtRun run, GetDqtRunDetailResponse response, int page, int pageSize, CancellationToken ct = default)` — shaper is handed the response instance (already carrying `Run`, `Success = true` set by the handler) and populates only its `Results` / `DriftResults` / `TotalDriftResults` fields.

**Chosen approach:** (B).

**Rationale:** The brief is explicit that the fix should introduce "No new abstractions for the whole system; just moves the `if` chain into the types that own each test type." Introducing a new value-object type purely to shuttle three fields one call frame is exactly the kind of incidental abstraction the brief is warning against, and it's needless indirection for values that already live on a plain response class in the same module. (B) is simpler, has zero new types, and keeps the diff minimal. The soft constraint from the spec ("must not require the shaper to construct `Run`/`Success`/`ErrorCode`") is enforced by convention (XML-doc on the interface) and by the handler being the only place that sets those three fields — the same kind of convention-not-compiler-enforced boundary already used elsewhere in this module (e.g. `IDriftDqtJobRunner`/`IInvoiceDqtJobRunner` marker interfaces alongside the umbrella `IDqtJobRunner`).

## Implementation Guidance

### Directory / Module Structure

All new files go in the existing `Services/` folder of the DataQuality module (same folder as `IDqtJobRunner.cs`, `InvoiceDqtJobRunner.cs`, `DriftDqtJobRunner.cs`):

```
backend/src/Anela.Heblo.Application/Features/DataQuality/Services/
    IDqtResultShaper.cs           (NEW)
    InvoiceDqtResultShaper.cs     (NEW)
    DriftDqtResultShaper.cs       (NEW)
```

`GetDqtRunDetailHandler.cs` (existing file, `UseCases/GetDqtRunDetail/`) is edited in place: constructor gains `IEnumerable<IDqtResultShaper>`, the two `if` blocks + `throw new NotSupportedException(...)` are replaced by a `SingleOrDefault(s => s.CanHandle(run.TestType))` lookup + delegated call.

`DataQualityModule.cs` (existing file) gains two lines:
```csharp
services.AddScoped<IDqtResultShaper, InvoiceDqtResultShaper>();
services.AddScoped<IDqtResultShaper, DriftDqtResultShaper>();
```
placed directly under the existing `IDqtJobRunner` registrations, for locality with the pattern they mirror.

### Interfaces and Contracts

```csharp
// Services/IDqtResultShaper.cs
namespace Anela.Heblo.Application.Features.DataQuality.Services;

/// <summary>
/// Populates the result portion (Results / DriftResults / TotalDriftResults) of a
/// GetDqtRunDetailResponse for a given DqtRun's TestType. Implementations must not set
/// Run, Success, or ErrorCode on the response — those remain owned by
/// GetDqtRunDetailHandler.
/// </summary>
public interface IDqtResultShaper
{
    bool CanHandle(DqtTestType testType);

    Task ShapeAsync(
        DqtRun run,
        GetDqtRunDetailResponse response,
        int page,
        int pageSize,
        CancellationToken ct = default);
}
```

```csharp
// Services/InvoiceDqtResultShaper.cs
public class InvoiceDqtResultShaper : IDqtResultShaper
{
    private readonly IMapper _mapper;
    public InvoiceDqtResultShaper(IMapper mapper) => _mapper = mapper;

    public bool CanHandle(DqtTestType testType) => testType == DqtTestType.IssuedInvoiceComparison;

    public Task ShapeAsync(DqtRun run, GetDqtRunDetailResponse response, int page, int pageSize, CancellationToken ct = default)
    {
        response.Results = _mapper.Map<List<InvoiceDqtResultDto>>(run.Results);
        return Task.CompletedTask;
    }
}
```

```csharp
// Services/DriftDqtResultShaper.cs
public class DriftDqtResultShaper : IDqtResultShaper
{
    private readonly IDqtRunRepository _repository;
    private readonly IEnumerable<IDriftDqtComparer> _comparers;
    private readonly IMapper _mapper;

    public DriftDqtResultShaper(IDqtRunRepository repository, IEnumerable<IDriftDqtComparer> comparers, IMapper mapper)
    {
        _repository = repository;
        _comparers = comparers;
        _mapper = mapper;
    }

    public bool CanHandle(DqtTestType testType) => _comparers.Any(c => c.TestType == testType);

    public async Task ShapeAsync(DqtRun run, GetDqtRunDetailResponse response, int page, int pageSize, CancellationToken ct = default)
    {
        var (driftItems, driftTotal) = await _repository.GetDriftResultsAsync(run.Id, page, pageSize, ct);
        response.DriftResults = _mapper.Map<List<DqtDriftResultDto>>(driftItems);
        response.TotalDriftResults = driftTotal;
    }
}
```

`GetDqtRunDetailHandler.Handle` after the run-not-found check becomes (illustrative, developer may adjust local var names):

```csharp
var shaper = _shapers.SingleOrDefault(s => s.CanHandle(run.TestType));
if (shaper == null)
{
    return new GetDqtRunDetailResponse
    {
        Success = false,
        ErrorCode = ErrorCodes.DqtUnsupportedTestType
    };
}

var response = new GetDqtRunDetailResponse
{
    Success = true,
    Run = _mapper.Map<DqtRunDto>(run)
};
await shaper.ShapeAsync(run, response, request.ResultPage, request.ResultPageSize, cancellationToken);
return response;
```

Note this preserves the exact existing behavior asserted by `GetDqtRunDetailHandlerTests.Handle_UnrecognizedTestType_ReturnsUnsupportedTestTypeError`: `response.Run` stays `null` in the unsupported-type case (the `Run =` assignment only happens after a shaper is found), matching today's behavior where the `NotSupportedException` is thrown before `Run` is ever set on the response.

The outer `try/catch(Exception ex)` around the whole `Handle` body is unchanged and still needs its `ex is NotSupportedException ? ErrorCodes.DqtUnsupportedTestType : ErrorCodes.Exception` ternary — but since no code path now throws `NotSupportedException` for the "no shaper" case (it is handled as a normal `null` check instead), the `is NotSupportedException` branch becomes dead for that condition. **Do not delete it** as a defensive fallback is harmless and keeps `ErrorCodes.DqtUnsupportedTestType` reachable if a future shaper implementation ever throws `NotSupportedException` internally by mistake; leaving the ternary is lower-risk than removing it. Developers should not remove the try/catch or its ternary as part of this change — that is a separate concern from the dispatch mechanism this spec targets.

### Data Flow

1. HTTP request → `GetDqtRunDetailRequest` → `GetDqtRunDetailHandler.Handle`.
2. Handler loads `run` via `_repository.GetWithResultsAsync` (unchanged).
3. Handler resolves the matching `IDqtResultShaper` from the injected `IEnumerable<IDqtResultShaper>` via `CanHandle` (new).
4. Handler builds the response shell (`Run`, `Success = true`) and delegates to `shaper.ShapeAsync(...)`, which performs whatever data access/mapping its family needs and mutates `Results`/`DriftResults`/`TotalDriftResults` on that same response instance.
5. Handler returns the response. Not-found and generic-exception paths are unchanged.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `SingleOrDefault` throws `InvalidOperationException` if two shapers both claim the same `TestType` (registration bug) | Low | Mirrors the exact same risk already accepted for `IDqtJobRunner` resolution in `RunDqtHandler`/`DriftDqtJobRunner` (`.Single(...)` / `.SingleOrDefault(...)`); no new risk class introduced. Covered by existing DI-registration hygiene, not new tooling. |
| Forgetting to register a new `IDqtResultShaper` implementation when a 6th `DqtTestType` is added, silently regressing to the old "unsupported" error | Low | Same shape of risk as forgetting to register a new `IDqtJobRunner` today — already an accepted, precedented risk in this module. Existing `RunDqtHandler` pre-check pattern (`hasRunner` check before starting a run) is the model for how the module already surfaces this class of misconfiguration early; no new mitigation needed beyond what FR-5's acceptance criterion (build + DI resolution) already covers. |
| Test churn in `GetDqtRunDetailHandlerTests.cs` (mocking `IEnumerable<IDqtResultShaper>` instead of the `if` chain) breaks in ways not anticipated by the spec | Low | Scoped explicitly in spec NFR-1 as in-scope test-file changes; developer should keep all 4 existing test cases' *assertions* unchanged and only change *setup* (mock a shaper instead of mocking repository/mapper calls the handler no longer makes directly). |

## Specification Amendments

- FR-1's method signature is now concretely: `Task ShapeAsync(DqtRun run, GetDqtRunDetailResponse response, int page, int pageSize, CancellationToken ct = default)` (Decision 2). This resolves the spec's "implementation detail for architecture/design to decide."
- FR-2 and FR-3's "or a new sibling type, per architecture's judgment" is resolved: **new dedicated classes** `InvoiceDqtResultShaper` and `DriftDqtResultShaper` (Decision 1, option C) — the existing `InvoiceDqtJobRunner`/`DriftDqtJobRunner` are **not** modified to implement `IDqtResultShaper`.
- FR-4's "whether it still goes through an exception is left to architecture/design" is resolved: the no-shaper-matched case becomes a plain `null` check, no exception thrown; the outer catch's `NotSupportedException` branch is kept as a harmless, no-longer-load-bearing defensive fallback (see Implementation Guidance note above) — it is not to be removed as part of this change since removing it is outside this refactor's scope.

## Prerequisites

None. No migrations, config, or infrastructure changes are needed — this is an additive, in-module refactor using only existing repository methods, existing mapping profiles, and the existing module registration file.
