# Architecture Review: Unit tests for ProcessDailyConsumptionHandler

## Skip Design: true

Backend-only test addition. No UI, no new visual components, no changes to public contracts.

## Architectural Fit Assessment

The feature aligns with the project's testing strategy (`docs/architecture/testing-strategy.md`): xUnit + FluentAssertions + Moq, AAA structure, behavior-named tests. The handler under test is a standard MediatR handler with two injected dependencies (`IConsumptionCalculationService`, `ILogger<ProcessDailyConsumptionHandler>`) and four observable branches. The test surface is small and self-contained — exactly the unit-test sweet spot the strategy targets.

However, **the spec contains three factual errors that conflict with the actual codebase**. They must be corrected before implementation, otherwise the tests will not compile.

Main integration points:
- `Anela.Heblo.Tests` test project (`backend/test/Anela.Heblo.Tests`) — already references Moq, NSubstitute, FluentAssertions, xUnit; no new packages needed.
- Existing PackingMaterials test folder (`backend/test/Anela.Heblo.Tests/Features/PackingMaterials`) — flat layout, hand-rolled mocks for the repository/invoice-source surfaces. The handler under test does **not** touch those collaborators, so the hand-rolled mocks are not relevant here.
- `IConsumptionCalculationService` interface — sole production collaborator that needs mocking.

## Proposed Architecture

### Component Overview

```
ProcessDailyConsumptionHandlerTests  (new, xUnit test class)
    │
    ├── Mock<IConsumptionCalculationService>   (Moq)
    │     └── stubs ProcessDailyConsumptionAsync per scenario
    │
    ├── Mock<ILogger<ProcessDailyConsumptionHandler>>   (Moq)
    │     └── Verify(...) used only for the exception path (FR-4)
    │
    └── SUT: ProcessDailyConsumptionHandler
            └── invoked directly via Handle(request, CancellationToken.None)
```

No MediatR pipeline, no DI container, no HTTP. The handler is instantiated by `new` with the two mocks.

### Key Design Decisions

#### Decision 1: Mocking library — Moq, not the hand-rolled `MockLogger<T>`
**Options considered:**
- (a) Reuse the existing hand-rolled `MockLogger<T>` in the PackingMaterials test folder and add a hand-rolled `MockConsumptionCalculationService`.
- (b) Use `Mock<IConsumptionCalculationService>` and `Mock<ILogger<...>>` via Moq.
- (c) Use NSubstitute (also referenced by the project).

**Chosen approach:** (b) Moq.

**Rationale:**
- FR-4 requires verifying an error-level log entry was emitted. The current `MockLogger<T>` is a no-op (`Log` method does nothing) and cannot satisfy that assertion without modification.
- Moq is already used by the neighboring backend handler tests (e.g. `Features/Packaging/FillTrackingNumbersJobTests.cs` — same project, same style of MediatR-adjacent handler).
- The PackingMaterials folder's hand-rolled mocks (`MockPackingMaterialRepository`, `MockInvoiceConsumptionSource`) exist because those interfaces have stateful behavior worth simulating; `IConsumptionCalculationService` here only needs four scripted returns and one throw — no state machine, no benefit from a hand-rolled class.
- Mixing Moq into the PackingMaterials folder is consistent with project-wide convention. The spec's "use whatever the surrounding tests use" is ambiguous here because the surrounding tests use *both* hand-rolled mocks *and* `MockLogger<T>` — but neither pattern fits the verification need.

#### Decision 2: Test file location — flat under `Features/PackingMaterials/`, not in a nested `UseCases/ProcessDailyConsumption/` subfolder
**Options considered:**
- (a) Mirror source structure: `test/.../Features/PackingMaterials/UseCases/ProcessDailyConsumption/ProcessDailyConsumptionHandlerTests.cs` (what the spec proposes).
- (b) Match existing convention: `test/.../Features/PackingMaterials/ProcessDailyConsumptionHandlerTests.cs`.

**Chosen approach:** (b).

