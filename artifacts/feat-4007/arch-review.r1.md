# Architecture Review: Unit test coverage for UpdateTransportBoxDescriptionHandler

## Skip Design: true

## Architectural Fit Assessment
This is a pure test-addition task against an existing, unmodified MediatR handler in the Logistics vertical slice. It requires no new production code, no new interfaces, no data model or API changes, and no UI. It aligns exactly with this repo's documented backend testing standard (`docs/architecture/testing-strategy.md`): xUnit + Moq + FluentAssertions, one test class per handler, Arrange/Act/Assert, mocking `ITransportBoxRepository` and other handler dependencies. Existing sibling tests in the same directory (`backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/*HandlerTests.cs`) already establish the exact conventions to follow — `RemoveItemFromBoxHandlerTests.cs` and `GetTransportBoxByIdHandlerTests.cs` are the closest analogues (both mock `ITransportBoxRepository`, both use `Mock<ILogger<T>>`, both assert on `BaseResponse.Success`/`ErrorCode`/`Params`).

There is no existing `UpdateTransportBoxDescriptionHandlerTests.cs` file — verified via `find` across `backend/test/`. The "currently only a small portion of the success path is exercised" note in the issue likely refers to indirect coverage from another test (e.g. an integration-style test elsewhere) rather than a dedicated unit test file; the new file is additive, not a rewrite of anything that exists today.

## Proposed Architecture

### Component Overview
```
backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/
└── UpdateTransportBoxDescriptionHandlerTests.cs   (NEW)
        │
        ├── mocks: Mock<ITransportBoxRepository>, Mock<IMediator>, Mock<ILogger<UpdateTransportBoxDescriptionHandler>>
        └── exercises: UpdateTransportBoxDescriptionHandler.Handle(...)
                │
                ├── ITransportBoxRepository.GetByIdWithDetailsAsync(int)
                ├── ITransportBoxRepository.UpdateAsync(TransportBox, CancellationToken)
                ├── ITransportBoxRepository.SaveChangesAsync(CancellationToken)
                └── IMediator.Send(GetTransportBoxByIdRequest, CancellationToken)
```
No production component changes. The test file is a new leaf under the existing `Features/Logistics/Transport` test folder, mirroring the production namespace (`Anela.Heblo.Application.Features.Logistics.UseCases.UpdateTransportBoxDescription`) the same way every sibling test file already does (test namespace is the flatter `Anela.Heblo.Tests.Features.Logistics.Transport`, not a 1:1 mirror of the UseCase folder — confirmed by reading `RemoveItemFromBoxHandlerTests.cs`).

### Key Design Decisions

#### Decision 1: One test class, three-plus `[Fact]` methods, no `[Theory]`
**Options considered:** (a) three separate `[Fact]` methods for not-found / exception / happy-path, matching FR-1..FR-3 1:1; (b) a parameterized `[Theory]` collapsing the two error-branch tests into one with an `ErrorCodes` parameter.
**Chosen approach:** (a) — separate `[Fact]`s.
**Rationale:** The two error branches have different trigger conditions (null return vs. thrown exception) and different `Params` key casing (`"BoxId"` vs `"boxId"`), so a shared `[Theory]` body would need branching logic inside the test itself, which every existing sibling test file avoids. Matches repo convention exactly (`RemoveItemFromBoxHandlerTests` uses one `[Fact]` per scenario throughout).

