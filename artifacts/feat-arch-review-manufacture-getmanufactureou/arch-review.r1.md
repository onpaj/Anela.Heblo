# Architecture Review: Inject TimeProvider into Manufacture Module Handlers (GetManufactureOutput & CalculateBatchPlan)

## Skip Design: true

Backend-only refactor. No UI components, layouts, or visual changes — the OpenAPI surface is untouched per the spec, so no generated TypeScript client changes either.

## Architectural Fit Assessment

This is a small, surgical refactor that brings two outlier handlers in line with an already-established module convention. The Manufacture module has already adopted `TimeProvider` injection in at least four other handlers (`CreateManufactureOrderHandler`, `SubmitManufactureHandler`, `UpdateManufactureOrderHandler`, `UpdateManufactureOrderStatusHandler`) plus the dashboard tiles and workflows. The proposed change mirrors that pattern exactly.

Key integration points already exist:
- **DI registration**: `services.AddSingleton(TimeProvider.System);` at `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:127`. No registration change required.
- **Downstream call sites**: `IManufactureHistoryClient.GetHistoryAsync(DateTime dateFrom, DateTime dateTo, ...)` (`backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureHistoryClient.cs:50`) accepts plain `DateTime` without `Kind` discrimination. `DateRange` (`backend/src/Anela.Heblo.Application/Common/TimePeriods/DateRange.cs:3`) is `record DateRange(DateTime From, DateTime To)` — no `Kind` check either. Passing `DateTimeKind.Unspecified` (the `Kind` returned by `DateTimeOffset.DateTime`) is safe.

There is **one significant inconsistency between the spec and the codebase** that this review must correct: the spec asserts `Microsoft.Extensions.TimeProvider.Testing.FakeTimeProvider` is "already in use across the Manufacture module tests." It is not. A grep across the entire backend test tree shows zero occurrences, and `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` does not reference the package. The project's existing convention — confirmed by the prior arch-review amendment in `docs/superpowers/plans/2026-06-03-inject-timeprovider-getproductmarginshandler.md` (amendment #1) — is to use `Mock<TimeProvider>` via Moq. This review adopts that same constraint.

## Proposed Architecture

### Component Overview

```
   MediatR pipeline
        │
        ▼
┌──────────────────────────────────────────────────┐
│ GetManufactureOutputHandler                      │
│   - IManufactureHistoryClient                    │
│   - IManufactureCatalogSource                    │
│   - ILogger<...>                                 │
│   + TimeProvider _timeProvider   ◄── added       │
└──────────────────────────────────────────────────┘
        │
        │ snapshots once:  var now = _timeProvider.GetUtcNow().DateTime;
        ▼
   uses `now` for:
     • toDate (date-range upper bound passed to GetHistoryAsync)
     • gap-filling loop endDate construction

┌──────────────────────────────────────────────────┐
│ CalculateBatchPlanHandler                        │
│   - IBatchPlanningService                        │
│   - IManufactureCatalogSource                    │
│   - IManufactureClient                           │
│   - ITimePeriodResolver                          │
│   + TimeProvider _timeProvider   ◄── added       │
└──────────────────────────────────────────────────┘
        │
        ▼
   ResolveSalesRanges fallback:
     endDate = request.ToDate ?? _timeProvider.GetUtcNow().DateTime
```

Both handlers receive `TimeProvider.System` from the DI container in production and a `Mock<TimeProvider>` in tests.

### Key Design Decisions

#### Decision 1: Test double — `Mock<TimeProvider>` (Moq), not `FakeTimeProvider`

**Options considered:**
1. Introduce `Microsoft.Extensions.TimeProvider.Testing` and use `FakeTimeProvider`.
2. Use `Mock<TimeProvider>` via the already-referenced Moq package.

**Chosen approach:** Option 2 — `Mock<TimeProvider>` with `mock.Setup(tp => tp.GetUtcNow()).Returns(new DateTimeOffset(...))`.

**Rationale:** The test project (`backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`) does not reference `Microsoft.Extensions.TimeProvider.Testing`, and no other test in the repository uses `FakeTimeProvider`. The prior architecture review for the sibling task (`GetProductMarginsHandler`) explicitly amended its spec to forbid introducing the testing package — preserving a single mocking framework convention across the test suite. Following that precedent keeps both handlers consistent with sibling test files (`CreateManufactureOrderHandlerTests`, `SubmitManufactureHandlerTests`) and avoids adding a NuGet dependency for two test classes.

#### Decision 2: Snapshot the clock once per handler invocation