**Rationale:** The entire existing `Features/PackingMaterials/` test folder is flat — `GetConsumptionHistoryHandlerTests.cs`, `AllocationHandlerTests.cs`, `PackingMaterialCrudHandlerTests.cs` etc. all sit at the top level despite their sources living in per-use-case subfolders. Introducing a nested `UseCases/ProcessDailyConsumption/` directory for a single file breaks convention without benefit. Other feature folders (`Features/Packaging`, `Features/Journal`) follow the same flat pattern.

#### Decision 3: Logger double — `Mock<ILogger<T>>` with `Verify`, not a custom capturing logger
**Options considered:**
- (a) Extend the existing `MockLogger<T>` to capture log entries in a list.
- (b) Use `Mock<ILogger<ProcessDailyConsumptionHandler>>` and `Verify(x => x.Log(LogLevel.Error, ...))`.
- (c) Use `NullLogger<T>.Instance` and don't verify the log (drop the log-emission assertion from FR-4).

**Chosen approach:** (b).

**Rationale:** Moq's `ILogger` verification pattern is idiomatic and already used by `FillTrackingNumbersJobTests`. Verification reads:
```
loggerMock.Verify(
    x => x.Log(
        LogLevel.Error,
        It.IsAny<EventId>(),
        It.IsAny<It.IsAnyType>(),
        It.Is<Exception>(e => e is InvalidOperationException),
        It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
    Times.Once);
```
This keeps FR-4's "an error-level log entry is emitted" acceptance criterion testable without modifying existing infrastructure.

## Implementation Guidance

### Directory / Module Structure

Create exactly one new file:

```
backend/test/Anela.Heblo.Tests/Features/PackingMaterials/ProcessDailyConsumptionHandlerTests.cs
```

No new folders. No changes to `Anela.Heblo.Tests.csproj` (Moq, FluentAssertions, xUnit already referenced).

### Interfaces and Contracts

The SUT and its collaborators (verified against source):

```csharp
// Application/Features/PackingMaterials/UseCases/ProcessDailyConsumption/
public class ProcessDailyConsumptionRequest : IRequest<ProcessDailyConsumptionResponse>
{
    public DateOnly ProcessingDate { get; set; }    // NOTE: DateOnly, not DateTime
}

public class ProcessDailyConsumptionResponse : BaseResponse   // Success comes from BaseResponse
{
    public DateOnly ProcessedDate { get; set; }
    public int MaterialsProcessed { get; set; }
    public string Message { get; set; } = string.Empty;
}

// Application/Features/PackingMaterials/Services/IConsumptionCalculationService.cs
public interface IConsumptionCalculationService
{
    Task<ProcessDailyConsumptionResult> ProcessDailyConsumptionAsync(
        DateOnly processingDate,                            // DateOnly
        CancellationToken cancellationToken = default);
    // (HasDayAlreadyBeenProcessedAsync exists but is not exercised by the handler)
}
```

`ProcessDailyConsumptionResult` exposes `WasRun` (bool) and `MaterialsProcessed` (int) — confirm exact property names and accessibility when stubbing.

### Data Flow

```
Test                                Handler                       Mocked Service
  │                                    │                                  │
  ├─ new Mock<IConsumptionCalc...>()   │                                  │
  ├─ Setup(... )─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─▶│
  ├─ new Mock<ILogger<...>>()          │                                  │
  ├─ new ProcessDailyConsumptionHandler(svcMock.Object, logMock.Object)   │
  │                                    │                                  │
  ├─ Handle(request, CancellationToken.None) ───────────────────────────▶ │
  │                                    │ ProcessDailyConsumptionAsync ──▶ │
  │                                    │ ◀── result (WasRun, MatProc)     │
  │ ◀──── Response (Success, Message, MaterialsProcessed, ProcessedDate)  │
  │                                    │                                  │
  ├─ FluentAssertions on response      │                                  │
  └─ Moq Verify on svcMock + logMock   │                                  │
```

Four test methods, each scripts the mock differently and asserts on `response` + `Verify`:

