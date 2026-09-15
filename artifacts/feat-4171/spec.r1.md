# Specification: Unit Test Coverage for TransportBoxBaseTile Drill-Down Filters

## Summary
`TransportBoxBaseTile` (`backend/src/Anela.Heblo.Application/Features/Logistics/DashboardTiles/TransportBoxBaseTile.cs`) has 0% line coverage against a 60% threshold. This specification defines the unit tests needed to cover `GenerateDrillDownFilters`'s three branches and the exception path of `LoadDataAsync`, closing the coverage gap without changing production behavior.

## Background
`TransportBoxBaseTile` is an abstract dashboard tile base class used by concrete Logistics dashboard tiles to summarize transport box counts and provide a drill-down filter payload consumed by the frontend to construct the correct filtered URL when a tile is clicked. `GenerateDrillDownFilters` silently produces the filter object; an undetected regression (e.g. the `"ACTIVE"` sentinel being emitted or omitted incorrectly) causes the dashboard to link to the wrong subset of transport boxes — a data-visibility bug with no test currently guarding against it. `LoadDataAsync`'s catch block, which shapes the error response returned to the frontend on repository failure, is also unexercised.

This is a test-only coverage-gap fix filed by the weekly coverage-gap routine (CI run #34699120372). No production code changes are anticipated; if writing tests reveals `TransportBoxBaseTile` cannot be tested without a production change (e.g. it needs to be made testable), that must be flagged as an Open Question / amendment rather than assumed.

## Functional Requirements

### FR-1: Cover `GenerateDrillDownFilters` — single-state branch
When a concrete tile's `FilterStates` contains exactly one `TransportBoxState`, `GenerateDrillDownFilters()` must return an object equivalent to `{ state = <that state>.ToString() }`.
**Acceptance criteria:**
- A test constructs a tile (via a concrete subclass or test double) with `FilterStates` set to a single-element array.
- The test asserts the returned object's `state` property equals the enum value's `.ToString()`.
- Covers `TransportBoxBaseTile.cs` lines implementing the `FilterStates.Length == 1` branch.

### FR-2: Cover `GenerateDrillDownFilters` — multi-state "active" branch (ACTIVE sentinel)
When `FilterStates` has more than one element and **all** of them are non-`Closed`, `GenerateDrillDownFilters()` must return `{ state = "ACTIVE" }`.
**Acceptance criteria:**
- A test uses a `FilterStates` array of 2+ states, none equal to `TransportBoxState.Closed`.
- The test asserts the returned object's `state` property equals the literal string `"ACTIVE"`.
- Confirms the sentinel is emitted only under this condition (paired with FR-3, which confirms it is *not* emitted when a `Closed` state is present).

### FR-3: Cover `GenerateDrillDownFilters` — multi-state "first state" fallback within the multi-state branch
When `FilterStates` has more than one element and **at least one** element equals `TransportBoxState.Closed` (so `isActiveFilter` is false), `GenerateDrillDownFilters()` must return `{ state = FilterStates[0].ToString() }` — the first state in the array, not the sentinel.
**Acceptance criteria:**
- A test uses a `FilterStates` array of 2+ states where at least one entry is `TransportBoxState.Closed`.
- The test asserts the returned object's `state` property equals `FilterStates[0].ToString()` and is **not** `"ACTIVE"`.
- This is the regression guard called out in the brief: it proves the ACTIVE sentinel is withheld correctly when a `Closed` state is mixed in.

### FR-4: Cover `GenerateDrillDownFilters` — empty fallback branch
When `FilterStates` is an empty array (`Length == 0`), `GenerateDrillDownFilters()` must return an empty object (no `state` property, or an object with no members matching the anonymous `new { }`).
**Acceptance criteria:**
- A test uses a `FilterStates` array with zero elements.
- The test asserts the returned object has no `state` member (e.g. via reflection on the anonymous type, or structural/JSON comparison against `new { }`).
- Confirms the fallback fires only when neither of the length-based conditions (`== 1`, `> 1`) matches.

### FR-5: Cover `LoadDataAsync` success path shape (baseline, if not already covered elsewhere)
`LoadDataAsync` must return an object with `status = "success"`, a `data.count` equal to the number of boxes returned by the repository, and `drillDown.filters` equal to the value produced by `GenerateDrillDownFilters()`.
**Acceptance criteria:**
- A test stubs/mocks `ITransportBoxRepository.FindAsync` to return a known set of boxes.
- The test asserts `status == "success"` and `data.count` matches the stubbed box count.
- This is baseline coverage support for FR-6 (the try branch must be exercised for the catch branch to be meaningfully isolated) — include it only if needed to reach the 60% threshold; do not treat it as optional filler if the coverage tool would otherwise still flag the try branch as uncovered.

### FR-6: Cover `LoadDataAsync` — exception path
When `_repository.FindAsync` throws, `LoadDataAsync` must catch the exception and return an object with `status = "error"`, `error = "Nepodařilo se načíst počet boxů"`, and `details` equal to the thrown exception's `Message`.
**Acceptance criteria:**
- A test stubs/mocks `ITransportBoxRepository.FindAsync` to throw a known `Exception` (with a known message).
- The test asserts `status == "error"`, `error == "Nepodařilo se načíst počet boxů"`, and `details` equals the thrown exception's message.
- Covers the entire `catch (Exception ex)` block, currently at 0% coverage per the brief.

## Non-Functional Requirements

### NFR-1: No production behavior change
These are additive unit tests only. `TransportBoxBaseTile.cs` must not be modified unless a test reveals it is genuinely untestable as written (e.g. no way to instantiate a concrete subclass or supply `FilterStates`/`ITransportBoxRepository` in a test harness) — in which case the minimal change needed to make it testable (e.g. exposing a testable constructor path or a test-only concrete subclass in the test project) must be called out explicitly, not made silently.

### NFR-2: Coverage target
The new tests must raise `TransportBoxBaseTile.cs` line coverage from 0.0% to at least the 60% filter threshold. All four `GenerateDrillDownFilters` branches (FR-1–FR-4) and the `LoadDataAsync` catch block (FR-6) are the minimum required to plausibly clear this bar; FR-5 (success path) may already be incidentally covered by the FR-6 test setup and does not need to be a fully separate test if achieving it as one is more natural.

### NFR-3: Test framework and conventions consistency
Tests must use this repository's existing backend test framework, mocking library, and naming/AAA conventions as found in the existing test project. No new testing library may be introduced. (Architect to confirm the concrete test project location, framework, and mocking library from active codebase exploration — see Open Questions.)

## Data Model
No data model changes. Tests exercise `TransportBoxBaseTile`'s existing shape:
- `FilterStates` (`TransportBoxState[]`, abstract property overridden by test double).
- `ITransportBoxRepository.FindAsync(Expression<Func<TransportBox, bool>>, bool includeDetails, CancellationToken)` — needs to be mockable per its interface signature.
- Return type of both `LoadDataAsync` and `GenerateDrillDownFilters` is `object` (anonymous types) — tests must assert against anonymous-type shapes via reflection, dynamic access, or serialization/structural comparison, consistent with how the codebase already asserts against anonymous-typed tile outputs (if any precedent exists).

## API / Interface Design
No API or interface changes. `TransportBoxBaseTile` is `abstract`, so tests need either:
1. An existing concrete subclass in the Logistics DashboardTiles area (if one exists) reused for these tests, or
2. A minimal test-only concrete subclass (e.g. `TestTransportBoxTile : TransportBoxBaseTile`) defined in the test project that exposes/sets `FilterStates` and forwards the base constructor's `ITransportBoxRepository`, or
3. A means to override `FilterStates` and invoke `GenerateDrillDownFilters` directly for isolated branch tests, separate from a `LoadDataAsync`-level test that only needs the repository mocked.

Which of these already exists in the codebase (e.g. a `TestTile`/fake tile fixture) must be confirmed during architecture/implementation — this is left to the architect's active exploration per FR/NFR above.

## Dependencies
- Existing `ITransportBoxRepository` interface (Domain layer) — must be mockable (interface, so any mocking library works).
- Existing `TransportBoxState` enum (Domain layer) — need to confirm it has a `Closed` member and at least one other non-`Closed` member to construct FR-2/FR-3 test data.
- Repository's existing backend test project and its mocking library (exact identity to be confirmed by architect via codebase exploration).

## Out of Scope
- Any change to dashboard tile business logic or the `"ACTIVE"` sentinel semantics.
- Coverage of concrete subclasses of `TransportBoxBaseTile` beyond what is needed to exercise the base class (e.g. no requirement to test every real concrete tile's `Title`/`Description`/`FilterStates` values).
- Frontend drill-down consumption of these filter payloads.
- Any other file's coverage gap.

## Open Questions
None.

## Status: COMPLETE
