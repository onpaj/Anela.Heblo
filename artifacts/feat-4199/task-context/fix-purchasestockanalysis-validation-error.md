### task: fix-purchasestockanalysis-validation-error

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/PurchaseStockAnalysisController.cs:27`
- Create: `backend/test/Anela.Heblo.Tests/Controllers/PurchaseStockAnalysisControllerTests.cs`

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Controllers/PurchaseStockAnalysisControllerTests.cs`:

```csharp
using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;
using Anela.Heblo.Application.Shared;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Controllers;

public class PurchaseStockAnalysisControllerTests
{
    private readonly Mock<IMediator> _mediatorMock = new();
    private readonly PurchaseStockAnalysisController _controller;

    public PurchaseStockAnalysisControllerTests()
    {
        _controller = new PurchaseStockAnalysisController(_mediatorMock.Object);
    }

    [Fact]
    public async Task GetStockAnalysis_InvalidModelState_ReturnsStandardizedValidationError()
    {
        // Arrange
        _controller.ModelState.AddModelError("PageSize", "The field PageSize must be between 1 and 100.");
        var request = new GetPurchaseStockAnalysisRequest { PageSize = 999 };

        // Act
        var result = await _controller.GetStockAnalysis(request, CancellationToken.None);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
        var response = Assert.IsType<GetPurchaseStockAnalysisResponse>(badRequestResult.Value);
        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.ValidationError, response.ErrorCode);

        // MediatR must never be invoked when validation fails
        _mediatorMock.Verify(
            m => m.Send(It.IsAny<GetPurchaseStockAnalysisRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetStockAnalysis_ValidModelState_DelegatesToMediator()
    {
        // Arrange
        var request = new GetPurchaseStockAnalysisRequest { PageSize = 20 };
        var expectedResponse = new GetPurchaseStockAnalysisResponse();
        _mediatorMock
            .Setup(m => m.Send(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.GetStockAnalysis(request, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(expectedResponse, okResult.Value);
    }
}
```

Check the project's existing test conventions before running: confirm `Moq` is already a referenced package in `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` (it is used by other controller tests in this project — if the reference is missing, add `<PackageReference Include="Moq" Version="..." />` matching the version already used elsewhere in the same csproj; do not introduce a new mocking library).

- [ ] **Step 2: Run the test to verify it fails**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PurchaseStockAnalysisControllerTests"
```
Expected: `GetStockAnalysis_InvalidModelState_ReturnsStandardizedValidationError` FAILS. The controller currently returns `BadRequest(ModelState)`, so `badRequestResult.Value` is a `Microsoft.AspNetCore.Mvc.ModelStateDictionary` (via ASP.NET Core's serializable wrapper), not a `GetPurchaseStockAnalysisResponse` — the `Assert.IsType<GetPurchaseStockAnalysisResponse>(...)` assertion fails.
`GetStockAnalysis_ValidModelState_DelegatesToMediator` is expected to PASS already (it exercises the untouched success path) — it is included to pin down that the fix does not disturb it.

- [ ] **Step 3: Write the minimal implementation**

In `backend/src/Anela.Heblo.API/Controllers/PurchaseStockAnalysisController.cs`, the action currently reads:

```csharp
    [HttpGet]
    public async Task<ActionResult<GetPurchaseStockAnalysisResponse>> GetStockAnalysis(
        [FromQuery] GetPurchaseStockAnalysisRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var response = await _mediator.Send(request, cancellationToken);
        return HandleResponse(response);
    }
```

Change it to:

```csharp
    [HttpGet]
    public async Task<ActionResult<GetPurchaseStockAnalysisResponse>> GetStockAnalysis(
        [FromQuery] GetPurchaseStockAnalysisRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ErrorResponseHelper.CreateValidationError<GetPurchaseStockAnalysisResponse>());
        }

        var response = await _mediator.Send(request, cancellationToken);
        return HandleResponse(response);
    }
```

Add the missing `using` for the helper's namespace at the top of the file (`PurchaseOrdersController.cs` uses the same namespace):

```csharp
using Anela.Heblo.API.Infrastructure;
```

The file's full `using` block should read:

```csharp
using Anela.Heblo.API.Infrastructure;
using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;
```

- [ ] **Step 4: Run the test to verify it passes**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PurchaseStockAnalysisControllerTests"
```
Expected: both `GetStockAnalysis_InvalidModelState_ReturnsStandardizedValidationError` and `GetStockAnalysis_ValidModelState_DelegatesToMediator` PASS.

- [ ] **Step 5: Run the full backend validation suite**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
dotnet format backend/Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```
Expected: build succeeds with no new warnings/errors, `dotnet format` makes no further changes (or only whitespace changes consistent with existing style), and the full test suite passes, including the pre-existing `GetPurchaseStockAnalysisHandlerTests.cs` and `GetPurchaseStockAnalysisHandlerDiacriticsTests.cs` (unaffected by this change, since only the controller's validation branch changed, not the handler).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/PurchaseStockAnalysisController.cs backend/test/Anela.Heblo.Tests/Controllers/PurchaseStockAnalysisControllerTests.cs
git commit -m "fix(purchase): standardize PurchaseStockAnalysisController validation error response

Replace BadRequest(ModelState) with ErrorResponseHelper.CreateValidationError<GetPurchaseStockAnalysisResponse>(), matching the pattern already used by PurchaseOrdersController, so validation failures return the project's standard BaseResponse error envelope instead of raw ModelState.

Fixes #4199"
```

## Self-Review

**1. Spec coverage:** FR-1 ("Return standardized validation error envelope") is implemented in Step 3 and verified by the test added in Step 1/4. The acceptance criteria — helper call matches `PurchaseOrdersController`'s pattern, 400 response body shape becomes `GetPurchaseStockAnalysisResponse` with `Success == false` / `ErrorCode == ValidationError`, and the success path is untouched — are each covered by one of the two test methods and Step 5's full-suite run. NFR-1/NFR-2 require no action (no performance or security surface changed) — confirmed by inspection, no task needed. "Data Model", "API / Interface Design", "Dependencies" sections of the spec describe existing state with no additional implementation required beyond Step 3. "Out of Scope" items (other controllers, `ErrorResponseHelper` itself, OpenAPI client) are correctly excluded — no task touches them.

**2. Placeholder scan:** No "TBD"/"TODO"/"handle appropriately" language. Every step shows exact, complete code and exact commands.

**3. Type consistency:** `GetPurchaseStockAnalysisResponse`, `GetPurchaseStockAnalysisRequest`, `ErrorResponseHelper.CreateValidationError<T>()`, and `ErrorCodes.ValidationError` are used consistently across the test and the implementation step, matching their real signatures as verified against `GetPurchaseStockAnalysisResponse.cs`, `GetPurchaseStockAnalysisRequest.cs`, and `ErrorResponseHelper.cs` in the current codebase.