| Test | Mock setup | Asserts on response | Mock verification |
|---|---|---|---|
| `Handle_ReturnsFailure_WhenAlreadyProcessed` | Returns `WasRun=false`, `MaterialsProcessed=42` (non-zero on purpose) | `Success=false`, `MaterialsProcessed=0`, `Message` contains "already processed" + date | svc called once with exact date & token |
| `Handle_ReturnsSuccess_WhenMaterialsUpdated` | Returns `WasRun=true`, `MaterialsProcessed=5` | `Success=true`, `MaterialsProcessed=5`, `Message` contains date + "5" + "updated" | svc called once |
| `Handle_ReturnsSuccessWithZeroCount_WhenNoInvoicesFound` | Returns `WasRun=true`, `MaterialsProcessed=0` | `Success=true`, `MaterialsProcessed=0`, `Message` contains date + "no invoices" | svc called once |
| `Handle_ReturnsGenericError_WhenServiceThrows` | Throws `InvalidOperationException("secret database connection string")` | `Success=false`, `MaterialsProcessed=0`, `Message` is the generic literal and does **not** contain "secret database connection string" | logger.Log called once with `LogLevel.Error` and the same exception instance |

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `ProcessDailyConsumptionResult` may be a constructor-only `record` with positional parameters, blocking `new ProcessDailyConsumptionResult { WasRun = true, MaterialsProcessed = 5 }`. | LOW | Inspect the type when implementing; use the appropriate constructor or `with`-syntax. The shape is internal-application, no DTO/contract concerns. |
| Message-content assertions (`Message.Should().Contain("already processed")`) couple tests to log/message wording, which may evolve. | LOW | Use `Contain` with stable substrings ("already processed", "no invoices", the date), not full-string equality. Avoid asserting on exact punctuation or casing beyond what the handler guarantees. |
| Moq's `ILogger.Verify` signature is verbose and easy to typo; a wrong `It.IsAny<It.IsAnyType>()` produces a confusing mismatch. | LOW | Extract a small local helper in the test class (`VerifyErrorLogged(loggerMock, expectedException)`) so the verbose Moq expression appears once. |
| Spec's FR-5 path (`UseCases/ProcessDailyConsumption/` subfolder) is wrong; a developer following the spec literally would create an inconsistent folder. | MEDIUM | This review's Decision 2 overrides FR-5. The amendment must be reflected in any prompt that drives implementation. |
| Spec's FR-1 acceptance criterion ("mock invoked with the request's `ProcessingDate` and the supplied `CancellationToken`") treats `ProcessingDate` as `DateTime`; the actual type is `DateOnly`. | MEDIUM | Use `DateOnly` throughout the test. See Specification Amendments. |

## Specification Amendments

The spec must be corrected on three points before implementation:

1. **Date type (FR-1 / FR-2 / FR-3 / FR-4 / "API / Interface Design"):** Every reference to `DateTime` for `ProcessingDate` and the service signature must be `DateOnly`. The actual production types are `DateOnly` (verified in `ProcessDailyConsumptionRequest.cs` and `IConsumptionCalculationService.cs`). Tests must construct `new DateOnly(2026, 1, 15)`-style values, and the mock setup signature must be `Setup(s => s.ProcessDailyConsumptionAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))`.

2. **Test file location (FR-5):** Replace `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/UseCases/ProcessDailyConsumption/ProcessDailyConsumptionHandlerTests.cs` with `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/ProcessDailyConsumptionHandlerTests.cs`. The flat layout matches every other handler test in the folder (`GetConsumptionHistoryHandlerTests.cs`, `AllocationHandlerTests.cs`, etc.).

3. **Mocking library (NFR-4 / "Dependencies"):** Resolve the "Moq or NSubstitute, whichever the surrounding tests use" ambiguity by mandating **Moq** for this file. Rationale: (a) Moq is the convention for handler tests in adjacent feature folders (`Features/Packaging/FillTrackingNumbersJobTests.cs`); (b) the existing `MockLogger<T>` in the PackingMaterials folder is a no-op and cannot satisfy FR-4's log-verification requirement; (c) Moq is already referenced in `Anela.Heblo.Tests.csproj` — no new package.

All other functional/non-functional requirements stand unchanged.

## Prerequisites

None. The test project already references Moq 4.20.72, FluentAssertions 6.12.0, xUnit 2.9.2, and `Microsoft.Extensions.Logging.Abstractions` is transitively available. No migrations, no config, no infrastructure work required. Implementation can begin immediately once the three spec amendments above are accepted.