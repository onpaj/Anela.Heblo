# Architecture Review: Inject `TimeProvider` into `GetBankStatementImportStatisticsHandler`

## Skip Design: true

## Architectural Fit Assessment

This is a pure hygiene fix that brings one outlier handler in line with a pattern the Analytics module has already established twice over. Verified directly in the codebase:

- `InvoiceImportStatisticsTile.cs` (`backend/src/Anela.Heblo.Application/Features/Analytics/DashboardTiles/InvoiceImportStatisticsTile.cs:10-11,22-29`) takes `TimeProvider` as a constructor dependency alongside `IAnalyticsRepository` and calls `_timeProvider.GetUtcNow().Date` at line 46.
- `TimeWindowParser.cs` (`backend/src/Anela.Heblo.Application/Features/Analytics/Services/TimeWindowParser.cs`) does the same via `_timeProvider.GetLocalNow().Date`.
- `GetBankStatementImportStatisticsHandler.cs:13-16,23` is the odd one out: constructor takes only `IAnalyticsRepository`, and line 23 calls `DateTime.UtcNow.Date` directly.
- DI registration is confirmed at `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:131` — `services.AddSingleton(TimeProvider.System);` inside `AddCrossCuttingServices()`. This is a single, application-wide registration; **no new registration is needed**, MediatR's container will resolve the added constructor parameter automatically.

There is no architectural decision to make here — the pattern, the DI wiring, and the test-mocking convention all already exist and are used verbatim elsewhere in the same module. The only job of this review is to confirm there's no reason to deviate, and there isn't.

## Proposed Architecture

### Component Overview

```
GetBankStatementImportStatisticsHandler : IRequestHandler<Request, Response>
 ├── IAnalyticsRepository   (existing dependency, unchanged)
 └── TimeProvider           (new dependency — resolves to TimeProvider.System via
                              existing singleton registration in
                              ServiceCollectionExtensions.cs:131)
```

No new components, no new interfaces, no new registrations. The change is confined to one handler's constructor and one line of its `Handle` method.

### Key Design Decisions

#### Decision 1: Reuse `TimeProvider` exactly as done in `InvoiceImportStatisticsTile`
**Options considered:**
- (a) Inject `TimeProvider` via constructor, matching `InvoiceImportStatisticsTile` / `TimeWindowParser`.
- (b) Introduce a module-level `IClock`/`INowProvider` abstraction.
- (c) Leave as-is and accept the untestable branch.

**Chosen approach:** (a).

**Rationale:** `TimeProvider` (BCL, .NET 8) is already the module's standardized abstraction, already registered once as a singleton, and already has an established mocking convention in tests (`Mock<TimeProvider>` + `.Setup(x => x.GetUtcNow())`). Introducing a second abstraction (b) would create inconsistency within the same module for no benefit. Leaving it as-is (c) is what the brief explicitly asks to fix.

#### Decision 2: No behavior change, testability-only
**Options considered:** Rewrite the default-window logic (e.g., extract to a shared helper shared with `GetInvoiceImportStatisticsHandler`) vs. minimal in-place swap.

**Chosen approach:** Minimal in-place swap of `DateTime.UtcNow.Date` → `_timeProvider.GetUtcNow().Date`. Everything else (the `??` fallback chain, the `DateTimeKind` normalization block at lines 26-30) stays untouched.

**Rationale:** The sibling handler (`GetInvoiceImportStatisticsHandler`) has the identical gap but is tracked separately under issue #3488. Unifying the two into a shared helper now would silently expand scope and couple two independently-tracked fixes. Keep this PR small and reviewable.

## Implementation Guidance

### Directory / Module Structure

No new files or directories except the test file. Everything lives in its existing location:

- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetBankStatementImportStatistics/GetBankStatementImportStatisticsHandler.cs`
- Add: `backend/test/Anela.Heblo.Tests/Features/Analytics/GetBankStatementImportStatisticsHandlerTests.cs` (confirmed this file does not exist today — `GetInvoiceImportStatisticsHandlerTests.cs` exists as a sibling in the same folder and is a reasonable structural neighbor, though `InvoiceImportStatisticsTileTests.cs` in `Features/Analytics/DashboardTiles/` is the closer *behavioral* template for the `Mock<TimeProvider>` setup).

### Interfaces and Contracts

Constructor signature changes from:
```csharp
public GetBankStatementImportStatisticsHandler(IAnalyticsRepository analyticsRepository)
```
to:
```csharp
public GetBankStatementImportStatisticsHandler(IAnalyticsRepository analyticsRepository, TimeProvider timeProvider)
```
with a new `private readonly TimeProvider _timeProvider;` field assigned in the body, matching the existing `_analyticsRepository` field style (confirmed at lines 11, 15 of the current handler).

No MediatR request/response contract changes. No controller changes. No new interfaces.

### Data Flow

Unchanged. `request.EndDate` (nullable) still short-circuits the clock call via `??`. When null, `_timeProvider.GetUtcNow().Date` supplies today's UTC date instead of `DateTime.UtcNow.Date` — same effective value in production (`TimeProvider.System` wraps the real system clock), but now substitutable with a `FakeTimeProvider`/`Mock<TimeProvider>` in tests. `startDate`'s derivation and the subsequent `DateTimeKind.Utc` normalization block (lines 26-30) are untouched and continue to run exactly as before, since `GetUtcNow().Date` produces the same `Unspecified`-kind `DateTime` that `DateTime.UtcNow.Date` did.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Constructor signature change breaks any manual (non-DI-container) instantiation of the handler | Low | Grep confirms the only production instantiation is via MediatR/DI, which resolves `TimeProvider` automatically from the existing singleton — no call-site changes needed. Test file is the only place explicitly constructing the handler, and it's being added, not modified. |
| Divergence between this fix and the still-open sibling gap in `GetInvoiceImportStatisticsHandler` (#3488) | Low | Explicitly out of scope per spec; no shared helper is introduced that would need to track both. Flagged here only so the reviewer doesn't expect this PR to also close #3488. |
| Test asserts on wrong date semantics (e.g., off-by-one from `.Date` truncation) | Low | Mirror the exact fixed-`DateTimeOffset` mocking pattern already proven in `InvoiceImportStatisticsTileTests.cs` (`_fixedDateTime` = `2025-10-14T10:00:00Z`), which already exercises this same `.GetUtcNow().Date` truncation correctly. |

## Specification Amendments

None. The spec (`spec.r1.md`) is accurate against the current codebase in every claim I verified: the handler's current constructor/line-23 content, the `InvoiceImportStatisticsTile`/`TimeWindowParser` reference patterns, and the DI registration at `ServiceCollectionExtensions.cs:131`. No changes needed.

One implementation-guidance note (not a spec defect, just for the developer): FR-3's acceptance criteria reference `GetInvoiceImportStatisticsHandlerTests`-style `Verify(...)` assertions for structure, but the closer template for the `Mock<TimeProvider>` setup itself is `InvoiceImportStatisticsTileTests.cs` (confirmed pattern: `Mock<TimeProvider>` + `.Setup(x => x.GetUtcNow()).Returns(fixedDateTime)`). Use that file as the primary reference for the mock setup, and `GetInvoiceImportStatisticsHandlerTests.cs` as the reference for handler-level `Handle(...)` + repository-verification structure.

## Prerequisites

None. No migrations, no config, no infrastructure changes. `TimeProvider.System` singleton registration already exists; `Moq` is already a test-project dependency. Implementation can start immediately.
