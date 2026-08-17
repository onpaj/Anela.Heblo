# Specification: Unit Test Coverage for `GetIssuedInvoiceDetailHandler`

## Summary
`GetIssuedInvoiceDetailHandler` is the MediatR handler backing the issued-invoice detail lookup, and its line coverage sits at 40% against a 60% threshold. This specification adds unit tests for the three untested branches (missing-ID validation, the `WithDetails` repository-method dispatch, not-found, and the outer exception handler) so every structured error shape the handler can return is pinned by an automated test. No production code changes are required — this is a test-only addition to `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceDetailHandlerTests.cs`.

## Background
The handler lives at `backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/GetIssuedInvoiceDetail/GetIssuedInvoiceDetailHandler.cs` and implements `IRequestHandler<GetIssuedInvoiceDetailRequest, GetIssuedInvoiceDetailResponse>`. It is the query handler for the issued-invoice detail endpoint: given an `InvoiceId` and a `WithDetails` flag, it fetches the invoice from `IIssuedInvoiceRepository`, maps it to `IssuedInvoiceDetailDto` via AutoMapper, and returns a `GetIssuedInvoiceDetailResponse` (which derives from `BaseResponse` — `Success`, `ErrorCode`, `Params`).

An existing test file, `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceDetailHandlerTests.cs`, already covers one branch: empty/whitespace `InvoiceId` → `ErrorCodes.ValidationError`, with a `VerifyNoOtherCalls()` assertion that the repository is never touched. That test was added when validation logic moved out of the now-deleted `IssuedInvoicesController` into this handler. The remaining three branches — repository method dispatch by `WithDetails`, not-found, and unexpected exception — are uncovered, which is what dropped the file below the 60% line-coverage gate. All four branches represent part of the handler's public error contract (`ValidationError`, `ResourceNotFound`, `Exception`) plus an internal dispatch contract (`GetByIdWithSyncHistoryAsync` vs `GetByIdAsync`) that callers implicitly rely on for correct sync-history population.

This work adds tests only, to the existing test file, following its established Moq + FluentAssertions + xUnit `Theory`/`Fact` conventions.

## Functional Requirements

### FR-1: Null/whitespace `InvoiceId` guard (already covered — no new test required, verify only)
The handler must return `Success = false`, `ErrorCode = ErrorCodes.ValidationError` when `request.InvoiceId` is `null`, empty, or whitespace, and must not call any repository method (`VerifyNoOtherCalls()` on `IIssuedInvoiceRepository`).