**Options considered:**
1. Replace every `DateTime.Now` reference with an inline `_timeProvider.GetUtcNow().DateTime` call (multiple clock reads).
2. Snapshot the clock to a local `var now = _timeProvider.GetUtcNow().DateTime;` at the top of each method and reuse.

**Chosen approach:** Option 2.

**Rationale:** The spec calls this out for the gap-filling loop in `GetManufactureOutputHandler` (FR-1, lines 128–129) to "avoid calling the clock twice." Applying the same discipline to the rest of `Handle` is consistent, idiomatic, and matches how `CreateManufactureOrderHandler` already structures its time-dependent fields. It also makes the test setup cleaner: a single `Setup(tp => tp.GetUtcNow())` call suffices for the whole handler.

#### Decision 3: Constructor parameter position — append `TimeProvider` last

**Options considered:**
1. Insert `TimeProvider` after the related infrastructure dependencies (e.g., after the logger).
2. Append `TimeProvider` as the final constructor parameter.

**Chosen approach:** Option 2 — append as the final parameter on both handlers.

**Rationale:** This matches the existing module convention. `CreateManufactureOrderHandler` and `SubmitManufactureHandler` both place `TimeProvider` at or near the end of the parameter list (after repositories, services, and logger). Appending is also a strictly additive change — no existing positional callers in the codebase need adjusting because the handlers are only constructed by the DI container plus the two test classes, both of which are updated as part of this work.

## Implementation Guidance

### Directory / Module Structure

No new files in production code. Files touched:

