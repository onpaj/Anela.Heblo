# Specification: Unit test coverage for GetTransportBoxesHandler state-filter logic

## Summary
`GetTransportBoxesHandler.Handle` contains an uncovered three-way branch that translates the incoming `State` string filter into either the special-case "active" flag, a parsed `TransportBoxState` enum, or no filter at all. This task adds a focused unit test suite covering that branch and the surrounding mapping/response behavior, raising line coverage above the 60% threshold. No production code changes are expected unless a real defect is discovered while writing tests (none is currently confirmed).

## Background
Coverage-gap tooling flagged `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GetTransportBoxes/GetTransportBoxesHandler.cs` at 20% line coverage (CI run #28968007617). The uncovered logic is the `State` filter routing in `Handle`:

```csharp
TransportBoxState? stateFilter = null;
bool isActiveFilter = false;

if (!string.IsNullOrWhiteSpace(request.State))
{
    if (request.State.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase))
    {
        isActiveFilter = true;             // "all boxes except Closed" — business rule, not an enum
    }
    else if (Enum.TryParse<TransportBoxState>(request.State, true, out var parsedState))
    {
        stateFilter = parsedState;
    }
}
```

This is the default view for the logistics transport-box screen. Both `stateFilter` and `isActiveFilter` are forwarded as separate parameters to `_repository.GetPagedListAsync(...)`. A silent regression here (e.g. `==` instead of `OrdinalIgnoreCase`, or the branch removed) would make the default box list appear empty or wrong with no exception thrown — hence the priority on locking this behavior down with tests.

## Functional Requirements

### FR-1: Cover the `State` filter branching in `GetTransportBoxesHandler.Handle`
Add xUnit tests (mirroring the existing style in `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/GetTransportBoxByIdHandlerTests.cs`: Moq for `ITransportBoxRepository`/`ILogger`, a real `AutoMapper` instance built from `TransportBoxMappingProfile`) exercising every branch of the filter logic, asserting on the exact arguments passed to `ITransportBoxRepository.GetPagedListAsync`.

**Acceptance criteria:**
- `State = "ACTIVE"` → repository is invoked with `state: null` and `isActiveFilter: true`.
- `State = "active"` (lowercase) → same result as above, proving `OrdinalIgnoreCase` is exercised (not `==` or `OrdinalIgnoreCase`-adjacent case-sensitive comparison).
- `State = "AcTiVe"` (mixed case) is acceptable as an additional/alternative case-insensitivity proof if preferred over the lowercase variant — at minimum one non-uppercase casing must be tested.
- `State = "Open"` (a valid `TransportBoxState` enum member) → repository is invoked with `state: TransportBoxState.Open` and `isActiveFilter: false`.
- A second valid enum value with different casing (e.g. `"closed"` or `"CLOSED"`) → repository is invoked with `state: TransportBoxState.Closed` and `isActiveFilter: false`, confirming `Enum.TryParse` is case-insensitive too.
- `State = null` → repository is invoked with `state: null` and `isActiveFilter: false`.
- `State = ""` (empty string) → repository is invoked with `state: null` and `isActiveFilter: false`.
- `State = "   "` (whitespace-only) → repository is invoked with `state: null` and `isActiveFilter: false` (exercises `IsNullOrWhiteSpace`, distinct from the empty-string case).
- `State = "NotARealState"` (unparseable, non-"ACTIVE" string) → repository is invoked with `state: null` and `isActiveFilter: false` (falls through both branches silently — current behavior, not to be "fixed" as part of this task since it's out of scope per the brief).
- Use `_repositoryMock.Verify(x => x.GetPagedListAsync(..., state, ..., isActiveFilter), Times.Once)` (or equivalent `It.Is<>` matchers) rather than only asserting on the response DTO, since `stateFilter`/`isActiveFilter` are local variables not exposed on `GetTransportBoxesResponse` — the repository call is the only observable seam.

### FR-2: Cover request pass-through and response mapping (supporting coverage, not the primary gap)
While the state-filter branch is the named gap, the handler's remaining ~80% of uncovered lines includes the pass-through of `Skip`, `Take`, `Code`, `ProductCode`, `SortBy`, `SortDescending` to the repository, and the mapping of returned items/`totalCount` into `GetTransportBoxesResponse`. At least one "happy path" test should cover this to push coverage comfortably past the 60% threshold and avoid a narrow test suite that covers only the `if/else if` but leaves other lines (mapper call, response construction) still cold.

**Acceptance criteria:**
- One test asserts that `request.Skip`, `request.Take`, `request.Code`, `request.ProductCode`, `request.SortBy`, `request.SortDescending` are forwarded verbatim to `GetPagedListAsync`.
- One test asserts that the response's `Items`, `TotalCount`, `Skip`, `Take` are populated correctly from the repository result and the mapper output (a non-empty `IList<TransportBox>` mapped to non-empty `List<TransportBoxDto>`, plus a `totalCount` distinct from the returned item count, e.g. paged scenario where `totalCount > items.Count`).

### FR-3 (conditional): Fix the state-filter bug only if discovered
The brief and coverage-gap issue explicitly scope this task to writing tests, not fixing a bug — no defect is currently confirmed in this handler. If, while writing the tests in FR-1, the actual behavior diverges from the documented/expected behavior (e.g. case-sensitivity is broken, or the "ACTIVE" branch behaves differently than described), do not silently "fix" it in this task. Instead:

**Acceptance criteria:**
- If a genuine discrepancy is found, write a test that documents the *current actual* behavior (so the suite passes and coverage improves), and flag the discrepancy explicitly in the PR description / handoff notes rather than changing production code, unless the fix is a trivial one-line change directly requested for confirmation — in which case note it in Open Questions before proceeding (see below; in practice for this file no such bug is expected since the code was read directly and matches the brief's description).

## Non-Functional Requirements

### NFR-1: Performance
Not applicable — this is a unit test addition with mocked dependencies. Tests must run in-process with no I/O and should execute in well under 100ms each.

### NFR-2: Security
Not applicable — no auth, no new data exposure, no production code paths change.

### NFR-3: Coverage target
Line coverage for `GetTransportBoxesHandler.cs` must rise from 20.0% to at least the 60% filter threshold; realistically, given the handler is ~35 lines of logic, the acceptance criteria above (all filter branches + one full happy-path pass-through/mapping test) should bring coverage to at or near 100%.

### NFR-4: Consistency with existing test conventions
New tests must live in `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/GetTransportBoxesHandlerTests.cs` (new file, following the sibling `GetTransportBoxByIdHandlerTests.cs` naming/location pattern) and use the same stack already established in that folder: xUnit (`[Fact]`/`[Theory]`), Moq (`Mock<ITransportBoxRepository>`, `Mock<ILogger<GetTransportBoxesHandler>>`), FluentAssertions for assertions, and a real `MapperConfiguration`/`TransportBoxMappingProfile`-backed `IMapper` (not a mocked mapper) so `TransportBoxDto` mapping is genuinely exercised. `[Theory]`/`[InlineData]` is preferred over repeated `[Fact]`s for the parallel state-string cases in FR-1 to keep the suite compact.

## Data Model
N/A — no schema, entity, or contract changes. Tests will construct `TransportBox` domain instances via existing public API (e.g. `new TransportBox()` + `.Open(...)`, matching the pattern in `GetTransportBoxByIdHandlerTests.cs`) or via object initializers where sufficient, and `GetTransportBoxesRequest`/`GetTransportBoxesResponse` DTOs as-is.

## API / Interface Design
N/A — no endpoint, controller, or contract changes. The only "interface" under test is the existing `IRequestHandler<GetTransportBoxesRequest, GetTransportBoxesResponse>.Handle` method and its collaboration with `ITransportBoxRepository.GetPagedListAsync`:

```csharp
Task<(IList<TransportBox> items, int totalCount)> GetPagedListAsync(
    int skip, int take, string? code = null, TransportBoxState? state = null,
    string? productCode = null, string? sortBy = null, bool sortDescending = false,
    bool isActiveFilter = false);
```

## Dependencies
- `ITransportBoxRepository` (mocked via Moq).
- `TransportBoxMappingProfile` (real AutoMapper profile, already used by sibling tests) for `TransportBox` → `TransportBoxDto` mapping.
- `TransportBoxState` enum (must contain at least two distinct, stable members — e.g. `Open`, `Closed` — to use in FR-1's enum-parsing test cases; confirm exact member names in `Anela.Heblo.Domain.Features.Logistics.Transport.TransportBoxState` before writing `[InlineData]`).
- No new NuGet packages; all test infra (xUnit, Moq, FluentAssertions) already referenced by `Anela.Heblo.Tests`.

## Out of Scope
- Fixing any bug in the state-filter logic (no bug is confirmed as of this reading of the handler; see FR-3).
- Testing `ITransportBoxRepository`'s own implementation of `GetPagedListAsync` (its filtering/query logic is a repository concern, out of this handler-level test).
- Integration/E2E tests against a real database or the `TransportBoxController` HTTP layer — this task is unit-test-only, at the handler level.
- Any other coverage-gap issues in the Logistics module or elsewhere — this spec covers only `GetTransportBoxesHandler.cs`.
- Adding a `[Theory]` explosion for every `TransportBoxState` enum value — two representative values (one non-Closed, one Closed) are sufficient per FR-1.

## Open Questions
None.

## Status: COMPLETE
