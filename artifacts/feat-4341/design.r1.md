# Design: Coverage gap — FlexiBankStatementImportService failure/exception paths

## Component Design

### Production change: `FlexiBankAccountClient.ImportStatementAsync` becomes `virtual`
Location: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Bank/FlexiBankAccountClient.cs`

One-word, behavior-preserving change (see `arch-review.r1.md` Decision 1): change

```csharp
public async Task<Result<bool>> ImportStatementAsync(int accountId, string aboData)
```

to

```csharp
public virtual async Task<Result<bool>> ImportStatementAsync(int accountId, string aboData)
```

No other line in this file changes. This is the only production code change in this task.

### `FlexiBankStatementImportServiceTests` (new test class)
Location: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Bank/FlexiBankStatementImportServiceTests.cs`

Responsibility: exercise all four outcome paths of `FlexiBankStatementImportService.ImportStatementAsync` (FR-1..FR-4 in `spec.r1.md`), by mocking its direct collaborator `FlexiBankAccountClient` (now mockable per the change above).

Structure (xUnit, constructor-based fixture setup — matching `FlexiStockTakingDomainServiceTests`/`LedgerServiceTests` conventions in this project):

```csharp
using Anela.Heblo.Adapters.Flexi.Bank;
using Anela.Heblo.Domain.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Rem.FlexiBeeSDK.Client.Clients.BankAccounts;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Bank;

public class FlexiBankStatementImportServiceTests
{
    private readonly Mock<FlexiBankAccountClient> _mockFlexiBankAccountClient;
    private readonly FlexiBankStatementImportService _sut;

    public FlexiBankStatementImportServiceTests()
    {
        _mockFlexiBankAccountClient = new Mock<FlexiBankAccountClient>(
            Mock.Of<IBankAccountClient>(),
            Mock.Of<ILogger<FlexiBankAccountClient>>());

        _sut = new FlexiBankStatementImportService(
            _mockFlexiBankAccountClient.Object,
            Mock.Of<ILogger<FlexiBankStatementImportService>>());
    }

    // one [Fact] per FR-1..FR-4
}
```

`Mock<FlexiBankAccountClient>`'s constructor args (`Mock.Of<IBankAccountClient>()`, a logger) are never exercised — every test overrides `ImportStatementAsync` entirely via `.Setup(...)`, so the real method body (and therefore the real SDK client) never runs.

### Test case boundary (four `[Fact]` methods, one per FR)

| Test method | Arrange (`_mockFlexiBankAccountClient.Setup(x => x.ImportStatementAsync(It.IsAny<int>(), It.IsAny<string>()))`) | Assert on `await _sut.ImportStatementAsync(1, "statement-data")` |
|---|---|---|
| `ImportStatementAsync_WhenClientReturnsSuccess_ReturnsSuccessResult` | `.ReturnsAsync(Result.Success(true))` | `result.IsSuccess.Should().BeTrue()`; `result.Value.Should().BeTrue()` |
| `ImportStatementAsync_WhenClientReturnsFailureWithMessage_ReturnsSameFailureMessage` | `.ReturnsAsync(Result.Failure<bool>("some FlexiBee error"))` | `result.IsSuccess.Should().BeFalse()`; `result.ErrorMessage.Should().Be("some FlexiBee error")` |
| `ImportStatementAsync_WhenClientReturnsFailureWithNullMessage_FallsBackToUnknownImportError` | `.ReturnsAsync(Result.Failure<bool>(null!))` | `result.IsSuccess.Should().BeFalse()`; `result.ErrorMessage.Should().Be("Unknown import error")` |
| `ImportStatementAsync_WhenClientThrows_ReturnsFailureWithExceptionMessageAndDoesNotThrow` | `.ThrowsAsync(new InvalidOperationException("boom"))` | call does not throw; `result.IsSuccess.Should().BeFalse()`; `result.ErrorMessage.Should().Be("Exception during import: boom")` |

`Result.Failure<bool>(null!)` is deliberate: `Anela.Heblo.Domain.Shared.Result<T>.ErrorMessage` is `string?` and the private constructor stores whatever is passed with no runtime validation (confirmed by reading `Result.cs`); the `null!` null-forgiving operator only suppresses the harmless nullable-reference compiler warning on the `Failure(string errorMessage)` parameter, it does not change runtime behavior.

No other production component changes: `FlexiBankStatementImportService`, `IBankStatementImportService`, and `Result<bool>` all keep their current public shape unchanged.

## Data Schemas

No database schema, API contract, or event payload changes. Reference shape the tests construct/consume (already existing, unmodified):

- `Result<bool>` / `Result` (`Anela.Heblo.Domain.Shared`): `IsSuccess` (bool), `Value` (`T?`), `ErrorMessage` (`string?`); created via `Result.Success(T value)` and `Result.Failure<T>(string errorMessage)`. This is the only type the test's Arrange/Assert code needs to know about — no FlexiBee SDK type is referenced in any assertion.
