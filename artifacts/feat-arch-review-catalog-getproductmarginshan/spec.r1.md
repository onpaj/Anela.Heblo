# Specification: Inject TimeProvider into GetProductMarginsHandler

## Summary
Replace the hardcoded `DateTime.Now` call in `GetProductMarginsHandler.MapToMarginDto` with the injected `TimeProvider` abstraction already used across the Catalog module. This is a small, behavior-preserving refactor that restores testability, consistency, and correct UTC semantics for the 13-month history window calculation.

## Background
`GetProductMarginsHandler.MapToMarginDto` (line 189 of `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductMargins/GetProductMarginsHandler.cs`) computes the start of a 13-month history window with:

```csharp
var dateFrom = DateTime.Now.AddMonths(-13);
```

This is the only handler in the Catalog module that does not take `TimeProvider` as a constructor dependency. Peer handlers and services such as `GetCatalogDetailHandler`, `CatalogRepository`, and `MarginCalculationService` consistently use `TimeProvider.GetUtcNow()`.

The current code has three concrete problems:
1. **Testability** — unit tests cannot control time, making any assertion involving the 13-month window brittle or impossible to write deterministically.
2. **Consistency** — the codebase-wide convention is `TimeProvider.GetUtcNow()`; this handler is an outlier.
3. **Correctness** — `DateTime.Now` returns local time while the rest of the system operates in UTC. Around midnight in a non-UTC timezone (the application runs in Czech local time), `dateFrom` can land one month earlier or later than the value computed elsewhere, producing off-by-one month errors in the returned history range.

This finding was filed by the daily arch-review routine on 2026-05-30.

## Functional Requirements

### FR-1: Inject TimeProvider into GetProductMarginsHandler
The handler must accept `TimeProvider` as a constructor dependency and store it in a private readonly field, matching the pattern used by sibling handlers.

**Acceptance criteria:**
- `GetProductMarginsHandler` constructor declares a `TimeProvider timeProvider` parameter alongside the existing `ICatalogRepository` and `ILogger<GetProductMarginsHandler>` parameters.
- The parameter is assigned to a private readonly field (e.g. `_timeProvider`).
- The DI container resolves `TimeProvider` without additional registration changes (it is already registered application-wide; verify and only add registration if missing).
- No other constructor parameters change name, order, or type beyond the addition of `timeProvider`.

### FR-2: Use UTC-based current time for the 13-month window
The hardcoded `DateTime.Now` call in `MapToMarginDto` must be replaced with the injected `TimeProvider`, using UTC semantics consistent with the rest of the module.

**Acceptance criteria:**
- Line 189 (or its equivalent after the constructor change) reads `var dateFrom = _timeProvider.GetUtcNow().DateTime.AddMonths(-13);` (or semantically equivalent — e.g. `UtcDateTime` — as long as the value is in UTC).
- No other call to `DateTime.Now`, `DateTime.UtcNow`, or `DateTimeOffset.Now`/`UtcNow` is introduced anywhere in the handler.
- The handler contains zero references to `DateTime.Now` after the change (grep verifies).

### FR-3: Preserve existing behavior of the history window
The returned 13-month history range must remain functionally equivalent for any caller running in UTC. The only behavioral change permitted is the correction of the local-vs-UTC discrepancy noted in Background.

**Acceptance criteria:**
- The arithmetic remains `AddMonths(-13)` — the window size and direction are unchanged.
- The data type and shape of `MarginDto.HistoryFrom` (or whichever field consumes `dateFrom`) is unchanged.
- No call sites of `GetProductMarginsHandler` need to change.
- Existing integration tests for `GetProductMargins` continue to pass without modification (other than test-setup changes required to inject a `TimeProvider`/`FakeTimeProvider`).

### FR-4: Unit test coverage for the time-window calculation
Add or update unit tests to demonstrate that the 13-month window is computed from the injected `TimeProvider` and uses UTC.

**Acceptance criteria:**
- A unit test uses `Microsoft.Extensions.Time.Testing.FakeTimeProvider` (or the test project's existing fake time provider, if one is already in use) to set a deterministic current UTC time.
- The test asserts that the resulting `dateFrom` equals the injected UTC time minus 13 months, exactly.
- At least one test exercises a timezone-sensitive case (e.g. fake time set to `2026-01-01T00:30:00Z` and verifies the window does not regress to the previous month as it would have with local time in a UTC+1 zone).
- The test follows the existing AAA pattern used in the Catalog handler test suite.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact expected. `TimeProvider.GetUtcNow()` is a virtual call against a system-default implementation in production and is not on a hot path. No additional allocations or I/O.

### NFR-2: Security
No security impact. The change does not alter authentication, authorization, input validation, data exposure, or logging of sensitive values.

### NFR-3: Consistency with codebase conventions
The implementation must match the dependency-injection pattern used by `GetCatalogDetailHandler`, `CatalogRepository`, and `MarginCalculationService` — constructor injection of `TimeProvider`, private readonly field, UTC via `GetUtcNow()`.

### NFR-4: Build & format gates
`dotnet build` and `dotnet format` must pass after the change. All tests touched by the change must pass.

## Data Model
No data-model changes. No database schema, EF entity, or DTO field types are modified. `MarginDto` continues to expose whatever date field it currently exposes; only the source of that value's computation changes.

## API / Interface Design
No public API changes.

- HTTP surface (`GetProductMargins` endpoint): unchanged — same route, same request DTO, same response DTO.
- MediatR contract (`GetProductMarginsQuery` / response): unchanged.
- Internal constructor signature of `GetProductMarginsHandler`: gains one parameter (`TimeProvider timeProvider`). This is an internal DI-resolved type, so no consumers other than the DI container are affected.

## Dependencies
- `TimeProvider` (from `System` namespace, .NET 8) — already in use across the module.
- `Microsoft.Extensions.TimeProvider.Testing` / `FakeTimeProvider` — verify it is already a test dependency in the relevant test project; if not, add it. The project's existing handler tests should indicate the prevailing approach.
- DI registration of `TimeProvider.System` (or equivalent) at composition root — verify it exists; if not, add `services.AddSingleton(TimeProvider.System);` to the same location where other framework services are registered.

## Out of Scope
- Refactoring any other handler, repository, or service that uses `DateTime.Now` or `DateTime.UtcNow` directly. Only `GetProductMarginsHandler` is changed.
- Changing the 13-month window length, the semantics of the history range, or the shape of `MarginDto`.
- Migrating other `DateTime`-typed properties on DTOs to `DateTimeOffset`.
- Adding new endpoints, query parameters, or response fields.
- Adjusting timezone handling at the API boundary or in the frontend.
- Backfilling unit tests for unrelated behavior in `GetProductMarginsHandler`.

## Open Questions
None.

## Status: COMPLETE