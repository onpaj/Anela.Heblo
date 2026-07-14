# GetTransportBoxesHandler Coverage Gap — Implementation Plan

**Goal:** Add an xUnit test suite for `GetTransportBoxesHandler.Handle` that exercises every branch of the `State` filter routing logic (ACTIVE / enum-parse / fallthrough) plus request pass-through and response mapping, raising `GetTransportBoxesHandler.cs` line coverage from 20% to at or near 100%.

**Architecture:** No production code changes. One new test file, `GetTransportBoxesHandlerTests.cs`, added to the existing `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/` folder, following the exact stack and conventions of the sibling `GetTransportBoxByIdHandlerTests.cs`: xUnit `[Fact]`/`[Theory]`, Moq for `ITransportBoxRepository`/`ILogger<T>`, a real `MapperConfiguration` built from `TransportBoxMappingProfile`, and FluentAssertions for assertions. The filter-routing behavior is verified via `Mock.Verify` on the exact arguments passed to `ITransportBoxRepository.GetPagedListAsync`, since `stateFilter`/`isActiveFilter` are handler-local variables never exposed on the response DTO.

**Tech Stack:** .NET 8, xUnit 2.9.2, Moq 4.20.72, FluentAssertions 6.12.0, AutoMapper (via `TransportBoxMappingProfile`), MediatR.

**Note on spec correction:** The spec's example enum value `TransportBoxState.Open` does not exist in the codebase. The actual enum (`backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBoxState.cs`) is `New, Opened, InTransit, Received, InSwap, Stocked, Closed, Error, Reserve, Quarantine`. All test code below uses the real member names `Opened` and `Closed`.

---

### task: add-transport-boxes-handler-tests

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/GetTransportBoxesHandlerTests.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/GetTransportBoxesHandlerTests.cs` (same file — no separate test runner file)

No production files are modified. The handler under test is `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GetTransportBoxes/GetTransportBoxesHandler.cs` (read-only reference).

- [ ] **Step 1: Write the failing test file skeleton with constructor setup**

Create `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/GetTransportBoxesHandlerTests.cs` with the class shell, mirroring `GetTransportBoxByIdHandlerTests.cs`'s constructor pattern (real `MapperConfiguration` + `NullLoggerFactory.Instance`, Moq for repository and logger):

```csharp
using Anela.Heblo.Application.Features.Logistics;
using Anela.Heblo.Application.Features.Logistics.UseCases.GetTransportBoxes;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.Transport;

public class GetTransportBoxesHandlerTests
{
    private readonly Mock<ITransportBoxRepository> _repositoryMock;
    private readonly Mock<ILogger<GetTransportBoxesHandler>> _loggerMock;
    private readonly GetTransportBoxesHandler _handler;

