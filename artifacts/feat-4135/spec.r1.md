# Specification: Inject TimeProvider into GetPurchaseStockAnalysisHandler

## Summary
`GetPurchaseStockAnalysisHandler` calls `DateTime.UtcNow` directly to compute default date-range boundaries, making that fallback path untestable and inconsistent with the sibling `CreatePurchaseOrderHandler`, which already injects `TimeProvider`. This change injects `TimeProvider` into the handler, replaces the two `DateTime.UtcNow` calls, and adds unit test coverage for the default-fallback behavior using a mocked `TimeProvider`.

## Background
`TimeProvider` is registered in DI as a singleton (`services.AddSingleton(TimeProvider.System)`), and `CreatePurchaseOrderHandler` in the same Purchase module already follows this pattern via `_timeProvider.GetUtcNow()`. `GetPurchaseStockAnalysisHandler` does not follow it, which (a) leaves the default-date-range logic impossible to unit test deterministically and (b) creates an inconsistent pattern within the module that a future contributor could copy incorrectly. This is a mechanical, low-risk refactor identified by the automated arch-review routine — no behavior change is intended for callers that pass explicit `FromDate`/`ToDate`.

## Functional Requirements

### FR-1: Inject TimeProvider into GetPurchaseStockAnalysisHandler
Add a `TimeProvider` constructor dependency to `GetPurchaseStockAnalysisHandler`, stored as a private readonly field, following the exact pattern used in `CreatePurchaseOrderHandler`.

**Acceptance criteria:**
- Constructor accepts `TimeProvider timeProvider` as an additional parameter and assigns it to a `private readonly TimeProvider _timeProvider` field.
- No DI registration change is required or made (existing `AddSingleton(TimeProvider.System)` registration covers it).
- No other constructor parameters, their order, or field assignments are changed.

### FR-2: Replace direct DateTime.UtcNow calls with TimeProvider
Replace the two `DateTime.UtcNow` usages in `Handle` (lines 33–34) with a single `_timeProvider.GetUtcNow().UtcDateTime` value used for both default fallbacks.

**Acceptance criteria:**
- `request.FromDate ?? DateTime.UtcNow.AddYears(-1)` becomes `request.FromDate ?? now.AddYears(-1)`, where `now = _timeProvider.GetUtcNow().UtcDateTime`.
- `request.ToDate ?? DateTime.UtcNow` becomes `request.ToDate ?? now`.
- No remaining direct reference to `DateTime.UtcNow` (or `DateTime.Now`) exists in `GetPurchaseStockAnalysisHandler.cs`.
- All other logic in `Handle` (validation, filtering, sorting, pagination, summary calculation) is unchanged.

### FR-3: Unit test coverage for the default-fallback path
Update `GetPurchaseStockAnalysisHandlerTests` to construct the handler with a mocked `TimeProvider` (matching the `Mock<TimeProvider>` + `.Setup(x => x.GetUtcNow()).Returns(fixedNow)` pattern already used in `CreatePurchaseOrderHandlerTests`), and add test coverage that exercises the default-date-range fallback deterministically.

**Acceptance criteria:**
- The test class's shared handler construction passes a `Mock<TimeProvider>` (or equivalent fake) configured with `GetUtcNow()` returning a fixed `DateTimeOffset`.
- All existing tests in `GetPurchaseStockAnalysisHandlerTests.cs` continue to pass unmodified in behavior (only the handler construction call site changes to include the new dependency).
- At least one new or updated test asserts that when `request.FromDate` and `request.ToDate` are both `null`, the resulting `Summary.AnalysisPeriodStart` equals `fixedNow.AddYears(-1)` and `Summary.AnalysisPeriodEnd` equals `fixedNow`, using the mocked fixed time (not real wall-clock time).
- The new/updated test does not depend on real system time in any assertion.

## Non-Functional Requirements

### NFR-1: Performance
No measurable performance impact; `TimeProvider.GetUtcNow()` is a trivial call with equivalent cost to `DateTime.UtcNow`.

### NFR-2: Security
None. No change to data sensitivity, auth, or external I/O.

## Data Model
No data model changes. No changes to `GetPurchaseStockAnalysisRequest` or `GetPurchaseStockAnalysisResponse` contracts.

## API / Interface Design
No public API surface changes. The MediatR request/response contracts and controller endpoint are unaffected. This is an internal constructor signature change to `GetPurchaseStockAnalysisHandler`, which is only ever resolved via DI (no other call sites construct it directly outside of tests).

## Dependencies
- `TimeProvider` (`System`, .NET 8 built-in), already registered in DI at startup — no new registration needed.
- Existing test pattern in `CreatePurchaseOrderHandlerTests.cs` (`Mock<TimeProvider>`) to mirror for consistency.

## Out of Scope
- Any change to `CreatePurchaseOrderHandler` (already correct).
- Any change to other handlers in the Purchase module or elsewhere that may use `DateTime.UtcNow`.
- Any change to the date-range validation logic, filtering, sorting, or summary calculation beyond substituting the time source.
- Any change to the request/response DTOs or the controller/endpoint layer.

## Open Questions
None.

## Status: COMPLETE
