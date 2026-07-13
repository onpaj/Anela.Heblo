# Architecture Review: GetTransportBoxesHandler state-filter unit tests

## Skip Design: true

This is a backend-only test-authoring task. There is no new production code, no new endpoint, no UI/UX surface, and no schema change — confirmed by reading `GetTransportBoxesHandler.cs` directly: it is a 35-line MediatR handler with a pure branching/mapping concern. Design review is not applicable.

## Architectural Fit Assessment

The task is a straight fit with zero architectural risk: add one new xUnit test class to an existing, well-established test module. I read the target handler (`backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GetTransportBoxes/GetTransportBoxesHandler.cs`) and the sibling test (`backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/GetTransportBoxByIdHandlerTests.cs`) — the sibling establishes exactly the stack, style, and mocking approach this task should reuse:

- xUnit `[Fact]`/`[Theory]`, Moq for `ITransportBoxRepository` and `ILogger<T>`, FluentAssertions.
- A **real** `MapperConfiguration` built from `TransportBoxMappingProfile` via `NullLoggerFactory.Instance` (not a mocked `IMapper`) — the same instance construction should be copy-pasted into the new test class.
- Domain objects (`TransportBox`) are constructed through their public API (`new TransportBox()`, `.Open(...)`) rather than reflection or object initializers on private setters — confirmed this handler's tests don't need full `TransportBox` state (only pass-through/mapping matters), so simpler construction is fine here, detailed below.

This matches `docs/architecture/testing-strategy.md`: MediatR handlers are explicitly "Required" for unit testing, the stack (xUnit/Moq/FluentAssertions) matches exactly, and the guidance to test "business logic... not implementation details" supports asserting on the repository call arguments (the only observable seam, since `stateFilter`/`isActiveFilter` are handler-local variables).

No architectural amendment is needed. The existing `ITransportBoxRepository.GetPagedListAsync` signature (confirmed in `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/ITransportBoxRepository.cs`) is stable and matches what the spec describes:

```csharp
Task<(IList<TransportBox> items, int totalCount)> GetPagedListAsync(
    int skip, int take, string? code = null, TransportBoxState? state = null,
    string? productCode = null, string? sortBy = null, bool sortDescending = false,
    bool isActiveFilter = false);
```

