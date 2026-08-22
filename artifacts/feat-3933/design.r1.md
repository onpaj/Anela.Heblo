# Design: Unit Test Coverage for `GetIssuedInvoiceDetailHandler`

## Component Design
No production components change; this is a test-only addition. Test-to-subject relationship:

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

`GetIssuedInvoiceDetailHandlerTests` (`backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceDetailHandlerTests.cs`) is extended with new `[Fact]`/`[Theory]` methods, reusing its existing `_repositoryMock`, `_mapperMock`, and `_handler` fields. Each new test arranges the repository/mapper mocks for one branch (dispatch by `WithDetails`, not-found, exception), invokes `_handler.Handle(request, CancellationToken.None)`, and asserts on the returned response plus `Verify(..., Times.Once/Never)` calls on the mocks. No new test class, fixture, or helper is introduced — same conventions (Moq, FluentAssertions, xUnit) as the existing test.

## Data Schemas
No schema changes. Tests exercise the existing shapes only:
- `GetIssuedInvoiceDetailRequest` — `InvoiceId: string`, `WithDetails: bool`.
- `GetIssuedInvoiceDetailResponse : BaseResponse` — `Success`, `ErrorCode`, `Params`, `Invoice: IssuedInvoiceDetailDto?`.
- `ErrorCodes` — `ValidationError`, `ResourceNotFound`, `Exception`.
- `IIssuedInvoiceRepository.GetByIdAsync` / `.GetByIdWithSyncHistoryAsync` and `IMapper.Map<IssuedInvoiceDetailDto>` — mocked, not modified.