#### Decision 2: Mock `IMediator` directly, not `ISender`
**Options considered:** MediatR 12+ exposes both `IMediator` and the narrower `ISender`; some codebases mock `ISender` since `IMediator : ISender, IPublisher`.
**Chosen approach:** Mock `IMediator` (the exact type the handler's constructor takes).
**Rationale:** The handler's constructor parameter type is `IMediator`, not `ISender` — mock the concrete dependency type, don't introduce an unrelated abstraction. `Mock<IMediator>().Setup(x => x.Send(It.IsAny<GetTransportBoxByIdRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(fakeResponse)` is standard Moq usage against `IMediator.Send<TResponse>(IRequest<TResponse>, CancellationToken)`.

#### Decision 3: Reuse a lightweight local box-builder helper, not a shared test-data builder
**Options considered:** (a) inline `new TransportBox { ... }` / minimal reflection-free construction per test; (b) reuse or extend `CreateOpenBox()`-style private static helpers as seen in `RemoveItemFromBoxHandlerTests`.
**Chosen approach:** A small private static `CreateBox(int id, string? description = null)` helper local to the new test file (not shared/extracted), setting `Id` (public settable per `Entity<T>`) and `Description` (public settable per the handler's own `box.Description = request.Description` line — no reflection needed for `Description`, unlike `State`/`Code` which are private-setter in the sibling example).
**Rationale:** `UpdateTransportBoxDescriptionHandler` never touches `State` or `Code`, so this test doesn't need the reflection tricks `RemoveItemFromBoxHandlerTests` uses for those fields — keep the helper minimal, matching only what this handler actually reads/writes. Don't extract a shared builder across files; that's a larger refactor out of scope for a coverage-gap task (see Specification Amendments).

## Implementation Guidance

### Directory / Module Structure
- New file only: `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/UpdateTransportBoxDescriptionHandlerTests.cs`
- No production files touched.
- Namespace: `Anela.Heblo.Tests.Features.Logistics.Transport` (matches every sibling file in that directory).

### Interfaces and Contracts
No new or changed interfaces. Test against the existing public contract:
- `UpdateTransportBoxDescriptionHandler(ITransportBoxRepository, IMediator, ILogger<UpdateTransportBoxDescriptionHandler>)`
- `Task<UpdateTransportBoxDescriptionResponse> Handle(UpdateTransportBoxDescriptionRequest, CancellationToken)`
- `UpdateTransportBoxDescriptionRequest { int BoxId; string? Description; }`
- `UpdateTransportBoxDescriptionResponse : BaseResponse { GetTransportBoxByIdResponse? UpdatedBox; }` — `BaseResponse` exposes `Success`, `ErrorCode`, `Params` (confirmed via sibling tests' assertion style; do not re-derive from source, just use the same assertion shapes already proven to compile in `RemoveItemFromBoxHandlerTests.cs`).

### Data Flow
1. **Not-found path:** `Handle` → `repo.GetByIdWithDetailsAsync(BoxId)` returns `null` → early return, no further repo/mediator calls. Test asserts response shape AND (via `Verify(..., Times.Never)`) that `UpdateAsync`/`SaveChangesAsync`/`mediator.Send` are never reached.
2. **Exception path:** any mocked call inside the try block (`Setup(...).ThrowsAsync(new Exception(...))` on `GetByIdWithDetailsAsync` is sufficient and simplest — it's the first call in the try block, so throwing there deterministically exercises the catch without needing to also stub the calls after it) → caught by `catch (Exception ex)` → returns `TransportBoxStateChangeError`.
3. **Happy path:** `repo.GetByIdWithDetailsAsync` returns a real `TransportBox` → `box.Description` mutated in-place → `repo.UpdateAsync(box, ct)` and `repo.SaveChangesAsync(ct)` called → `mediator.Send(GetTransportBoxByIdRequest{Id=BoxId}, ct)` called and its stubbed return value flows into `response.UpdatedBox`.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| `GetTransportBoxByIdResponse` may require non-trivial construction to stub as the mediator's return value | Low | It's a DTO-shaped response class per repo convention (DTOs are classes, not records, per `CLAUDE.md`); a parameterless `new GetTransportBoxByIdResponse()` or minimal object is sufficient since the test only needs reference-equality/identity to confirm it flows through, not to validate its internal shape (that's `GetTransportBoxByIdHandlerTests`' job, not this handler's) |
| Overlapping/duplicate test file if one already exists under a different name | Low | Confirmed via `find` that no `UpdateTransportBoxDescriptionHandlerTests.cs` (or similarly named file) exists anywhere in `backend/test/` — this is a clean new file, not a merge |
| `dotnet test` runtime regression from an unrelated flaky test in the same project | Low | Out of scope to fix; if encountered, run the new test class in isolation (`dotnet test --filter "FullyQualifiedName~UpdateTransportBoxDescriptionHandlerTests"`) to confirm this change is not the cause before investigating further |

## Specification Amendments
None required — the spec (`spec.r1.md`) is implementable as written. One minor addition for the planner: Decision 3 above (local, non-shared box-builder helper) should be treated as the concrete implementation choice for "construct a valid box" in FR-3, rather than reusing/extending helpers from `RemoveItemFromBoxHandlerTests.cs` (different file, no shared base class exists in this test project — confirmed by inspection).

## Prerequisites
None. No migrations, config, or infrastructure changes needed. The test project already references xUnit, Moq, and FluentAssertions (used throughout the directory) — no new package references required.
