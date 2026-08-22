# Architecture Review: Unit Test Coverage for GetIssuedInvoiceDetailHandler

## Skip Design: true

## Architectural Fit Assessment
This is a pure test-authoring task with zero production code impact. `GetIssuedInvoiceDetailHandler` is a standard MediatR `IRequestHandler<TRequest, TResponse>` in the vertical-slice `UseCases/GetIssuedInvoiceDetail` folder — request → validate → repository fetch → AutoMapper → `BaseResponse`-derived response, wrapped in a single outer try/catch. This shape is the dominant pattern across the codebase's handlers, and `docs/architecture/testing-strategy.md` explicitly names "MediatR handlers" as required unit-test targets under the 70% unit-test layer of the pyramid. The existing test file already exercises this handler with the correct tooling (xUnit `Theory`/`Fact`, Moq for `IIssuedInvoiceRepository`/`IMapper`, FluentAssertions, `VerifyNoOtherCalls()` for strict dispatch checks) — there is no new pattern to introduce, only gaps in an established one to fill. No new component, interface, endpoint, or module boundary is created or touched.

## Proposed Architecture

### Component Overview
No components change. Test-to-subject relationship, confirmed by reading the source:

```
GetIssuedInvoiceDetailHandlerTests (xUnit test class)
  ├─ Mock<IIssuedInvoiceRepository>  ──┐
  ├─ Mock<IMapper>                    ├─→ GetIssuedInvoiceDetailHandler.Handle(...)
  └─ Mock.Of<ILogger<...>>()         ──┘        │
                                                 ├─ validates request.InvoiceId
                                                 ├─ dispatches by request.WithDetails:
                                                 │    true  → repo.GetByIdWithSyncHistoryAsync
                                                 │    false → repo.GetByIdAsync
                                                 ├─ null result → ResourceNotFound
                                                 ├─ maps via IMapper → IssuedInvoiceDetailDto
                                                 └─ outer catch(Exception) → ErrorCodes.Exception
```

### Key Design Decisions

#### Decision 1: Extend the existing test class vs. create a new one
**Options considered:** (a) add `[Fact]`/`[Theory]` methods to the existing `GetIssuedInvoiceDetailHandlerTests` class; (b) split into a new test file per branch/concern.
**Chosen approach:** (a) — extend the existing class in place.
**Rationale:** The file is small, cohesive, and already wired with the right mocks and constructor. Splitting would add indirection for no benefit and diverge from the single-test-class-per-handler convention visible elsewhere in `backend/test/Anela.Heblo.Tests/Features/`.

#### Decision 2: `IssuedInvoice` test-double construction
**Options considered:** (a) hand-construct a minimal `IssuedInvoice` via object initializer; (b) introduce a test builder/factory.
**Chosen approach:** (a).
**Rationale:** `IssuedInvoice` (in `backend/src/Anela.Heblo.Domain/Features/Invoices/IssuedInvoice.cs`) is a plain class with a public parameterless constructor and settable properties (`Id`, `SyncHistoryCount`, etc. — `IsSynced`/`LastSyncTime`/`ErrorMessage`/`ErrorType` are private-set but not needed for these tests). A builder would be over-engineering for a single test file's needs; matches spec's suggestion of "minimal instantiation is sufficient."

#### Decision 3: Mapper verification granularity for the not-found case
**Options considered:** (a) `_mapperMock.VerifyNoOtherCalls()`; (b) targeted `_mapperMock.Verify(m => m.Map<IssuedInvoiceDetailDto>(...), Times.Never)`.
**Chosen approach:** (b), per spec FR-3.
**Rationale:** `IMapper` exposes many members (including extension-method-backed overloads); a blanket `VerifyNoOtherCalls()` risks brittle failures unrelated to the behavior under test. A targeted `Times.Never` on the exact call signature is precise and stable.

## Implementation Guidance

### Directory / Module Structure
No new files or directories. All changes go into:
`backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceDetailHandlerTests.cs`

Add five new test methods (or four plus one extended `[InlineData]` case) to the existing class, per spec FR-1..FR-4:
- Extend `Handle_EmptyOrWhitespaceInvoiceId_ReturnsValidationError` with an `[InlineData(null)]` case (the `Theory` parameter is `string`, and C# allows passing `null` to a `Theory`'s `string` parameter via `InlineData`).
- `Handle_WithDetailsTrue_CallsGetByIdWithSyncHistoryAsync`
- `Handle_WithDetailsFalse_CallsGetByIdAsync`
- `Handle_InvoiceNotFound_ReturnsResourceNotFoundError`
- `Handle_RepositoryThrows_ReturnsExceptionError`

### Interfaces and Contracts
No interface or contract changes. Tests consume the existing public surface only:
- `GetIssuedInvoiceDetailHandler.Handle(GetIssuedInvoiceDetailRequest, CancellationToken)`
- `IIssuedInvoiceRepository.GetByIdAsync(string, CancellationToken)` (inherited from `IReadOnlyRepository<TEntity, TKey>`) and `GetByIdWithSyncHistoryAsync(string, CancellationToken)`
- `IMapper.Map<IssuedInvoiceDetailDto>(object)`
- `ErrorCodes.ValidationError` / `ResourceNotFound` / `Exception`

### Data Flow
Unchanged production flow; tests drive it via mocks:
1. Arrange `Mock<IIssuedInvoiceRepository>` to return a stub `IssuedInvoice`, `null`, or throw, depending on the case; arrange `Mock<IMapper>` to return a stub `IssuedInvoiceDetailDto` where a mapped response is expected.
2. Act: `await _handler.Handle(request, CancellationToken.None)`.
3. Assert on `response.Success`, `response.ErrorCode`, `response.Invoice`, and — for dispatch tests — `Verify(..., Times.Once)` / `Verify(..., Times.Never)` on the repository mock for the two fetch methods.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| `IssuedInvoiceDetailDto` inherits from `IssuedInvoiceDto`, which may require properties the test author isn't aware of | Low | Instantiate with only the properties needed for assertions (an empty/default object initializer is sufficient since the mapper is mocked, not exercised for real); confirm by checking `IssuedInvoiceDetailDto`'s definition if the compiler complains about required members. |
| Mocking `CancellationToken.None` vs. `It.IsAny<CancellationToken>()` in `Verify` calls could make tests brittle to incidental changes | Low | Use `It.IsAny<CancellationToken>()` in `Verify` matchers (consistent with typical Moq usage) unless the spec's "expected cancellation token" wording is read strictly — since the handler forwards the token unchanged, either works; prefer `It.IsAny<CancellationToken>()` for resilience. |
| Coverage tool (filter threshold 60%) may still report the file under threshold if some line remains unreachable (e.g. dead code) | Low | Not expected given the four branches map 1:1 to the file's control flow; re-run coverage after adding tests to confirm the file clears 60%. |

## Specification Amendments
None. The spec is complete, correctly scoped, and matches the actual handler code and existing test conventions verified in this review (handler logic, `IssuedInvoice` entity shape, `IReadOnlyRepository.GetByIdAsync` signature, and testing-strategy.md's mandate to unit-test MediatR handlers all check out as described).

## Prerequisites
None. No migrations, config, or infrastructure changes needed. Implementation can start immediately by editing the existing test file. Validate with `dotnet build` and `dotnet test --filter GetIssuedInvoiceDetailHandlerTests` (or the full `Anela.Heblo.Tests` suite) before completion, per repo convention.
