# Architecture Review: GetLeafletGenerationHandler not-found path unit test

## Skip Design: true

## Architectural Fit Assessment
This is a test-only addition to an existing, unchanged MediatR handler (`GetLeafletGenerationHandler`) in the Leaflet vertical slice. It aligns exactly with the project's established testing conventions per `docs/architecture/testing-strategy.md` ("MediatR Handlers: All business logic, validation, error scenarios" are Required unit-test coverage) and with the existing sibling tests in `backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/` (xUnit + Moq + FluentAssertions, one test class per handler, `Mock<TRepo>` field + `CreateHandler()` factory + Arrange/Act/Assert). No architectural decision is required beyond "follow the existing pattern" — there is no new component, no new interface, no data-flow change.

## Proposed Architecture

### Component Overview
```
GetLeafletGenerationHandlerTests (new)
        |
        | constructs, mocks ILeafletGenerationRepository
        v
GetLeafletGenerationHandler (existing, unchanged)
        |
        | calls GetGenerationByIdAsync(id, ct) -> null (mocked)
        v
returns GetLeafletGenerationResponse(ErrorCodes.LeafletFeedbackNotFound)
```
No new components. The test exercises the existing handler in isolation, exactly as `GetLeafletChunkDetailHandlerTests` exercises `GetLeafletChunkDetailHandler`.

### Key Design Decisions

#### Decision 1: Test file location and class shape
**Options considered:** (a) add the not-found test to a new dedicated test file mirroring the handler's name; (b) fold it into an existing, unrelated Leaflet test file.
**Chosen approach:** (a) — create `backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/GetLeafletGenerationHandlerTests.cs`, a new file, with one test class containing the single `[Fact]` for the not-found path.
**Rationale:** One test file per handler is the established 1:1 convention in this directory (8 existing sibling files each named `<Handler>Tests.cs`). No handler test file for `GetLeafletGenerationHandler` currently exists, so this is additive, not a merge into unrelated code.

#### Decision 2: Scope of the single test
**Options considered:** (a) one `[Fact]` asserting only `Success == false` and `ErrorCode == LeafletFeedbackNotFound`; (b) one `[Fact]` additionally asserting every data property is at its default, per the issue's explicit ask ("all other response properties remain at their defaults").
**Chosen approach:** (b), matching spec FR-1 exactly.
**Rationale:** The issue explicitly calls out that no test currently proves the response isn't partially populated on the not-found path — asserting only the error code would leave that specific gap open. FluentAssertions supports this cleanly via chained `.Should().Be(...)` assertions, consistent with `GetLeafletChunkDetailHandlerTests`'s multi-property assertion style in its "found" test.

## Implementation Guidance

### Directory / Module Structure
- New file only: `backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/GetLeafletGenerationHandlerTests.cs`.
- No production files touched (`GetLeafletGenerationHandler.cs`, `GetLeafletGenerationRequest.cs`, `BaseResponse.cs`, `ILeafletGenerationRepository.cs` all stay as-is).
- Namespace: `Anela.Heblo.Tests.Features.Leaflet.UseCases` (matches all sibling files in that directory).

### Interfaces and Contracts
- Mock target: `ILeafletGenerationRepository` (`Anela.Heblo.Domain.Features.Leaflet`), specifically `GetGenerationByIdAsync(Guid, CancellationToken) : Task<LeafletGeneration?>` — set up with `Mock<ILeafletGenerationRepository>().Setup(r => r.GetGenerationByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((LeafletGeneration?)null)`, mirroring `GetLeafletChunkDetailHandlerTests`'s `ReturnsAsync((LeafletChunk?)null)` cast.
- System under test: `GetLeafletGenerationHandler`, constructed via a `CreateHandler()` helper (`new GetLeafletGenerationHandler(_repoMock.Object)`), matching the sibling pattern.
- Request: `new GetLeafletGenerationRequest { Id = Guid.NewGuid() }`.
- No other production interfaces are touched — `ErrorCodes` is referenced by value only (`Anela.Heblo.Application.Shared.ErrorCodes.LeafletFeedbackNotFound`).

### Data Flow
Test → constructs mock repository returning `null` → constructs handler with mock → calls `Handle(request, CancellationToken.None)` → handler's existing not-found branch returns `new GetLeafletGenerationResponse(ErrorCodes.LeafletFeedbackNotFound)` → test asserts on the returned `GetLeafletGenerationResponse` instance. Fully synchronous-in-effect, no I/O, no async timing concerns.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Asserting exact default values (e.g. `CreatedAt == default(DateTimeOffset)`, `Id == Guid.Empty`) could be seen as testing C# language defaults rather than business behavior | Low | This is exactly what the issue asks for — proving the handler does NOT partially populate fields on the error path is the point of the gap. Acceptable and intentional per spec FR-1. |
| Namespace/using mismatches against `LeafletGeneration` domain entity (not yet inspected directly) causing a compile error | Low | Developer must resolve `using Anela.Heblo.Domain.Features.Leaflet;` (same namespace already used by `ILeafletGenerationRepository`) — no new type needs to be constructed, since the mock returns `null`, so the entity's shape doesn't matter beyond its nullable return type. |

## Specification Amendments
None. The spec (`spec.r1.md`) is implementable as written; this review confirms no architectural changes are needed.

## Prerequisites
None — no migrations, config, or infrastructure changes required. The test project (`Anela.Heblo.Tests`) already references xUnit, Moq, and FluentAssertions.
