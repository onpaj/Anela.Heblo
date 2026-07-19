# Specification: Unit test coverage for RecalculateProductWeightHandler

## Summary
`RecalculateProductWeightHandler` currently sits at 17.1% line coverage, below the 60% CI threshold. This task closes that gap by adding unit tests that exercise the handler's three uncovered paths — single-product recalculation, full-catalog recalculation, and the exception fallback — plus the `Success` flag derivation. No production code changes; this is purely a test-coverage task.

## Background
The handler dispatches to `IProductWeightRecalculationService` based on whether `RecalculateProductWeightRequest.ProductCode` is populated, then maps the service result onto `RecalculateProductWeightResponse`. Two behaviors make the gap risky:

1. **Single-vs-all dispatch.** A regression could route a single-product request into a full catalog recalculation (expensive, slow) or vice versa. Nothing currently pins this branching.
2. **Error surface.** On exception the handler returns `Success = false`, `ErrorCount = 1`, and an `ErrorMessages` entry that the frontend renders as user-facing feedback. If that population regresses, users get a silent failure.

Additionally `Success = result.ErrorCount == 0` is computed but never asserted, so a typo flipping it would go undetected.

The codebase convention for handler unit tests (see `backend/test/Anela.Heblo.Tests/Features/Catalog/CreateManufactureDifficultyHandlerTests.cs`) is xUnit + Moq + FluentAssertions, with mocked dependencies constructed in the test class constructor. The new tests must follow this convention.

## Functional Requirements

### FR-1: Single-product recalculation path
When `request.ProductCode` is a non-empty string, the handler must call `IProductWeightRecalculationService.RecalculateProductWeight(productCode, ct)` and must NOT call `RecalculateAllProductWeights`.
**Acceptance criteria:**
- A test arranges a request with `ProductCode = "PROD001"`, a mocked service returning a successful `ProductWeightRecalculationResult`, and asserts `RecalculateProductWeight("PROD001", It.IsAny<CancellationToken>())` is invoked exactly once.
- The same test asserts `RecalculateAllProductWeights` is never invoked.
- The response mirrors the service result's `ProcessedCount`, `SuccessCount`, `ErrorCount`, and `ErrorMessages`.

### FR-2: Full-catalog recalculation path
When `request.ProductCode` is null or empty, the handler must call `RecalculateAllProductWeights(ct)` and must NOT call `RecalculateProductWeight`.
**Acceptance criteria:**
- A test with `ProductCode = null` asserts `RecalculateAllProductWeights(It.IsAny<CancellationToken>())` is invoked exactly once and `RecalculateProductWeight` is never invoked.
- A second case with `ProductCode = ""` (empty string) exercises the same branch (parameterize via `[Theory]`/`[InlineData]` with `null` and `""`).

### FR-3: Success flag derivation
The response `Success` must equal `result.ErrorCount == 0`.
**Acceptance criteria:**
- A test with a service result of `ErrorCount = 0` asserts `response.Success == true`.
- A test with a service result of `ErrorCount = 1` (with a non-empty `ErrorMessages`) asserts `response.Success == false` and that the counts/messages are passed through unchanged.

### FR-4: Exception fallback path
When the service throws, the handler must catch it and return a fallback response instead of propagating the exception.
**Acceptance criteria:**
- A test where the mocked service throws `Exception("boom")` asserts the handler does not rethrow.
- The returned response has `ProcessedCount == 0`, `SuccessCount == 0`, `ErrorCount == 1`, `Success == false`.
- `ErrorMessages` is non-empty and contains an entry that includes the original exception message (`"boom"`) prefixed with `"Internal error:"`.
- Cover the exception on both dispatch branches, or at minimum on the branch reached by an empty `ProductCode`, since the try/catch wraps both.

## Non-Functional Requirements

### NFR-1: Performance
Tests are pure unit tests against a mocked service — no I/O, no database. Each test must complete in well under a second and add negligible time to the suite.

### NFR-2: Security
No security surface. No secrets, no auth, no real service calls; the recalculation service is entirely mocked.

### NFR-3: Maintainability & conventions
- Follow the existing Catalog handler test pattern: mocks (`Mock<IProductWeightRecalculationService>`, `Mock<ILogger<RecalculateProductWeightHandler>>`) built in the constructor, system-under-test assembled once.
- Use xUnit (`[Fact]`/`[Theory]`), Moq, and FluentAssertions consistent with neighboring tests.
- Place the file at `backend/test/Anela.Heblo.Tests/Features/Catalog/RecalculateProductWeightHandlerTests.cs` in namespace `Anela.Heblo.Tests.Features.Catalog`.

### NFR-4: Coverage target
Line coverage for `RecalculateProductWeightHandler.cs` must rise above the 60% CI threshold; the added tests should cover all three branches plus the mapping, which brings coverage to effectively 100% of the handler's reachable lines.

## Data Model
No schema changes. Relevant existing types:
- `RecalculateProductWeightRequest` — `string? ProductCode`.
- `RecalculateProductWeightResponse : BaseResponse` — `ProcessedCount`, `SuccessCount`, `ErrorCount`, `List<string> ErrorMessages`, plus `Success` from `BaseResponse`.
- `ProductWeightRecalculationResult` — `ProcessedCount`, `SuccessCount`, `ErrorCount`, `ErrorMessages`, `Duration`, `StartTime`, `EndTime`.
- `IProductWeightRecalculationService` — `RecalculateAllProductWeights(ct)`, `RecalculateProductWeight(productCode, ct)`.

## API / Interface Design
No public interface changes. The unit under test is the MediatR handler `Handle(RecalculateProductWeightRequest, CancellationToken)`. Interaction with the collaborator is verified via Moq `Verify(...)` and return values via FluentAssertions on the response object.

## Dependencies
- xUnit, Moq, FluentAssertions (already referenced by `Anela.Heblo.Tests`).
- No new NuGet packages, no external services.

## Out of Scope
- Any change to `RecalculateProductWeightHandler` or other production code.
- Tests for `RecalculateProductWeightRequestValidator` (separate file, separate concern) unless trivially adjacent — not required by this gap.
- Integration/E2E tests; the recalculation service internals; the actual weight calculation logic.
- Assertions on logging calls (logger is mocked but log invocations are not part of the contract under test).

## Open Questions
None.

## Status: COMPLETE
