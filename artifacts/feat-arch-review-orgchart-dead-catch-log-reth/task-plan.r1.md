# Remove Redundant Try-Catch in GetOrganizationStructureHandler — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the dead `try { ... } catch { log; throw; }` wrapper from `GetOrganizationStructureHandler.Handle` so failure paths log exactly once (from the controller), and add the missing unit-test coverage that asserts the new propagation behavior.

**Architecture:** Pure refactor in a single MediatR handler — the controller already owns failure logging and the 500 mapping; the handler returns to a happy-path-only shape that matches every other handler in the codebase (e.g., `GetJournalEntriesHandler`). No interface, contract, route, or response-shape changes. Adds a new xUnit test class mirroring `src/Anela.Heblo.Application/Features/OrgChart/` under `backend/test/Anela.Heblo.Tests/Features/OrgChart/` to satisfy spec FR-4.

**Tech Stack:** .NET 8, C#, MediatR, xUnit, FluentAssertions, Moq (matches `Features/Journal/GetJournalEntryHandlerTests.cs` peer conventions). Build/format/test gates: `dotnet build`, `dotnet format`, `dotnet test`.

---

## File Structure

| Action | Path | Responsibility |
|---|---|---|
| Modify | `backend/src/Anela.Heblo.Application/Features/OrgChart/UseCases/GetOrganizationStructure/GetOrganizationStructureHandler.cs` | Remove try-catch in `Handle`; keep the entry `LogInformation` and the single `await` to `IOrgChartService`. |
| Create | `backend/test/Anela.Heblo.Tests/Features/OrgChart/GetOrganizationStructureHandlerTests.cs` | Unit tests: happy path returns service response unchanged; exception from service propagates out of `Handle` unchanged. |

No other files change. `OrgChartController.cs` stays exactly as-is (verified in arch review — the controller log line is verbatim identical to the handler log line and already attaches the exception object).

---

## Task 1: Add unit tests for the handler (RED phase)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/OrgChart/GetOrganizationStructureHandlerTests.cs`

> Why first: there are zero existing tests for `OrgChart` in `backend/test/Anela.Heblo.Tests/`. Per spec FR-4 and the arch-review amendment, we need (a) a happy-path test and (b) an exception-propagation test that would FAIL today (because the current handler catches, logs, then rethrows — which still propagates, BUT we additionally assert *no* `LogError` is emitted by the handler logger; that assertion fails today and passes after Task 2). This gives us a true RED → GREEN cycle.

- [ ] **Step 1: Create the test file with both tests**

Create `backend/test/Anela.Heblo.Tests/Features/OrgChart/GetOrganizationStructureHandlerTests.cs` with exactly this content:

```csharp
using Anela.Heblo.Application.Features.OrgChart.Contracts;
using Anela.Heblo.Application.Features.OrgChart.Services;
using Anela.Heblo.Application.Features.OrgChart.UseCases.GetOrganizationStructure;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.OrgChart;

public class GetOrganizationStructureHandlerTests
{
    private readonly Mock<IOrgChartService> _orgChartServiceMock;
    private readonly Mock<ILogger<GetOrganizationStructureHandler>> _loggerMock;
    private readonly GetOrganizationStructureHandler _handler;

    public GetOrganizationStructureHandlerTests()
    {
        _orgChartServiceMock = new Mock<IOrgChartService>();
        _loggerMock = new Mock<ILogger<GetOrganizationStructureHandler>>();
        _handler = new GetOrganizationStructureHandler(_orgChartServiceMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ReturnsServiceResponse_WhenServiceSucceeds()
    {
        // Arrange
        var request = new GetOrganizationStructureRequest();
        var expected = new OrgChartResponse
        {
            Organization = new OrganizationDto { Name = "Anela" }
        };

        _orgChartServiceMock
            .Setup(x => x.GetOrganizationStructureAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().BeSameAs(expected);
        _orgChartServiceMock.Verify(
            x => x.GetOrganizationStructureAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_PropagatesException_WhenServiceThrows()
    {
        // Arrange
        var request = new GetOrganizationStructureRequest();
        var thrown = new InvalidOperationException("boom");

        _orgChartServiceMock
            .Setup(x => x.GetOrganizationStructureAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(thrown);

        // Act
        var act = async () => await _handler.Handle(request, CancellationToken.None);

        // Assert: the exception propagates UNMODIFIED (same instance, same type, same message).
        var caught = await act.Should().ThrowAsync<InvalidOperationException>();
        caught.Which.Should().BeSameAs(thrown);

        // Assert: the handler does NOT emit its own LogError — the controller owns failure logging.
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }
}
```