`TransportBoxState` enum (confirmed) contains `New, Opened, InTransit, Received, InSwap, Stocked, Closed, Error, Reserve, Quarantine` — `Opened` and `Closed` (not `Open`, as the brief's shorthand suggested) are the two stable, semantically distinct members to use for the enum-parsing test cases. **This is a spec amendment** — see below.

## Proposed Architecture

### Component Overview

No new components. One new test class is added alongside its sibling in the existing test module:

```
backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/
├── GetTransportBoxByIdHandlerTests.cs   (existing, pattern source)
└── GetTransportBoxesHandlerTests.cs     (new — this task)
```

The class under test and its collaborators are unchanged:

```
GetTransportBoxesHandlerTests
        │ (Moq)              │ (Moq)                    │ (real, via TransportBoxMappingProfile)
        ▼                    ▼                           ▼
ITransportBoxRepository   ILogger<GetTransportBoxesHandler>   IMapper
        └──────────────┬──────────────────────────────────────┘
                        ▼
              GetTransportBoxesHandler.Handle(request, ct)
                        ▼
              GetTransportBoxesResponse
```

### Key Design Decisions

#### Decision 1: Assert on repository call arguments, not just the response DTO
**Options considered:**
(a) Assert only on `GetTransportBoxesResponse` fields.
(b) Use `Mock.Verify` / `It.Is<>` matchers on the `GetPagedListAsync` call to assert `state` and `isActiveFilter` directly.

**Chosen approach:** (b), as the spec requires. `stateFilter` and `isActiveFilter` are handler-local variables never exposed on the response — the mocked repository call is the only observable seam for the filter-routing logic (FR-1). The mapper/response fields (FR-2) should still be asserted separately in the happy-path test(s), since that's the only way to verify the mapping/pass-through half of the handler.

**Rationale:** Confirmed by reading the handler — `stateFilter`/`isActiveFilter` do not flow anywhere except into the repository call. Testing only the response would leave the actual filter-selection logic (the coverage gap named in the brief) unverified by anything other than incidental side effects.

#### Decision 2: Use `Opened`/`Closed` as the two enum test values, not `Open`
**Options considered:** The brief/spec draft used `TransportBoxState.Open` as an example. The actual enum (confirmed from source) has no `Open` member — it has `Opened`.

**Chosen approach:** Use `Opened` and `Closed` as the two representative enum values in `[Theory]`/`[InlineData]` cases.

**Rationale:** `Open` does not compile — `Enum.TryParse<TransportBoxState>("Open", true, out _)` would return `false`, which actually happens to still validate the "unparseable" branch by accident, not the "valid enum" branch the spec intends. Using the real member names is required for the test to do what FR-1 describes.

#### Decision 3: Construct `TransportBox` instances only where the happy-path/mapping test needs a populated domain object; use minimal/empty construction for the filter-routing tests
**Options considered:** (a) Build fully realistic `TransportBox` domain objects via `.Open(...)` for every test case, matching `GetTransportBoxByIdHandlerTests`. (b) For the pure filter-routing cases (FR-1), return an empty/trivial `IList<TransportBox>` from the mocked repository since the routing logic doesn't touch the returned items at all; reserve realistic domain construction for the FR-2 happy-path/mapping test.

**Chosen approach:** (b). FR-1 tests care only about what arguments reach `GetPagedListAsync` — the mocked return value can be `(new List<TransportBox>(), 0)` in every FR-1 case. FR-2's happy-path test is where a realistic non-empty `IList<TransportBox>` (built via `.Open(...)`, mirroring the sibling test) and a `totalCount` distinct from `items.Count` are needed to exercise the mapper and response construction.

**Rationale:** Keeps FR-1 tests focused and fast (matches NFR-1), avoids redundant domain setup that mirrors sibling test boilerplate without adding value, and keeps the two concerns (routing vs. mapping) clearly separated in the test file — one `[Theory]` for routing, one or two `[Fact]`s for pass-through/mapping.

## Implementation Guidance

### Directory / Module Structure

New file only, exact path per spec (confirmed no such file exists yet):

```
backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/GetTransportBoxesHandlerTests.cs
```

Namespace: `Anela.Heblo.Tests.Features.Logistics.Transport` (matches the sibling file). No changes to any `.csproj` — `Anela.Heblo.Tests` already references xUnit, Moq, FluentAssertions, and AutoMapper (confirmed via the sibling test compiling against these).

### Interfaces and Contracts

No new interfaces. Test against the existing:
- `GetTransportBoxesHandler.Handle(GetTransportBoxesRequest, CancellationToken)`
- `ITransportBoxRepository.GetPagedListAsync(int skip, int take, string? code, TransportBoxState? state, string? productCode, string? sortBy, bool sortDescending, bool isActiveFilter)` — mock via `Mock<ITransportBoxRepository>`, set up with `It.IsAny<...>()` for unconstrained args and `It.Is<TransportBoxState?>(...)` / exact values for the args under test, then `Verify(...)` with matching matchers.
- `TransportBoxMappingProfile` — construct real `IMapper` exactly as in the sibling file:
```csharp
var config = new MapperConfiguration(cfg =>
{
    cfg.AddProfile<TransportBoxMappingProfile>();
}, NullLoggerFactory.Instance);
var mapper = config.CreateMapper();
```

### Data Flow

For FR-1 (filter routing), each test:
1. Arrange: `_repositoryMock.Setup(x => x.GetPagedListAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<TransportBoxState?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync((new List<TransportBox>(), 0));`
2. Act: call `_handler.Handle(new GetTransportBoxesRequest { State = <case> }, CancellationToken.None)`.
3. Assert: `_repositoryMock.Verify(x => x.GetPagedListAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), <expected TransportBoxState? matcher>, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>(), <expected isActiveFilter bool>), Times.Once);`

For FR-2 (pass-through + mapping), one test verifies `Skip/Take/Code/ProductCode/SortBy/SortDescending` reach the repository call unchanged, and one test verifies a populated `(items, totalCount)` repository result flows through the real mapper into `response.Items`/`TotalCount`/`Skip`/`Take` correctly (these can be the same test or two, developer's judgment — spec allows either).

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Using nonexistent `TransportBoxState.Open` (per brief) instead of `Opened` | Low | Already caught here — use `Opened`/`Closed`, confirmed against actual enum source. |
| Loose `It.IsAny<>` on all params could mask a real regression in an unrelated param | Low | Only loosen params not under test in a given case; always pin the 1-2 params the test name claims to verify. |
| Coverage tool counts lines differently than expected, still under 60% after this suite | Low | Spec's NFR-3 already anticipates near-100% coverage from these cases (handler is ~35 lines); no further action needed unless CI proves otherwise. |

## Specification Amendments

1. **Enum member name correction**: Replace all references to `TransportBoxState.Open` in the spec/brief with `TransportBoxState.Opened` — confirmed via direct read of `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBoxState.cs`. The FR-1 acceptance criteria's `"Open"` test case must use the string `"Opened"` and assert `TransportBoxState.Opened`.
2. No other amendments. All other spec details (file path, mocking approach, mapper usage, FR/NFR breakdown) were verified against the actual codebase and are accurate as written.

## Prerequisites

N/A — no migrations, config, or infrastructure changes. The target handler, repository interface, mapping profile, and test project all already exist and compile; the new test file can be added and run immediately with `dotnet test backend/test/Anela.Heblo.Tests/ --filter GetTransportBoxesHandlerTests`.