**Acceptance criteria:**
- Existing `Handle_EmptyOrWhitespaceInvoiceId_ReturnsValidationError` theory already asserts `""` and `"   "`. Extend it (or confirm/add a case) so `InvoiceId = null` is also exercised, since `GetIssuedInvoiceDetailRequest.InvoiceId` is a non-nullable `string` property in C# but can still be assigned `null` at the call site (e.g. from model binding) and `string.IsNullOrWhiteSpace(null)` returns `true`.
- Response has `Invoice == null`.
- `Params["ErrorMessage"]` equals `"ID faktury je povinné"` (optional but recommended assertion — matches existing test's level of detail; the existing test does not assert the message, so this is an enhancement, not a strict requirement).

### FR-2: `WithDetails` toggle dispatches to the correct repository method
When `request.WithDetails == true`, the handler must call `IIssuedInvoiceRepository.GetByIdWithSyncHistoryAsync(request.InvoiceId, cancellationToken)` and must NOT call `GetByIdAsync`. When `request.WithDetails == false`, the handler must call `GetByIdAsync(request.InvoiceId, cancellationToken)` and must NOT call `GetByIdWithSyncHistoryAsync`. In both cases, on a successful (non-null) repository result, the handler maps the result via `IMapper.Map<IssuedInvoiceDetailDto>(invoice)` and returns `Success = true` with `Invoice` populated.

**Acceptance criteria:**
- Test case A: `WithDetails = true`, repository mock set up so `GetByIdWithSyncHistoryAsync` returns a non-null `IssuedInvoice` (with `SyncHistoryCount` set, since the handler logs this field — a null-safe stub value like `0` or any int is sufficient). Mapper mock set up to return a non-null `IssuedInvoiceDetailDto`. Assert: `GetByIdWithSyncHistoryAsync` was called exactly once with the expected `InvoiceId` and cancellation token; `GetByIdAsync` was never called; response `Success == true`; response `Invoice` is the mapped DTO (reference or value equality, per mock setup).
- Test case B: `WithDetails = false`, repository mock set up so `GetByIdAsync` returns a non-null `IssuedInvoice`. Assert: `GetByIdAsync` was called exactly once with the expected `InvoiceId`; `GetByIdWithSyncHistoryAsync` was never called; response `Success == true`; response `Invoice` is the mapped DTO.
- Both cases use Moq `Verify(..., Times.Once)` / `Verify(..., Times.Never)` (or equivalent) on the repository mock, matching the strict-dispatch style already used by the existing `VerifyNoOtherCalls()` test.

### FR-3: Invoice not found
When the selected repository method (either `GetByIdAsync` or `GetByIdWithSyncHistoryAsync`, per `WithDetails`) returns `null`, the handler must return `Success = false`, `ErrorCode = ErrorCodes.ResourceNotFound`, and must not call `IMapper.Map`.

**Acceptance criteria:**
- Test case: repository mock (for either `GetByIdAsync` — the simpler/default-`WithDetails` case is sufficient, `WithDetails` doesn't need to be re-parameterized here since FR-2 already covers dispatch) returns `null` for a valid non-empty `InvoiceId`.
- Assert: response `Success == false`; `ErrorCode == ErrorCodes.ResourceNotFound`; `Invoice == null`; `Params["ErrorMessage"]` equals `"Faktura nebyla nalezena"` (recommended); `_mapperMock` was never invoked (`_mapperMock.Verify(m => m.Map<IssuedInvoiceDetailDto>(It.IsAny<object>()), Times.Never)` or `VerifyNoOtherCalls()` on the mapper mock, whichever fits the existing mock setup pattern — since `IMapper` has many extension/interface members, prefer the explicit `Map<IssuedInvoiceDetailDto>` verify to avoid brittle `VerifyNoOtherCalls` failures on unrelated `IMapper` members).

### FR-4: Unexpected exception is caught and returns a structured error
When the repository call throws any `Exception` (e.g. simulating a database failure), the handler's outer `try/catch (Exception ex)` must catch it, log via `ILogger.LogError`, and return `Success = false`, `ErrorCode = ErrorCodes.Exception`, without rethrowing.

**Acceptance criteria:**
- Test case: repository mock set up so `GetByIdAsync` (or `GetByIdWithSyncHistoryAsync`) throws (e.g. `new InvalidOperationException("simulated failure")`) when invoked with a valid `InvoiceId`.
- Assert: calling `_handler.Handle(request, CancellationToken.None)` completes normally (does not throw) — i.e. `await handler.Handle(...)` succeeds and returns a response object rather than propagating the exception.
- Response `Success == false`; `ErrorCode == ErrorCodes.Exception`; `Invoice == null`; `Params["ErrorMessage"]` equals `"Chyba při načítání detailu faktury"` (recommended).
- No assertion needed on logger calls (the existing test suite does not assert on `ILogger`; `Mock.Of<ILogger<...>>()` is a loose mock and needs no explicit setup for the exception path to work, since a loose mock's default `LogError` no-ops).

## Non-Functional Requirements

### NFR-1: Performance
Not applicable — this is a test-only change. Each new unit test must run in well under 100ms (no I/O, no real repository, no real database); the full handler test class should remain fast enough to run in every local `dotnet test` and CI pass without noticeable slowdown.

### NFR-2: Security
Not applicable — no new production code, no new data exposure. Test doubles must not embed real invoice IDs, customer data, or any data resembling production records; use clearly synthetic values (e.g. `"INV-TEST-001"`).

## Data Model
No data model changes. Relevant existing types (read-only context for test authors):

- `GetIssuedInvoiceDetailRequest` — `InvoiceId: string` (default `string.Empty`), `WithDetails: bool` (default `false`).
- `GetIssuedInvoiceDetailResponse : BaseResponse` — adds `Invoice: IssuedInvoiceDetailDto?`.
- `BaseResponse` — `Success: bool`, `ErrorCode: ErrorCodes?`, `Params: Dictionary<string,string>?`.
- `ErrorCodes` (relevant members) — `ValidationError = 1`, `ResourceNotFound = 6`, `Exception = 99`.
- `IIssuedInvoiceRepository` (extends `IRepository<IssuedInvoice, string>`) — relevant members: `GetByIdAsync(string id, CancellationToken)` (inherited from the generic repository/read-only repository interface) and `GetByIdWithSyncHistoryAsync(string id, CancellationToken)`.
- `IssuedInvoice` (domain entity) — has `SyncHistoryCount: int`, used in a log message (`invoice.SyncHistoryCount`) after a successful fetch; test doubles for the "found" cases must supply a non-null `IssuedInvoice` instance (a minimal instantiation is sufficient — check the entity's public constructor/settable properties in `backend/src/Anela.Heblo.Domain/Features/Invoices/IssuedInvoice.cs` when writing the test).
- `IssuedInvoiceDetailDto : IssuedInvoiceDto` — the AutoMapper target type; mapper mock should be set up with `_mapperMock.Setup(m => m.Map<IssuedInvoiceDetailDto>(It.IsAny<IssuedInvoice>())).Returns(new IssuedInvoiceDetailDto { ... })` or equivalent.

## API / Interface Design
No API changes. This spec targets the existing `GetIssuedInvoiceDetailHandler.Handle` method signature only:

```csharp
Task<GetIssuedInvoiceDetailResponse> Handle(GetIssuedInvoiceDetailRequest request, CancellationToken cancellationToken)
```

Test additions go into the existing test class `GetIssuedInvoiceDetailHandlerTests` in `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceDetailHandlerTests.cs`, using its existing `_repositoryMock` (`Mock<IIssuedInvoiceRepository>`), `_mapperMock` (`Mock<IMapper>`), and `_handler` fields. New test methods should be added as additional `[Fact]` / `[Theory]` methods in the same class — no new test class or file is needed given the small, cohesive scope.

Suggested new test method names (for consistency with the existing `Handle_EmptyOrWhitespaceInvoiceId_ReturnsValidationError` naming convention):
- `Handle_NullInvoiceId_ReturnsValidationError` (or fold into the existing theory as an additional `[InlineData(null)]` case, if the `Theory` parameter type allows a nullable string).
- `Handle_WithDetailsTrue_CallsGetByIdWithSyncHistoryAsync`
- `Handle_WithDetailsFalse_CallsGetByIdAsync`
- `Handle_InvoiceNotFound_ReturnsResourceNotFoundError`
- `Handle_RepositoryThrows_ReturnsExceptionError`

## Dependencies
- `Moq` — already used in the existing test file for `IIssuedInvoiceRepository` and `IMapper` mocking.
- `FluentAssertions` — already used for assertions.
- `xUnit` — already the test framework (`[Fact]`, `[Theory]`, `[InlineData]`).
- No new NuGet packages, no changes to `GetIssuedInvoiceDetailHandler.cs`, `GetIssuedInvoiceDetailRequest.cs`, or `GetIssuedInvoiceDetailResponse.cs`.
- Depends on being able to construct a minimal valid `IssuedInvoice` domain entity instance in test code; consult `backend/src/Anela.Heblo.Domain/Features/Invoices/IssuedInvoice.cs` for its constructor and required/settable members before writing the "found" test cases.

## Out of Scope
- Any change to the handler's production logic, error codes, or messages.
- Testing `IssuedInvoiceDetailDto` mapping correctness itself (AutoMapper profile tests, if any, are a separate concern) — the mapper is mocked here, not exercised for real.
- Integration/E2E tests against a real database or the HTTP endpoint that calls this handler (e.g. via `IssuedInvoicesController` or equivalent MVC controller) — this spec is unit-test-only, scoped to the handler in isolation, per the brief's suggested approach.
- Coverage of other handlers in the `Invoices` feature area (e.g. list, sync-stats) — out of scope for this coverage-gap ticket.
- Raising the file's coverage threshold or CI gate configuration — this spec only adds tests to close the existing gap under the current 60% threshold.

## Open Questions
None.

## Status: COMPLETE