Notes on the test code (do not deviate):
- `OrganizationDto` is in `Anela.Heblo.Application.Features.OrgChart.Contracts` and has a `Name` property — the only thing the happy-path test needs is *some* response object identity (`BeSameAs`), so the DTO contents are not asserted; the field is only set to make the construction explicit.
- `BeSameAs(thrown)` is critical — spec FR-3/FR-4 requires the exception instance/type be unchanged (no wrapping, no swallowing).
- The `_loggerMock.Verify(...)` block uses the canonical `ILogger.Log<TState>(LogLevel, EventId, TState, Exception?, Func<TState, Exception?, string>)` overload with `It.IsAnyType` because that is how `ILogger` extension methods (`LogError`, `LogInformation`, etc.) bottom out at the abstraction level Moq can intercept. This is the same pattern that other test classes in this repo use to assert on logger calls; do not switch to `LogError(...)` directly — Moq cannot intercept extension methods.
- `OrgChartResponse` derives from `BaseResponse`; the parameterless constructor sets `Success = true`. No need to set anything else.

- [ ] **Step 2: Run the new tests and verify the failure-propagation test FAILS**

Run from the worktree root:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetOrganizationStructureHandlerTests" \
  --nologo
```

Expected result:
- `Handle_ReturnsServiceResponse_WhenServiceSucceeds` → **PASS** (current handler returns the service response on the happy path).
- `Handle_PropagatesException_WhenServiceThrows` → **FAIL** with a Moq verification message like `Expected invocation on the mock should never have been performed, but was 1 times: x => x.Log<...>(Error, ...)`. The exception itself still propagates today (the current catch rethrows), so the `ThrowAsync` assertion will pass — but the `LogError` assertion will fail because the current handler logs at error level before rethrowing.

If both tests pass at this point, **stop** — that means the handler edit was already applied or the logger verification is malformed. Re-check the file you just wrote.

- [ ] **Step 3: Commit the RED test file**

```bash
git add backend/test/Anela.Heblo.Tests/Features/OrgChart/GetOrganizationStructureHandlerTests.cs
git commit -m "test(orgchart): add handler tests asserting exception propagation without self-log"
```

---

## Task 2: Remove the redundant try-catch in the handler (GREEN phase)

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/OrgChart/UseCases/GetOrganizationStructure/GetOrganizationStructureHandler.cs:24-38`

- [ ] **Step 1: Replace the `Handle` method body**

Open `GetOrganizationStructureHandler.cs`. Replace lines 24–38 (the entire `Handle` method) with:

```csharp
    public async Task<OrgChartResponse> Handle(GetOrganizationStructureRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Handling request to fetch organizational structure");
        return await _orgChartService.GetOrganizationStructureAsync(cancellationToken);
    }
```

The full post-edit file must read exactly:

```csharp
using MediatR;
using Microsoft.Extensions.Logging;
using Anela.Heblo.Application.Features.OrgChart.Contracts;
using Anela.Heblo.Application.Features.OrgChart.Services;

namespace Anela.Heblo.Application.Features.OrgChart.UseCases.GetOrganizationStructure;

/// <summary>
/// Handler for retrieving the complete organizational structure
/// </summary>
public class GetOrganizationStructureHandler : IRequestHandler<GetOrganizationStructureRequest, OrgChartResponse>
{
    private readonly IOrgChartService _orgChartService;
    private readonly ILogger<GetOrganizationStructureHandler> _logger;

    public GetOrganizationStructureHandler(
        IOrgChartService orgChartService,
        ILogger<GetOrganizationStructureHandler> logger)
    {
        _orgChartService = orgChartService;
        _logger = logger;
    }

    public async Task<OrgChartResponse> Handle(GetOrganizationStructureRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Handling request to fetch organizational structure");
        return await _orgChartService.GetOrganizationStructureAsync(cancellationToken);
    }
}
```

Verification checklist before moving on:
- No `try`, `catch`, or `throw` keywords appear anywhere in the file.
- The intermediate `var result = ...; return result;` is gone — single-expression return.
- `using Microsoft.Extensions.Logging;` is still present (still needed for `LogInformation`).
- The other two `using` directives (`MediatR`, `Anela.Heblo.Application.Features.OrgChart.Contracts`, `Anela.Heblo.Application.Features.OrgChart.Services`) all remain — each is still referenced.
- Class name, namespace, constructor signature, interface implementation — all unchanged.

- [ ] **Step 2: Re-run the handler tests and verify both PASS**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetOrganizationStructureHandlerTests" \
  --nologo
```

Expected: both tests PASS. `Handle_PropagatesException_WhenServiceThrows` flips from FAIL to PASS because the handler no longer calls `LogError`.

- [ ] **Step 3: Run `dotnet format` on the changed files**

```bash
dotnet format backend/Anela.Heblo.sln \
  --include backend/src/Anela.Heblo.Application/Features/OrgChart/UseCases/GetOrganizationStructure/GetOrganizationStructureHandler.cs \
            backend/test/Anela.Heblo.Tests/Features/OrgChart/GetOrganizationStructureHandlerTests.cs