    public GetTransportBoxesHandlerTests()
    {
        _repositoryMock = new Mock<ITransportBoxRepository>();
        _loggerMock = new Mock<ILogger<GetTransportBoxesHandler>>();

        var config = new MapperConfiguration(cfg =>
        {
            cfg.AddProfile<TransportBoxMappingProfile>();
        }, NullLoggerFactory.Instance);
        var mapper = config.CreateMapper();

        _handler = new GetTransportBoxesHandler(_loggerMock.Object, _repositoryMock.Object, mapper);
    }
}
```

This alone will not compile as a meaningful test yet (no `[Fact]`/`[Theory]` present) — that is expected; the next step adds the first real test.

- [ ] **Step 2: Add the FR-1 `[Theory]` covering all state-filter routing branches**

Add this inside the class body, directly below the constructor:

```csharp
    [Theory]
    [InlineData("ACTIVE", null, true)]
    [InlineData("active", null, true)]
    [InlineData("Opened", TransportBoxState.Opened, false)]
    [InlineData("closed", TransportBoxState.Closed, false)]
    [InlineData(null, null, false)]
    [InlineData("", null, false)]
    [InlineData("   ", null, false)]
    [InlineData("NotARealState", null, false)]
    public async Task Handle_StateFilter_RoutesExpectedArgumentsToRepository(
        string? state, TransportBoxState? expectedState, bool expectedIsActiveFilter)
    {
        // Arrange
        _repositoryMock
            .Setup(x => x.GetPagedListAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<TransportBoxState?>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync((new List<TransportBox>(), 0));

        var request = new GetTransportBoxesRequest { State = state };

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _repositoryMock.Verify(x => x.GetPagedListAsync(
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<string>(),
            expectedState,
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            expectedIsActiveFilter),
            Times.Once);
    }
```

This single `[Theory]` covers every FR-1 acceptance criterion: `"ACTIVE"` uppercase, `"active"` lowercase (proves `OrdinalIgnoreCase`, not `==`), a valid enum member (`"Opened"`), a second valid enum member with different casing (`"closed"` → `TransportBoxState.Closed`, proving `Enum.TryParse` case-insensitivity), `null`, empty string, whitespace-only, and an unparseable non-"ACTIVE" string — each asserting the exact `state`/`isActiveFilter` arguments reaching `GetPagedListAsync` via `Mock.Verify`, matching the mocking approach the arch review specified (Decision 1).

- [ ] **Step 3: Run the theory to verify it passes against current handler behavior**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetTransportBoxesHandlerTests.Handle_StateFilter_RoutesExpectedArgumentsToRepository"`

Expected: `Passed! - Failed: 0, Passed: 8, Skipped: 0` (8 `[InlineData]` cases). If any case fails, do not "fix" the handler — per spec FR-3, only a test-writing task is in scope; a genuine discrepancy would need to be flagged, not patched, unless it's the trivial spec/enum-name correction already applied above. No discrepancy is expected here since the handler was read directly and matches this behavior.

- [ ] **Step 4: Add the FR-2 pass-through test (Skip/Take/Code/ProductCode/SortBy/SortDescending forwarded verbatim)**

Add this `[Fact]` after the theory:

```csharp
    [Fact]
    public async Task Handle_ForwardsAllPassThroughParametersToRepository()
    {
        // Arrange
        _repositoryMock
            .Setup(x => x.GetPagedListAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<TransportBoxState?>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync((new List<TransportBox>(), 0));

        var request = new GetTransportBoxesRequest
        {
            Skip = 20,
            Take = 10,
            Code = "B001",
            ProductCode = "P123",
            SortBy = "Code",
            SortDescending = true
        };

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _repositoryMock.Verify(x => x.GetPagedListAsync(
            20,
            10,
            "B001",
            null,
            "P123",
            "Code",
            true,
            false),
            Times.Once);
    }
```

`State` is left unset on the request (defaults to `null`), so the expected `state` argument is `null` and `isActiveFilter` is `false` — isolating this test to only the pass-through concern, not the filter-routing concern already covered by Step 2's theory.

- [ ] **Step 5: Run the pass-through test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetTransportBoxesHandlerTests.Handle_ForwardsAllPassThroughParametersToRepository"`

Expected: `Passed! - Failed: 0, Passed: 1, Skipped: 0`

- [ ] **Step 6: Add the FR-2 mapping test (repository result → response mapping via the real mapper)**

Add this `[Fact]` after the pass-through test:

```csharp
    [Fact]
    public async Task Handle_MapsRepositoryResultIntoResponse()
    {
        // Arrange — use public API to build realistic TransportBox instances (mirrors GetTransportBoxByIdHandlerTests.cs)
        var box1 = new TransportBox();
        box1.Open("B001", DateTime.UtcNow, "user");

        var box2 = new TransportBox();
        box2.Open("B002", DateTime.UtcNow, "user");

        var items = new List<TransportBox> { box1, box2 };
        const int totalCount = 25; // distinct from items.Count (2), simulating a paged scenario

        _repositoryMock
            .Setup(x => x.GetPagedListAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<TransportBoxState?>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync((items, totalCount));

        var request = new GetTransportBoxesRequest { Skip = 10, Take = 2 };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Items.Should().HaveCount(2);
        result.Items[0].Code.Should().Be("B001");
        result.Items[1].Code.Should().Be("B002");
        result.Items[0].State.Should().Be(nameof(TransportBoxState.Opened));
        result.TotalCount.Should().Be(totalCount);
        result.Skip.Should().Be(10);
        result.Take.Should().Be(2);
    }
```

This exercises the real `TransportBoxMappingProfile` (item mapping, `State` enum-to-string conversion) and the response construction (`Items`, `TotalCount`, `Skip`, `Take`), with `totalCount` (25) deliberately distinct from `items.Count` (2) per the spec's FR-2 acceptance criteria.

- [ ] **Step 7: Run the full new test class**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetTransportBoxesHandlerTests"`

Expected: `Passed! - Failed: 0, Passed: 10, Skipped: 0` (8 theory cases + 2 facts).

- [ ] **Step 8: Run the full backend test suite to confirm no regressions**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`

Expected: all tests pass (no failures introduced elsewhere; this is a pure test-file addition with no production code touched).

- [ ] **Step 9: Build and format check**

Run: `dotnet build Anela.Heblo.sln`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` (or pre-existing warning count unchanged).

Run: `dotnet format Anela.Heblo.sln --verify-no-changes`
Expected: no formatting violations reported for the new file. If violations are reported, run `dotnet format Anela.Heblo.sln` to auto-fix, then re-run Step 7's test command to confirm the fix didn't alter behavior.

- [ ] **Step 10: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/GetTransportBoxesHandlerTests.cs
git commit -m "test: add coverage for GetTransportBoxesHandler state-filter branching"
```

---

## Self-Review

**1. Spec coverage:**
- FR-1 (state-filter branching, all listed cases including OrdinalIgnoreCase and Enum.TryParse case-insensitivity proofs) → Step 2's `[Theory]`, 8 `[InlineData]` cases, one per spec bullet.
- FR-2 (pass-through of Skip/Take/Code/ProductCode/SortBy/SortDescending; response mapping of Items/TotalCount/Skip/Take) → Step 4 (pass-through) and Step 6 (mapping), matching the spec's two separate acceptance-criteria bullets.
- FR-3 (no silent bugfix) → explicitly called out in Step 3; no discrepancy is expected or introduced, and no production file is in the Files list.
- NFR-1 (fast, mocked, no I/O) → all tests use `Mock<ITransportBoxRepository>`, no real DB/network calls.
- NFR-2 (security, N/A) → nothing added that touches auth or data exposure.
- NFR-3 (coverage ≥60%, realistically ~100%) → all branches of the `if/else if` in `Handle` are hit by Step 2's 8 cases; the mapper call and response construction lines are hit by Step 4 and Step 6. No line in the handler is left uncovered.
- NFR-4 (test conventions: file path, namespace, xUnit/Moq/FluentAssertions, real mapper, `[Theory]`/`[InlineData]` preferred over repeated `[Fact]`s) → file path and namespace match the spec exactly; stack matches the sibling file; Step 2 uses one `[Theory]` instead of 8 separate `[Fact]`s.
- Data Model / API sections (N/A, existing `TransportBox`/`GetPagedListAsync` signature) → confirmed against actual source in this session; test code uses the real 8-parameter `GetPagedListAsync` signature and the real `TransportBox.Open(boxCode, date, userName)` public API.
- Out of Scope items (no repository implementation tests, no controller/E2E tests, no bugfix, no full enum explosion) → none of these appear in the plan; only 2 enum values (`Opened`, `Closed`) are used, matching the spec's explicit cap.

No spec requirement is missing a corresponding step.

**2. Placeholder scan:** No "TBD"/"TODO"/"add appropriate handling" language appears in any step. Every code block is complete, compilable C# (verified against the actual handler signature, repository interface, DTOs, and mapping profile read directly from the worktree in this session) — no pseudocode.

**3. Type consistency:** `GetTransportBoxesRequest.State` is `string?`; `GetTransportBoxesHandler`'s constructor signature `(ILogger<GetTransportBoxesHandler>, ITransportBoxRepository, IMapper)` matches the sibling handler's constructor shape and the actual source. `ITransportBoxRepository.GetPagedListAsync`'s 8-parameter order (`skip, take, code, state, productCode, sortBy, sortDescending, isActiveFilter`) is used identically across Steps 2, 4, and 6, matching both the interface definition and the existing `ChangeTransportBoxStateHandlerTests.cs` mock-setup precedent in this same test project. `TransportBoxDto.Code`/`State`/`Items` property names match `TransportBoxDto.cs` exactly. `TransportBoxState.Opened`/`TransportBoxState.Closed` (not the spec's placeholder `Open`) are used consistently in every step that references the enum.
