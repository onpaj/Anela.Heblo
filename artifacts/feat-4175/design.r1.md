# Design: Unit test coverage for GetGiftPackageDetailHandler exception-to-error-code mapping

## Component Design

### `GetGiftPackageDetailHandlerTests` (new)
- **Location:** `backend/test/Anela.Heblo.Tests/Application/GiftPackageManufacture/GetGiftPackageDetailHandlerTests.cs`
- **Namespace:** `Anela.Heblo.Tests.Application.GiftPackageManufacture`
- **Responsibility:** Unit-test `GetGiftPackageDetailHandler.Handle` against a mocked `IGiftPackageQueryService`, covering the success path and both catch-block branches so the exception→`ErrorCodes` mapping cannot silently regress.
- **Dependencies (all existing, none new):**
  - `Mock<IGiftPackageQueryService>` (Moq) — the sole handler dependency, mocked per test.
  - `Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.GetGiftPackageDetail.{GetGiftPackageDetailHandler, GetGiftPackageDetailRequest, GetGiftPackageDetailResponse}` — system under test and its request/response types.
  - `Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts.GiftPackageDto` — constructed as the mocked service's return value for the success test.
  - `Anela.Heblo.Application.Shared.ErrorCodes` — asserted values.
  - `FluentAssertions`, `xUnit`, `Moq` — test framework/assertion/mocking libraries already referenced by the test project.

- **Test cases (one `[Fact]` each, Arrange/Act/Assert style matching `DisassembleGiftPackageHandlerTests.cs`):**

  1. `Handle_ReturnsSuccessWithGiftPackage_WhenServiceSucceeds`
     - Arrange: mock returns a `GiftPackageDto` (e.g. `Code = "SET001"`) for `GetGiftPackageDetailAsync("SET001", ..., ...)`.
     - Act: `Handle(request, CancellationToken.None)` with `request.GiftPackageCode == "SET001"`.
     - Assert: `Success == true`, `ErrorCode == null`, `GiftPackage.Code == "SET001"`; verify mock invoked once with the request's arguments.

  2. `Handle_ReturnsValidationError_WhenServiceThrowsArgumentException`
     - Arrange: mock throws `new ArgumentException("...")` (single-arg ctor, per repo convention).
     - Act: `Handle(request, CancellationToken.None)`.
     - Assert: `Success == false`, `ErrorCode == ErrorCodes.ValidationError`, `GiftPackage == null`.

  3. `Handle_ReturnsInternalServerError_WhenServiceThrowsUnexpectedException`
     - Arrange: mock throws `new InvalidOperationException("...")` (a non-`ArgumentException` type).
     - Act: `Handle(request, CancellationToken.None)`.
     - Assert: `Success == false`, `ErrorCode == ErrorCodes.InternalServerError`, `GiftPackage == null`.

  Tests 2 and 3 together satisfy FR-4 (spec): each asserts its own exact `ErrorCodes` value, and the two expected values differ, so swapping the handler's two catch-block bodies fails at least one of these tests.

- **Construction pattern:** a private `CreateSut()` helper (or direct `new GetGiftPackageDetailHandler(_serviceMock.Object)` inline, since this handler has only one constructor dependency and no `ICurrentUserService`-style second dependency like `DisassembleGiftPackageHandler` does) that wires the mock into the handler.

## Data Schemas

No new or changed schemas. Existing shapes used as test fixtures/assertions only:

```
GetGiftPackageDetailRequest
  GiftPackageCode: string
  SalesCoefficient: decimal (default 1.0m)
  FromDate: DateTime?
  ToDate: DateTime?

GetGiftPackageDetailResponse : BaseResponse
  Success: bool
  ErrorCode: ErrorCodes?
  Params: Dictionary<string, string>?
  GiftPackage: GiftPackageDto?

GiftPackageDto (minimal fields needed for the success-path fixture)
  Code: string
  Name: string
  (other fields left at default — not asserted by this feature)

ErrorCodes (subset under test)
  ValidationError = 0001
  InternalServerError = 0010
```

No API request/response contract changes; no database schema changes; no event payloads involved.