```

Expected: command exits 0 with no further changes (or with whitespace-only fixups that you then re-commit). If `dotnet format` reports it cannot find the solution file, fall back to `dotnet format backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` and `dotnet format backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`.

- [ ] **Step 4: Run a full backend build**

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --nologo
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --nologo
```

Expected: both `Build succeeded.` with `0 Error(s)`. Warnings about unused `using` directives in the handler file would indicate Step 1 of this task was applied incorrectly — re-check.

- [ ] **Step 5: Run the full backend test suite to confirm no regressions**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --nologo
```

Expected: all tests pass. Spec FR-3 (client-observable behavior unchanged) is implicitly verified by the absence of failures in any controller-level or integration test that exercises the org-chart endpoint. If a previously-passing test now fails *and* its assertion references a handler-emitted `LogError` for the org-chart path, update that test to drop the assertion (it was wrong per the new design) and note the change in the commit. If a failing test references *anything else*, stop and investigate — the change may have unintended side effects.

- [ ] **Step 6: Commit the handler change**

```bash
git add backend/src/Anela.Heblo.Application/Features/OrgChart/UseCases/GetOrganizationStructure/GetOrganizationStructureHandler.cs
git commit -m "refactor(orgchart): remove redundant try-catch in handler; controller owns failure log"
```

If `dotnet format` modified the test file in step 3 (whitespace only), include those changes in this commit:

```bash
git add backend/test/Anela.Heblo.Tests/Features/OrgChart/GetOrganizationStructureHandlerTests.cs
git commit --amend --no-edit
```

(Only amend if the only delta in the test file is `dotnet format` whitespace. Otherwise — i.e., if the test logic itself changed — investigate before amending.)

---

## Self-Review

**Spec coverage check** — every requirement from `spec.r1.md` mapped to a task:

| Spec item | Where addressed |
|---|---|
| FR-1: Remove handler-level try-catch | Task 2, Step 1 |
| FR-1: Body is `LogInformation` + single `return await` | Task 2, Step 1 (exact code shown) |
| FR-1: No unused `using` directives remain | Task 2, Step 1 verification checklist + Step 4 build check |
| FR-2: Controller try-catch unchanged | No task touches `OrgChartController.cs`; verified by Task 2, Step 5 (full suite still green). |
| FR-3: Success path returns same `OrgChartResponse` / 200 | Task 1 happy-path test + Task 2, Step 5 |
| FR-3: Failure path returns same 500 shape | Controller is untouched; Task 2, Step 5 full suite |
| FR-3: No new exception types introduced or swallowed | Task 1 `Handle_PropagatesException_WhenServiceThrows` asserts `BeSameAs(thrown)` |
| FR-4: Existing handler tests updated | Vacuous — none exist; the arch-review amendment documented this. |
| FR-4: A test verifying propagation exists | Task 1, `Handle_PropagatesException_WhenServiceThrows` |
| FR-4: No test asserts on handler-level error log | Task 1 explicitly asserts `LogError` is NOT called |
| FR-4: Controller 500 tests continue to pass | Task 2, Step 5 full suite |
| NFR-1: Performance — negligible | No-op (no perf-targeted tasks needed) |
| NFR-2: Security — no new info exposed to clients | Controller body untouched; arch-review flagged `ex.Message` exposure as pre-existing out-of-scope |
| NFR-3: Maintainability — dead code removed | Task 2 |
| NFR-4: Exactly one error log per failure, content "at least as rich" | Arch-review amendment 2 verifies the controller log text/exception are verbatim identical to the removed handler log; no controller edits required. |

**Placeholder scan:** searched the plan for "TBD", "TODO", "implement later", "fill in details", "add appropriate", "similar to Task" — none present. Every code block is complete. Every command has an expected outcome.

**Type consistency check:**
- `GetOrganizationStructureHandler` — same class name in Task 1 (test) and Task 2 (edit).
- `IOrgChartService.GetOrganizationStructureAsync(CancellationToken)` — same signature referenced in both tasks; matches the actual interface.
- `GetOrganizationStructureRequest` — empty marker class with parameterless constructor in both tasks; matches the actual source.
- `OrgChartResponse` — class with `Organization` property (`OrganizationDto`) in Task 1; matches the actual source. `BaseResponse` parameterless ctor sets `Success = true` (default).
- `OrganizationDto.Name` — verified via the `OrgChart/Contracts/OrganizationDto.cs` file in the codebase.
- Logger verification uses `ILogger.Log<TState>(LogLevel, EventId, TState, Exception?, Func<TState, Exception?, string>)` — the only signature Moq can intercept, because `LogError`/`LogInformation` are extension methods.

No gaps, no inconsistencies, no placeholders.