**Modified (production):**
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureOutput/GetManufactureOutputHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CalculateBatchPlan/CalculateBatchPlanHandler.cs`

**Modified (tests):**
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/GetManufactureOutputHandlerTests.cs` — add a `Mock<TimeProvider>` field, pass it to the constructor in the test fixture, add new time-shift tests.
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/CalculateBatchPlanHandlerTests.cs` — same shape: add `Mock<TimeProvider>`, pass to constructor, add a fallback-path time-shift test.

**Untouched (verified):**
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — already registers `TimeProvider.System` at line 127. No change.
- All MediatR request/response DTOs in both use-case folders.
- The controllers that dispatch these handlers.
- `IManufactureHistoryClient`, `DateRange`, `ITimePeriodResolver`, `IBatchPlanningService` — no interface changes.

### Interfaces and Contracts

**`GetManufactureOutputHandler` constructor (final shape):**

```csharp
public GetManufactureOutputHandler(
    IManufactureHistoryClient manufactureHistoryClient,
    IManufactureCatalogSource catalogSource,
    ILogger<GetManufactureOutputHandler> logger,
    TimeProvider timeProvider)
{
    _manufactureHistoryClient = manufactureHistoryClient;
    _catalogSource = catalogSource;
    _logger = logger;
    _timeProvider = timeProvider;
}
```

**`CalculateBatchPlanHandler` constructor (final shape):**

```csharp
public CalculateBatchPlanHandler(
    IBatchPlanningService batchPlanningService,
    IManufactureCatalogSource catalogSource,
    IManufactureClient manufactureClient,
    ITimePeriodResolver timePeriodResolver,
    TimeProvider timeProvider)
{
    _batchPlanningService = batchPlanningService;
    _catalogSource = catalogSource;
    _manufactureClient = manufactureClient;
    _timePeriodResolver = timePeriodResolver;
    _timeProvider = timeProvider;
}
```

**Field convention (both handlers):** `private readonly TimeProvider _timeProvider;`

**Test fixture wiring (both test classes):**

```csharp
private readonly Mock<TimeProvider> _timeProviderMock;
// ...
_timeProviderMock = new Mock<TimeProvider>();
_timeProviderMock.Setup(tp => tp.GetUtcNow()).Returns(new DateTimeOffset(2026, 03, 15, 10, 0, 0, TimeSpan.Zero));
```

Existing tests that do not care about the clock can either share that default setup or pass `TimeProvider.System` directly (as `SubmitManufactureHandlerTests` does at line 28) — both are valid; prefer the mock to keep the fixture uniform.

### Data Flow

**GetManufactureOutputHandler — time-dependent flow:**

1. `Handle()` snapshots `var now = _timeProvider.GetUtcNow().DateTime;` once.
2. `toDate = now;` `fromDate = now.AddMonths(-request.MonthsBack);`
3. `IManufactureHistoryClient.GetHistoryAsync(fromDate, toDate, null, ct)` consumes both values.
4. Gap-filling loop: `var endDate = new DateTime(now.Year, now.Month, 1);` — built from the same snapshot, not a second clock read.

**CalculateBatchPlanHandler — time-dependent flow:**

1. `Handle()` calls `ResolveSalesRanges(request)`.
2. If `request.TimePeriod` is set → delegate to `_timePeriodResolver.Resolve(...)` (unchanged path, clock not consulted).
3. Else: `var endDate = request.ToDate ?? _timeProvider.GetUtcNow().DateTime;` then `startDate = request.FromDate ?? endDate.AddDays(-DefaultFallbackDays);`.
4. Returned as `new[] { new DateRange(startDate, endDate) }`.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Existing handler tests will fail to compile because they call the old constructor without `TimeProvider`. | High | Update both test fixtures (`GetManufactureOutputHandlerTests.cs` and `CalculateBatchPlanHandlerTests.cs`) in the same PR — straightforward fixture-only edit, no behavioural assertion changes. |
| `DateTimeKind` shift from `Local` → `Unspecified` breaks a downstream consumer (format strings, `ToUniversalTime()` calls). | Low | Grep across the call paths confirms no `Kind`-dependent code: `GetHistoryAsync` (Flexi adapter) treats inputs as raw values; `DateRange` is a plain record; the gap-filling loop only reads `Year`/`Month`. No mitigation work needed beyond the audit FR-4 already requires; document the audit result in the PR description. |
| Semantic regression: report date range silently shifts from operator-local (Europe/Prague) to UTC, producing a 1–2h skew the business hasn't asked for. | Medium | The brief and spec explicitly assert UTC is the correct interpretation and this is the whole point of the fix. Document in the PR that the prior behaviour was non-deterministic ("local" = whatever the OS was set to, which is currently CET on prod but UTC in CI) and that the fix anchors everything to UTC. If the business actually wants Europe/Prague, that is a separate ticket with explicit TZ conversion. |
| Spec-internal contradiction: spec instructs use of `FakeTimeProvider`, which is not in the test project and contradicts the prior arch-review precedent. | Medium | Amend the spec (see "Specification Amendments" below) before implementation begins. |
| Wrong property name `DateRange.End` in the spec misleads the test author. | Low | Amend the spec to use the real property name `DateRange.To`. |

## Specification Amendments

1. **FR-3 (Unit tests): use `Mock<TimeProvider>` instead of `FakeTimeProvider`.**
   The spec asserts `Microsoft.Extensions.TimeProvider.Testing.FakeTimeProvider` is "already in use across the Manufacture module tests." This is incorrect — the package is not referenced anywhere in the backend test tree, and the established convention (per the GetProductMargins arch-review amendment #1) is to use `Mock<TimeProvider>` via the already-referenced Moq package. Rewrite FR-3 acceptance criteria in terms of:
   ```csharp
   var mock = new Mock<TimeProvider>();
   mock.Setup(tp => tp.GetUtcNow())
       .Returns(new DateTimeOffset(2026, 03, 15, 10, 0, 0, TimeSpan.Zero));
   ```
   Do **not** add a `PackageReference` to `Microsoft.Extensions.TimeProvider.Testing`.

2. **FR-3 (Unit tests): `DateRange` exposes `From`/`To`, not `Start`/`End`.**
   The spec's acceptance criterion "asserts the resulting `DateRange.End` equals the injected instant" should read **`DateRange.To`**, matching `backend/src/Anela.Heblo.Application/Common/TimePeriods/DateRange.cs:3` (`public sealed record DateRange(DateTime From, DateTime To);`).

3. **Existing tests must be updated, not just new tests added.**
   The spec's "All new tests pass; all existing tests in `Anela.Heblo.Tests` continue to pass" criterion glosses over the fact that `GetManufactureOutputHandlerTests.cs` (line 25–28) and `CalculateBatchPlanHandlerTests.cs` (line 26–30) currently construct the handlers without a `TimeProvider` argument. Both fixtures must be edited in the same PR to pass a mock — otherwise the project will not compile. Add this as an explicit acceptance criterion under FR-3.

4. **Constructor parameter position — append last.**
   Make the implicit choice explicit: append `TimeProvider` as the final constructor parameter on both handlers, mirroring `CreateManufactureOrderHandler` and `SubmitManufactureHandler`. This avoids ambiguity from the spec's "or matching the position used by sibling handlers in the module — verify before implementing" phrasing.

## Prerequisites

None. All preconditions are already satisfied:

- `TimeProvider.System` is registered as a DI singleton at `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:127`.
- `Moq` is already referenced by `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` (line 24) and used by every Manufacture test class.
- No data migrations, no configuration changes, no infrastructure work.
- No NuGet additions.

Implementation can begin immediately after the amendments above are reflected in the spec.