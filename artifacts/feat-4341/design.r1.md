# Design: Coverage gap — FlexiBankStatementImportService failure/exception paths

## Component Design

### `FlexiBankStatementImportServiceTests` (new test class)
Location: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Bank/FlexiBankStatementImportServiceTests.cs`

Responsibility: exercise all four outcome paths of `FlexiBankStatementImportService.ImportStatementAsync` (FR-1..FR-4 in `spec.r1.md`), per the seam chosen in Decision 1 of `arch-review.r1.md`.

Structure (xUnit, constructor-based fixture setup — matching `FlexiStockTakingDomainServiceTests`/`FlexiLotsClientTests` conventions in this project):

```csharp
public class FlexiBankStatementImportServiceTests
{
    private readonly Mock<IBankAccountClient> _mockSdkClient;
    private readonly FlexiBankAccountClient _flexiBankAccountClient;
    private readonly FlexiBankStatementImportService _sut;

    public FlexiBankStatementImportServiceTests()
    {
        _mockSdkClient = new Mock<IBankAccountClient>(MockBehavior.Loose);
        _flexiBankAccountClient = new FlexiBankAccountClient(
            _mockSdkClient.Object,
            new Mock<ILogger<FlexiBankAccountClient>>().Object);
        _sut = new FlexiBankStatementImportService(
            _flexiBankAccountClient,
            new Mock<ILogger<FlexiBankStatementImportService>>().Object);
    }

    // one [Fact] per FR-1..FR-4
}
```

Only `IBankAccountClient` (the FlexiBee SDK boundary) is mocked. `FlexiBankAccountClient` and `FlexiBankStatementImportService` are both constructed as real objects — this is the "component boundary" for this change: the test's only seam is the SDK interface, not any of this repo's own classes.

### Test case boundary (four `[Fact]` methods, one per FR)

| Test method | Arrange (mock `IBankAccountClient.ImportStatement`) | Assert on `sut.ImportStatementAsync(...)` result |
|---|---|---|
| `ImportStatementAsync_WhenSdkReturnsSuccess_ReturnsSuccessResult` | Returns SDK result with `IsSuccess = true` | `IsSuccess == true`, `Value == true` |
| `ImportStatementAsync_WhenSdkReturnsFailureWithMessage_ReturnsFailureWithSameMessage` | Returns SDK result with `IsSuccess = false`, `ErrorMessage = "some FlexiBee error"` | `IsSuccess == false`, error text == `"some FlexiBee error"` |
| `ImportStatementAsync_WhenSdkReturnsFailureWithNullMessage_ReturnsUnknownImportErrorFallback` | Returns SDK result with `IsSuccess = false`, `ErrorMessage = null` | `IsSuccess == false`, error text == `"Unknown import error"` |
| `ImportStatementAsync_WhenSdkThrows_ReturnsFailureWithExceptionMessageAndDoesNotThrow` | Throws `new InvalidOperationException("boom")` | Call does not throw; `IsSuccess == false`; error text == `"Exception during import: boom"` |

No production component changes: `FlexiBankStatementImportService`, `FlexiBankAccountClient`, `IBankStatementImportService`, and `Result<bool>` all keep their current public shape.

## Data Schemas

No database schema, API contract, or event payload changes — this task adds test code only. Reference shapes the tests construct/consume (already existing, unmodified):

- `Result<bool>` (`Anela.Heblo.Domain.Shared`): outcome wrapper exposing `IsSuccess`, `Value`, and an error/message accessor (exact accessor name to be confirmed by the developer against the existing type — the type is used pervasively in this codebase, including by the SUT itself, so no ambiguity in behavior, only in naming).
- SDK result type returned by `IBankAccountClient.ImportStatement(int accountId, string aboData)` (`Rem.FlexiBeeSDK.Client.Clients.BankAccounts`): exposes `IsSuccess` and an `ErrorMessage`/`GetErrorMessage()` accessor, as already consumed by `FlexiBankAccountClient.ImportStatementAsync`. Exact constructor/property shape to be confirmed by the developer from the installed `rem.flexibeesdk.client` NuGet package (per Risk row 1 in `arch-review.r1.md`).
