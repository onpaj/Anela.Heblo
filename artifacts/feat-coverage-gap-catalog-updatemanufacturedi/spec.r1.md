# Specification: Unit Test Coverage for UpdateManufactureDifficultyHandler

## Summary
Add comprehensive unit test coverage for `UpdateManufactureDifficultyHandler` to raise line coverage from 22.4% to at least 60% (project filter threshold). Tests must exercise the three uncovered business-rule validation branches (not-found, date-range, overlap) plus the happy path including catalog-cache refresh.

## Background
`UpdateManufactureDifficultyHandler` mutates manufacture difficulty settings which drive production cost calculations over date ranges. The handler currently sits at 22.4% line coverage — the success path and all three guard clauses are untested. Because the entity is a date-ranged configuration with history semantics, regressions in any of the guards cause silent data corruption:

- A bypassed not-found guard would attempt to update a null entity and surface as a NullReferenceException at write time.
- An inverted date-range check would reject valid ranges or accept inverted/empty ranges.
- A broken overlap check (in particular, a missing or wrong `excludeId`) would either reject every update (a record always overlaps with itself) or allow overlapping ranges that corrupt difficulty history.

The weekly coverage-gap routine (CI run #27416879267, 2026-06-14) identified this handler as the highest-priority gap in the Catalog feature module. Effort estimate from the brief: ~1.5 hours.

## Functional Requirements

### FR-1: Happy-path update test
A test must verify that when the target record exists, the request payload is valid, and no overlap exists, the handler updates the entity, refreshes the catalog cache for the affected product, and returns a success result containing the updated DTO.

**Acceptance criteria:**
- `IManufactureDifficultyRepository.GetByIdAsync(id, ct)` is invoked exactly once with the request's `Id`.
- `IManufactureDifficultyRepository.HasOverlapAsync(...)` is invoked with the correct product code, the request's `ValidFrom`/`ValidTo`, and an `excludeId` equal to the request `Id`.
- The entity's mutable fields (`DifficultyValue`, `ValidFrom`, `ValidTo`) are updated to the request values prior to persistence.
- The repository update / save method is invoked exactly once.
- `ICatalogRepository` cache-refresh method is invoked exactly once with the product code of the updated entity.
- Result is `Success` and the returned DTO matches the updated entity values.

### FR-2: Not-found guard test
A test must verify that when `GetByIdAsync` returns `null`, the handler short-circuits with a `ManufactureDifficultyNotFound` error and performs no further work.

**Acceptance criteria:**
- Result is a failure with error code `ManufactureDifficultyNotFound`.
- The error payload includes the requested `Id` in its `Params` (or equivalent diagnostic field used by other handlers in the feature).
- `HasOverlapAsync`, repository update/save, and catalog cache refresh are never invoked.

### FR-3: Invalid date-range test (ValidFrom == ValidTo)
A test must verify that when `ValidFrom` equals `ValidTo`, the handler returns `InvalidValue` and performs no overlap check, update, or cache refresh.

**Acceptance criteria:**
- Result is a failure with error code `InvalidValue`.
- The error `Params` identifies the offending field(s) (`ValidFrom` / `ValidTo`) consistent with how other handlers in the feature report `InvalidValue`.
- `HasOverlapAsync`, repository update/save, and catalog cache refresh are never invoked.

### FR-4: Invalid date-range test (ValidFrom > ValidTo)
A test must verify that a fully reversed range also triggers `InvalidValue`.

**Acceptance criteria:**
- Same as FR-3, with `ValidFrom` strictly greater than `ValidTo`.

### FR-5: Valid boundary date-range test (ValidFrom < ValidTo by 1 day)
A test must verify that the smallest valid range passes the date-range check and proceeds to the overlap check. This locks the inequality direction and prevents a future flip from going unnoticed.

**Acceptance criteria:**
- With `ValidFrom = ValidTo - 1 day`, the handler proceeds past the date-range guard.
- `HasOverlapAsync` is invoked (i.e., the request is not rejected as `InvalidValue`).

### FR-6: Overlap conflict test
A test must verify that when `HasOverlapAsync` returns `true`, the handler returns `ManufactureDifficultyConflict` with the conflicting product code and performs no update or cache refresh.

**Acceptance criteria:**
- Result is a failure with error code `ManufactureDifficultyConflict`.
- The error `Params` includes the product code of the entity being updated.
- Repository update/save and catalog cache refresh are never invoked.

### FR-7: `excludeId` is propagated to overlap check
A test must explicitly verify that the `Id` of the record being updated is passed as `excludeId` to `HasOverlapAsync` so the record does not match itself.

**Acceptance criteria:**
- A captured-argument or `It.Is<>` assertion confirms `excludeId == request.Id` on the `HasOverlapAsync` invocation.
- This assertion lives in the happy-path test (FR-1) or its own dedicated test — but it must exist as a standalone assertion, not implicit in a `Verify(...)` of broader behaviour.

## Non-Functional Requirements

### NFR-1: Coverage threshold
Line coverage for `UpdateManufactureDifficultyHandler.cs` must reach at least 60% (the project's filter threshold). The set of tests above should comfortably exceed this; verify with the coverage report after implementation.

### NFR-2: Test isolation and speed
- Tests use mocked `IManufactureDifficultyRepository` and `ICatalogRepository` — no database, no I/O.
- No shared mutable state between tests; each test constructs its own mocks and handler instance.
- Each test completes in well under 100 ms on developer hardware.

### NFR-3: Style and conventions
- Tests follow the existing xUnit + Moq (or whichever mocking library is used by sibling handler tests in `backend/test/Anela.Heblo.Tests/.../Catalog/`) conventions of the repository.
- AAA (Arrange-Act-Assert) structure, descriptive `Method_Scenario_Expectation` naming consistent with sibling test files.
- DTOs constructed as classes (per repo rule), not records.
- No `Thread.Sleep`, no real time dependencies — pass deterministic dates.

### NFR-4: Determinism
- All dates are explicit `new DateTime(...)` literals; do not use `DateTime.Now`/`DateTime.UtcNow` inside tests or fixtures.

## Data Model
No data-model changes. Tests operate on the existing types:
- `ManufactureDifficultyHistory` (entity) — fields exercised: `Id`, `ProductCode`, `DifficultyValue`, `ValidFrom`, `ValidTo`.
- `UpdateManufactureDifficultyRequest` (request DTO) — fields used: `Id`, `DifficultyValue`, `ValidFrom`, `ValidTo`.
- `UpdateManufactureDifficultyResponse` (response DTO) — fields asserted in the happy path.
- Error codes referenced: `ManufactureDifficultyNotFound`, `InvalidValue`, `ManufactureDifficultyConflict` (from the feature's error-code enum).

Exact type/property names should match the handler's current signatures; tests must not introduce new fields.

## API / Interface Design
No production API changes. Test file is added under the backend test project, mirroring the production folder structure:

```
backend/test/Anela.Heblo.Tests/Features/Catalog/UseCases/UpdateManufactureDifficulty/
  UpdateManufactureDifficultyHandlerTests.cs
```

(Adjust the test project root if the repo uses a different convention — match the location of sibling Catalog handler tests.)

## Dependencies
- xUnit (existing test framework).
- Mocking library currently used by sibling Catalog handler tests (Moq, NSubstitute, or FakeItEasy — match what's already there).
- FluentAssertions if it's the house style in sibling tests; otherwise plain `Assert`.
- No new NuGet packages required.

## Out of Scope
- Integration tests against a real database or in-memory EF context.
- Refactoring `UpdateManufactureDifficultyHandler` itself, even if cleanup opportunities are visible.
- Coverage improvements for adjacent handlers (`Create...`, `Delete...`, `Get...`) — each is its own gap and out of this brief's scope.
- Changes to the error-code enum, DTOs, or repository interfaces.
- Frontend, E2E, or API-surface tests.

## Open Questions
None.

## Status: COMPLETE