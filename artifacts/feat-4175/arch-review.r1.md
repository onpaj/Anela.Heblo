# Architecture Review: Unit test coverage for GetGiftPackageDetailHandler exception-to-error-code mapping

## Skip Design: true

## Architectural Fit Assessment
This is a pure test-only addition to an existing, already-shipped MediatR handler in the Logistics / GiftPackageManufacture vertical slice. It introduces no new production types, no new module boundary, and no new dependency — it only exercises the existing `IRequestHandler<GetGiftPackageDetailRequest, GetGiftPackageDetailResponse>` contract through its already-injected `IGiftPackageQueryService` seam. The change fits entirely within the project's existing test conventions: `backend/test/Anela.Heblo.Tests` already has an established pattern for this exact module (`backend/test/Anela.Heblo.Tests/Application/GiftPackageManufacture/DisassembleGiftPackageHandlerTests.cs`) — mock the service dependency with Moq, assert with FluentAssertions, one `[Fact]` per exception arm plus one for the success path. No architectural decision is required beyond "follow the sibling test's shape"; there is no competing pattern to choose between.

## Proposed Architecture

### Component Overview
```
GetGiftPackageDetailHandlerTests (new, test project)
        |
        | mocks
        v
IGiftPackageQueryService (existing interface, Moq<T>)
        ^
        | injected via ctor
        |
GetGiftPackageDetailHandler (existing, unmodified)
        |
        | returns
        v
GetGiftPackageDetailResponse : BaseResponse (existing, unmodified)
```
No new components. The only new artifact is the test class itself.

### Key Design Decisions

#### Decision 1: Test file location and naming
**Options considered:**
1. `backend/test/Anela.Heblo.Tests/Application/GiftPackageManufacture/GetGiftPackageDetailHandlerTests.cs` (sibling of `DisassembleGiftPackageHandlerTests.cs`, `CreateGiftPackageManufactureHandlerTests.cs`).
2. `backend/test/Anela.Heblo.Tests/Features/Logistics/GiftPackageManufacture/...` (mirrors the production namespace path more literally, alongside `GiftPackageManufactureAtomicityIntegrationTests.cs`).

**Chosen approach:** Option 1 — `backend/test/Anela.Heblo.Tests/Application/GiftPackageManufacture/GetGiftPackageDetailHandlerTests.cs`, namespace `Anela.Heblo.Tests.Application.GiftPackageManufacture`.

**Rationale:** The existing repo already groups all handler-level unit tests for this module under `Application/GiftPackageManufacture/` (three files there today: `CreateGiftPackageManufactureHandlerTests.cs`, `DisassembleGiftPackageHandlerTests.cs`, `GiftPackageManufactureModuleTests.cs`). `Features/Logistics/GiftPackageManufacture/` is reserved for the service-level and integration-level tests (`GiftPackageManufactureAtomicityIntegrationTests.cs`) — a different test tier. Placing the new handler test with its handler-test siblings keeps the existing tier separation intact and requires no new folder.

#### Decision 2: Mocking strategy
**Options considered:**
1. `Mock<IGiftPackageQueryService>` (Moq), matching `DisassembleGiftPackageHandlerTests.cs`'s use of `Mock<IGiftPackageManufactureService>`.
2. A hand-written fake/stub implementing `IGiftPackageQueryService`.

**Chosen approach:** Option 1 — Moq.

**Rationale:** Moq is already a project dependency and the established convention for single-dependency handler tests in this exact test folder. No reason to deviate for a two-method interface with one method under test.

#### Decision 3: Exception type used for the "any other exception" arm
**Options considered:**
1. `System.Exception` directly.
2. A concrete subtype unrelated to `ArgumentException` (e.g. `InvalidOperationException`), as `DisassembleGiftPackageHandlerTests.cs` does for its analogous "unexpected failure" case.

**Chosen approach:** Option 2 — use `InvalidOperationException` (or equivalent non-`ArgumentException` type) for the generic-exception test, per the spec's FR-3.

**Rationale:** A concrete, semantically plausible exception type (the service failing for an operational reason) is more realistic than a bare `new Exception("boom")` and matches the sibling test's style, while still being caught by the handler's generic `catch (Exception)` block and unambiguously not an `ArgumentException`, keeping FR-4's "the two codes must be distinct and each test must fail on a swap" guarantee intact regardless of which non-`ArgumentException` type is chosen.

## Implementation Guidance

### Directory / Module Structure
Create exactly one new file:
- `backend/test/Anela.Heblo.Tests/Application/GiftPackageManufacture/GetGiftPackageDetailHandlerTests.cs`

No other files are created or modified. `GetGiftPackageDetailHandler.cs` and its `Request`/`Response` types are read-only inputs to this test.

### Interfaces and Contracts
No new or changed interfaces. The test consumes exactly what already exists:
- `IGiftPackageQueryService.GetGiftPackageDetailAsync(string, decimal, DateTime?, DateTime?, CancellationToken)` — mock this.
- `GetGiftPackageDetailRequest` — construct directly with `GiftPackageCode` set (and optionally `SalesCoefficient`/`FromDate`/`ToDate` if a test wants to assert they're forwarded verbatim).
- `GetGiftPackageDetailResponse` — assert on `Success`, `ErrorCode`, `GiftPackage`.
- `ErrorCodes.ValidationError` / `ErrorCodes.InternalServerError` (namespace `Anela.Heblo.Application.Shared`).
- `GiftPackageDto` (namespace `Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts`) — construct a minimal instance (e.g. `Code`, `Name` set) as the mocked success return value.

### Data Flow
Standard handler-unit-test flow, no deviation from the sibling pattern:
1. Test arranges a `Mock<IGiftPackageQueryService>` and sets up `GetGiftPackageDetailAsync` to either return a `GiftPackageDto` or throw.
2. Test constructs `new GetGiftPackageDetailHandler(mock.Object)` and calls `Handle(request, CancellationToken.None)`.
3. Test asserts on the returned `GetGiftPackageDetailResponse` and optionally verifies the mock was invoked with the expected arguments (`Times.Once`).

No database, HTTP, or DI container involvement — this is a plain constructor-injected unit test, consistent with every other file in `Application/GiftPackageManufacture/`.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| A future refactor changes the catch-block order or codes and this test doesn't catch it because both tests only assert "is an error" | Low | FR-4 requires each test to assert its own exact `ErrorCodes` value (not a generic "is failure" check); already reflected in the spec. |
| Coverage tool's 60% threshold isn't actually hit despite testing all 3 branches (e.g. property getters/setters or `using` lines counted oddly) | Low | Out of the developer's direct control beyond exercising both catch blocks and the happy path, which are the only branches in a 33-line handler; no further mitigation needed — re-check coverage output after adding tests, don't over-engineer for a coverage-tool quirk. |
| Test file name collides with a hypothetical future file | Negligible | None needed; no such file exists today (verified by directory listing). |

## Specification Amendments
None. The specification (`spec.r1.md`) as written is directly implementable; no architectural finding changes its functional or non-functional requirements.

## Prerequisites
None. The test project (`Anela.Heblo.Tests`), Moq, FluentAssertions, and xUnit are already wired up and used by the sibling test file referenced throughout this review. No migration, config, or infrastructure work precedes implementation.
