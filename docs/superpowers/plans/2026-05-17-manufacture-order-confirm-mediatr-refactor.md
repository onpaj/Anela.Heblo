# ManufactureOrder Confirm Endpoints — MediatR Refactor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refactor `POST /{id}/confirm-semi-product` and `POST /{id}/confirm-products` in `ManufactureOrderController` to dispatch through MediatR (matching the eight sibling endpoints), move DTO mapping into the existing AutoMapper profile, and delete the now-unused `IManufactureOrderApplicationService` — with zero observable behavior change.

**Architecture:** Convert the two existing request classes (in `Application/Features/Manufacture/Contracts/`) to `IRequest<TResponse>` markers and create matching handlers under `Application/Features/Manufacture/UseCases/<UseCase>/`. Each handler wraps the existing workflow call in a `try/catch (Exception)` (mirroring `ResolveManualActionHandler`) and returns a typed `XxxResponse(ErrorCodes.InternalServerError)` on uncaught failures — there is **no global MediatR exception pipeline** in this codebase, so the controller's current `try/catch` responsibility moves into the handler. Status codes still flow through `BaseApiController.HandleResponse` (which reads `HttpStatusCodeAttribute` off `ErrorCodes`), preserving today's 200/400/500/502 mapping exactly.

**Tech Stack:** .NET 8, MediatR, AutoMapper, xUnit + Moq + FluentAssertions, ASP.NET Core MVC controllers.

---

## File Map

**Create (4 files):**
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/ConfirmSemiProductManufacture/ConfirmSemiProductManufactureHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/ConfirmProductCompletion/ConfirmProductCompletionHandler.cs`
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/ConfirmSemiProductManufactureHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/ConfirmProductCompletionHandlerTests.cs`

**Modify (5 files):**
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Contracts/ConfirmSemiProductManufactureRequest.cs` — add `: IRequest<ConfirmSemiProductManufactureResponse>`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Contracts/ConfirmProductCompletionRequest.cs` — add `: IRequest<ConfirmProductCompletionResponse>`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureOrderMappingProfile.cs` — add two `CreateMap` calls
- `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureModule.cs` — remove `IManufactureOrderApplicationService` DI registration
- `backend/src/Anela.Heblo.API/Controllers/ManufactureOrderController.cs` — drop service dependency, replace both endpoint bodies, delete `MapResidueDistributionToDto`

**Delete (2 files):**
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/IManufactureOrderApplicationService.cs`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ManufactureOrderApplicationService.cs`

**Modify test files (2 files):**
- `backend/test/Anela.Heblo.Tests/Controllers/ManufactureOrderControllerTests.cs` — drop `_applicationServiceMock` ctor param; convert ConfirmSemiProductManufacture tests to use `_mediatorMock`; add status-code-pinning tests for ConfirmProductCompletion endpoint.
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/ManufactureOrderControllerProtocolTests.cs` — drop `serviceMock` ctor param.

**Delete test file (1 file):**
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/ManufactureOrderApplicationServiceTests.cs` (entire service is removed; its delegation tests no longer apply — the workflow behavior is still covered by `ConfirmSemiProductManufactureWorkflowTests.cs` / `ConfirmProductCompletionWorkflowTests.cs` and by the new handler tests).

---

## Task 1: Mark ConfirmSemiProductManufactureRequest as IRequest

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/Contracts/ConfirmSemiProductManufactureRequest.cs`

- [ ] **Step 1: Add MediatR `IRequest<TResponse>` marker**

Open the file. Add `using MediatR;` to the imports and change the class declaration to implement `IRequest<ConfirmSemiProductManufactureResponse>`.

Final file content:

```csharp
using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.Contracts;

public class ConfirmSemiProductManufactureRequest : IRequest<ConfirmSemiProductManufactureResponse>
{
    [Required]
    public int Id { get; set; }

    [Required]
    [Range(0.01, double.MaxValue, ErrorMessage = "ActualQuantity must be greater than 0")]
    public decimal ActualQuantity { get; set; }

    public string? ChangeReason { get; set; }
}
```

- [ ] **Step 2: Verify it compiles**

Run:
```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```
Expected: build succeeds (no test runs needed yet — controllers still call the application service).

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/Contracts/ConfirmSemiProductManufactureRequest.cs
git commit -m "refactor: mark ConfirmSemiProductManufactureRequest as IRequest<TResponse>"
```

---

## Task 2: Mark ConfirmProductCompletionRequest as IRequest

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/Contracts/ConfirmProductCompletionRequest.cs`

- [ ] **Step 1: Add MediatR `IRequest<TResponse>` marker**

Final file content:

```csharp
using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.Contracts;

public class ConfirmProductCompletionRequest : IRequest<ConfirmProductCompletionResponse>
{
    [Required]
    public int Id { get; set; }

    [Required]
    public List<ProductActualQuantityRequest> Products { get; set; } = new();

    public bool OverrideConfirmed { get; set; } = false;

    public string? ChangeReason { get; set; }
}
```

- [ ] **Step 2: Verify it compiles**

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/Contracts/ConfirmProductCompletionRequest.cs
git commit -m "refactor: mark ConfirmProductCompletionRequest as IRequest<TResponse>"
```

---

## Task 3: Add ResidueDistribution AutoMapper profile entries (TDD)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Manufacture/ManufactureOrderMappingProfileTests.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureOrderMappingProfile.cs`

Currently `ResidueDistribution` → `ResidueDistributionDto` is mapped by a private static method in `ManufactureOrderController.cs:195-222`. The shapes match exactly (7 fields on the outer DTO including a list; 8 fields per inner item) — AutoMapper convention mapping handles all of them without `.ForMember(...)`.

- [ ] **Step 1: Write the failing mapping test**

Create `backend/test/Anela.Heblo.Tests/Features/Manufacture/ManufactureOrderMappingProfileTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Manufacture;
using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Domain.Features.Manufacture;
using AutoMapper;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class ManufactureOrderMappingProfileTests
{
    private readonly IMapper _mapper;

    public ManufactureOrderMappingProfileTests()
    {
        var config = new MapperConfiguration(cfg => cfg.AddProfile<ManufactureOrderMappingProfile>());
        config.AssertConfigurationIsValid();
        _mapper = config.CreateMapper();
    }

    [Fact]
    public void Map_ResidueDistribution_To_Dto_PreservesAllFields()
    {
        // Arrange
        var source = new ResidueDistribution
        {
            ActualSemiProductQuantity = 12.5m,
            TheoreticalConsumption = 12.0m,
            Difference = 0.5m,
            DifferencePercentage = 4.17,
            IsWithinAllowedThreshold = true,
            AllowedResiduePercentage = 5.0,
            Products = new List<ProductConsumptionDistribution>
            {
                new()
                {
                    ProductCode = "PROD-A",
                    ProductName = "Product A",
                    ActualPieces = 100m,
                    TheoreticalGramsPerUnit = 0.12m,
                    TheoreticalConsumption = 12.0m,
                    AdjustedConsumption = 12.5m,
                    AdjustedGramsPerUnit = 0.125m,
                    ProportionRatio = 1.0,
                },
                new()
                {
                    ProductCode = "PROD-B",
                    ProductName = "Product B",
                    ActualPieces = 0m,
                    TheoreticalGramsPerUnit = 0m,
                    TheoreticalConsumption = 0m,
                    AdjustedConsumption = 0m,
                    AdjustedGramsPerUnit = 0m,
                    ProportionRatio = 0.0,
                },
            },
        };

        // Act
        var dto = _mapper.Map<ResidueDistributionDto>(source);

        // Assert
        dto.ActualSemiProductQuantity.Should().Be(12.5m);
        dto.TheoreticalConsumption.Should().Be(12.0m);
        dto.Difference.Should().Be(0.5m);
        dto.DifferencePercentage.Should().Be(4.17);
        dto.IsWithinAllowedThreshold.Should().BeTrue();
        dto.AllowedResiduePercentage.Should().Be(5.0);
        dto.Products.Should().HaveCount(2);

        dto.Products[0].ProductCode.Should().Be("PROD-A");
        dto.Products[0].ProductName.Should().Be("Product A");
        dto.Products[0].ActualPieces.Should().Be(100m);
        dto.Products[0].TheoreticalGramsPerUnit.Should().Be(0.12m);
        dto.Products[0].TheoreticalConsumption.Should().Be(12.0m);
        dto.Products[0].AdjustedConsumption.Should().Be(12.5m);
        dto.Products[0].AdjustedGramsPerUnit.Should().Be(0.125m);
        dto.Products[0].ProportionRatio.Should().Be(1.0);

        // Zero-quantity edge case (PROD-B) round-trips intact.
        dto.Products[1].ActualPieces.Should().Be(0m);
        dto.Products[1].AdjustedGramsPerUnit.Should().Be(0m);
    }

    [Fact]
    public void Map_ResidueDistribution_To_Dto_EmptyProductList()
    {
        // Arrange
        var source = new ResidueDistribution
        {
            ActualSemiProductQuantity = 0m,
            Products = new List<ProductConsumptionDistribution>(),
        };

        // Act
        var dto = _mapper.Map<ResidueDistributionDto>(source);

        // Assert
        dto.Products.Should().NotBeNull().And.BeEmpty();
    }
}
```

- [ ] **Step 2: Run the test and verify it fails**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ManufactureOrderMappingProfileTests"
```
Expected: FAIL — the constructor's `AssertConfigurationIsValid()` will throw because `ResidueDistribution → ResidueDistributionDto` and `ProductConsumptionDistribution → ProductConsumptionDistributionDto` are not configured.

- [ ] **Step 3: Add the two `CreateMap` calls to the profile**

Replace the body of `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureOrderMappingProfile.cs` with:

```csharp
using AutoMapper;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOrders;

namespace Anela.Heblo.Application.Features.Manufacture;

public class ManufactureOrderMappingProfile : Profile
{
    public ManufactureOrderMappingProfile()
    {
        CreateMap<ManufactureOrder, ManufactureOrderDto>();
        CreateMap<ManufactureOrderSemiProduct, ManufactureOrderSemiProductDto>();
        CreateMap<ManufactureOrderProduct, ManufactureOrderProductDto>();
        CreateMap<ManufactureOrderNote, ManufactureOrderNoteDto>();
        CreateMap<ManufactureOrderConditionsReading, ManufactureOrderConditionsReadingDto>()
            .ForMember(dest => dest.Source, opt => opt.MapFrom(src => (int)src.Source));
        CreateMap<ResidueDistribution, ResidueDistributionDto>();
        CreateMap<ProductConsumptionDistribution, ProductConsumptionDistributionDto>();
    }
}
```

- [ ] **Step 4: Run the test and verify it passes**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ManufactureOrderMappingProfileTests"
```
Expected: PASS (both tests).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureOrderMappingProfile.cs \
        backend/test/Anela.Heblo.Tests/Features/Manufacture/ManufactureOrderMappingProfileTests.cs
git commit -m "feat: add ResidueDistribution AutoMapper profile mapping"
```

---

## Task 4: Implement ConfirmSemiProductManufactureHandler (TDD)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Manufacture/ConfirmSemiProductManufactureHandlerTests.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/ConfirmSemiProductManufacture/ConfirmSemiProductManufactureHandler.cs`

The handler delegates to `IConfirmSemiProductManufactureWorkflow` and maps the workflow's `ConfirmSemiProductManufactureResult` into `ConfirmSemiProductManufactureResponse`. On any uncaught exception it logs and returns `ConfirmSemiProductManufactureResponse(ErrorCodes.InternalServerError)` with the Czech message preserved verbatim from the current controller.

- [ ] **Step 1: Write failing handler tests**

Create `backend/test/Anela.Heblo.Tests/Features/Manufacture/ConfirmSemiProductManufactureHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Application.Features.Manufacture.Services.Workflows;
using Anela.Heblo.Application.Features.Manufacture.UseCases.ConfirmSemiProductManufacture;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class ConfirmSemiProductManufactureHandlerTests
{
    private readonly Mock<IConfirmSemiProductManufactureWorkflow> _workflowMock = new();
    private readonly Mock<ILogger<ConfirmSemiProductManufactureHandler>> _loggerMock = new();
    private readonly ConfirmSemiProductManufactureHandler _handler;

    public ConfirmSemiProductManufactureHandlerTests()
    {
        _handler = new ConfirmSemiProductManufactureHandler(_workflowMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_WorkflowSuccess_ReturnsSuccessResponseWithMessage()
    {
        // Arrange
        var request = new ConfirmSemiProductManufactureRequest
        {
            Id = 1,
            ActualQuantity = 10m,
            ChangeReason = "test reason",
        };
        var workflowResult = new ConfirmSemiProductManufactureResult(
            success: true,
            message: "Polotovar byl úspěšně vyroben se skutečným množstvím 10");
        _workflowMock
            .Setup(w => w.ExecuteAsync(1, 10m, "test reason", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflowResult);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.ErrorCode.Should().BeNull();
        response.Message.Should().Be("Polotovar byl úspěšně vyroben se skutečným množstvím 10");
        _workflowMock.Verify(w => w.ExecuteAsync(1, 10m, "test reason", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WorkflowFailureWithErrorCode_ReturnsErrorResponse()
    {
        // Arrange
        var request = new ConfirmSemiProductManufactureRequest { Id = 1, ActualQuantity = 10m };
        var workflowResult = new ConfirmSemiProductManufactureResult(
            success: false,
            message: "ERP timeout",
            errorCode: ErrorCodes.ErpGatewayError);
        _workflowMock
            .Setup(w => w.ExecuteAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflowResult);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ErpGatewayError);
        response.Message.Should().Be("ERP timeout");
    }

    [Fact]
    public async Task Handle_WorkflowFailureWithoutErrorCode_DefaultsToInvalidOperation()
    {
        // Arrange
        var request = new ConfirmSemiProductManufactureRequest { Id = 1, ActualQuantity = 10m };
        var workflowResult = new ConfirmSemiProductManufactureResult(
            success: false,
            message: "Unknown failure",
            errorCode: null);
        _workflowMock
            .Setup(w => w.ExecuteAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflowResult);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.InvalidOperation);
        response.Message.Should().Be("Unknown failure");
    }

    [Fact]
    public async Task Handle_WorkflowThrowsException_ReturnsInternalServerErrorResponse()
    {
        // Arrange
        var request = new ConfirmSemiProductManufactureRequest { Id = 1, ActualQuantity = 10m };
        _workflowMock
            .Setup(w => w.ExecuteAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.InternalServerError);
        response.Message.Should().Be("Došlo k neočekávané chybě při potvrzení výroby polotovaru");
    }
}
```

- [ ] **Step 2: Run tests and verify they fail (handler doesn't exist yet)**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ConfirmSemiProductManufactureHandlerTests"
```
Expected: FAIL — compile error "type or namespace `ConfirmSemiProductManufactureHandler` could not be found".

- [ ] **Step 3: Create the handler**

First create the directory:
```bash
mkdir -p backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/ConfirmSemiProductManufacture
```

Create `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/ConfirmSemiProductManufacture/ConfirmSemiProductManufactureHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Application.Features.Manufacture.Services.Workflows;
using Anela.Heblo.Application.Shared;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.ConfirmSemiProductManufacture;

public class ConfirmSemiProductManufactureHandler
    : IRequestHandler<ConfirmSemiProductManufactureRequest, ConfirmSemiProductManufactureResponse>
{
    private const string UnexpectedErrorMessage = "Došlo k neočekávané chybě při potvrzení výroby polotovaru";

    private readonly IConfirmSemiProductManufactureWorkflow _workflow;
    private readonly ILogger<ConfirmSemiProductManufactureHandler> _logger;

    public ConfirmSemiProductManufactureHandler(
        IConfirmSemiProductManufactureWorkflow workflow,
        ILogger<ConfirmSemiProductManufactureHandler> logger)
    {
        _workflow = workflow;
        _logger = logger;
    }

    public async Task<ConfirmSemiProductManufactureResponse> Handle(
        ConfirmSemiProductManufactureRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _workflow.ExecuteAsync(
                request.Id,
                request.ActualQuantity,
                request.ChangeReason,
                cancellationToken);

            if (result.Success)
            {
                return new ConfirmSemiProductManufactureResponse
                {
                    Message = result.Message,
                };
            }

            var errorCode = result.ErrorCode ?? ErrorCodes.InvalidOperation;
            _logger.LogWarning(
                "ConfirmSemiProductManufacture failed for order {OrderId}: {ErrorCode} — {Message}",
                request.Id, errorCode, result.Message);

            return new ConfirmSemiProductManufactureResponse(errorCode)
            {
                Message = result.Message,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error confirming semi-product manufacture for order {OrderId}", request.Id);
            return new ConfirmSemiProductManufactureResponse(ErrorCodes.InternalServerError)
            {
                Message = UnexpectedErrorMessage,
            };
        }
    }
}
```

- [ ] **Step 4: Run tests and verify they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ConfirmSemiProductManufactureHandlerTests"
```
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/ConfirmSemiProductManufacture/ConfirmSemiProductManufactureHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Manufacture/ConfirmSemiProductManufactureHandlerTests.cs
git commit -m "feat: add ConfirmSemiProductManufactureHandler"
```

---

## Task 5: Implement ConfirmProductCompletionHandler (TDD)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Manufacture/ConfirmProductCompletionHandlerTests.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/ConfirmProductCompletion/ConfirmProductCompletionHandler.cs`

The handler translates `request.Products` (a `List<ProductActualQuantityRequest>`) into `Dictionary<int, decimal>` for the workflow, then maps the workflow's `ConfirmProductCompletionResult` into `ConfirmProductCompletionResponse`. Three workflow result shapes must be handled:

1. **`Success = true`, `RequiresConfirmation = false`, `Distribution = null`** → return successful empty response.
2. **`Success = false`, `RequiresConfirmation = true`, `Distribution != null`** → return successful response with `RequiresConfirmation = true` and mapped `Distribution`. Throw `InvalidOperationException` if `Distribution` is null (preserves controller invariant at `ManufactureOrderController.cs:197-200`).
3. **`Success = false`, `RequiresConfirmation = false`** → return error response with `ErrorCodes.InvalidOperation` and `Message = result.ErrorMessage`.

On uncaught exceptions, log and return `ConfirmProductCompletionResponse(ErrorCodes.InternalServerError)` with the Czech message verbatim from the current controller.

- [ ] **Step 1: Write failing handler tests**

Create `backend/test/Anela.Heblo.Tests/Features/Manufacture/ConfirmProductCompletionHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Manufacture;
using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Application.Features.Manufacture.Services.Workflows;
using Anela.Heblo.Application.Features.Manufacture.UseCases.ConfirmProductCompletion;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class ConfirmProductCompletionHandlerTests
{
    private readonly Mock<IConfirmProductCompletionWorkflow> _workflowMock = new();
    private readonly Mock<ILogger<ConfirmProductCompletionHandler>> _loggerMock = new();
    private readonly IMapper _mapper;
    private readonly ConfirmProductCompletionHandler _handler;

    public ConfirmProductCompletionHandlerTests()
    {
        var config = new MapperConfiguration(cfg => cfg.AddProfile<ManufactureOrderMappingProfile>());
        _mapper = config.CreateMapper();
        _handler = new ConfirmProductCompletionHandler(_workflowMock.Object, _mapper, _loggerMock.Object);
    }

    private static ConfirmProductCompletionRequest BuildRequest() => new()
    {
        Id = 1,
        Products = new List<ProductActualQuantityRequest>
        {
            new() { Id = 10, ActualQuantity = 5m },
            new() { Id = 20, ActualQuantity = 7m },
        },
        OverrideConfirmed = false,
        ChangeReason = "test reason",
    };

    [Fact]
    public async Task Handle_WorkflowSuccess_ReturnsSuccessfulEmptyResponse()
    {
        // Arrange
        var request = BuildRequest();
        _workflowMock
            .Setup(w => w.ExecuteAsync(
                1,
                It.Is<Dictionary<int, decimal>>(d => d.Count == 2 && d[10] == 5m && d[20] == 7m),
                false,
                "test reason",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConfirmProductCompletionResult());

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.ErrorCode.Should().BeNull();
        response.RequiresConfirmation.Should().BeFalse();
        response.Distribution.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WorkflowRequiresConfirmation_ReturnsSuccessfulResponseWithMappedDistribution()
    {
        // Arrange
        var request = BuildRequest();
        var distribution = new ResidueDistribution
        {
            ActualSemiProductQuantity = 15m,
            TheoreticalConsumption = 12m,
            Difference = 3m,
            DifferencePercentage = 25.0,
            IsWithinAllowedThreshold = false,
            AllowedResiduePercentage = 5.0,
            Products = new List<ProductConsumptionDistribution>
            {
                new()
                {
                    ProductCode = "PROD-A",
                    ProductName = "Product A",
                    ActualPieces = 100m,
                    TheoreticalGramsPerUnit = 0.12m,
                    TheoreticalConsumption = 12m,
                    AdjustedConsumption = 15m,
                    AdjustedGramsPerUnit = 0.15m,
                    ProportionRatio = 1.0,
                },
            },
        };
        _workflowMock
            .Setup(w => w.ExecuteAsync(
                It.IsAny<int>(),
                It.IsAny<Dictionary<int, decimal>>(),
                It.IsAny<bool>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConfirmProductCompletionResult.NeedsConfirmation(distribution));

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.RequiresConfirmation.Should().BeTrue();
        response.Distribution.Should().NotBeNull();
        response.Distribution!.ActualSemiProductQuantity.Should().Be(15m);
        response.Distribution.IsWithinAllowedThreshold.Should().BeFalse();
        response.Distribution.Products.Should().HaveCount(1);
        response.Distribution.Products[0].ProductCode.Should().Be("PROD-A");
        response.Distribution.Products[0].AdjustedGramsPerUnit.Should().Be(0.15m);
    }

    [Fact]
    public async Task Handle_WorkflowFailure_ReturnsBadRequestResponseWithErrorMessage()
    {
        // Arrange
        var request = BuildRequest();
        _workflowMock
            .Setup(w => w.ExecuteAsync(
                It.IsAny<int>(),
                It.IsAny<Dictionary<int, decimal>>(),
                It.IsAny<bool>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConfirmProductCompletionResult("Chyba při aktualizaci množství produktů: InternalServerError"));

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.InvalidOperation);
        response.Message.Should().Be("Chyba při aktualizaci množství produktů: InternalServerError");
        response.RequiresConfirmation.Should().BeFalse();
        response.Distribution.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WorkflowThrowsException_ReturnsInternalServerErrorResponse()
    {
        // Arrange
        var request = BuildRequest();
        _workflowMock
            .Setup(w => w.ExecuteAsync(
                It.IsAny<int>(),
                It.IsAny<Dictionary<int, decimal>>(),
                It.IsAny<bool>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.InternalServerError);
        response.Message.Should().Be("Došlo k neočekávané chybě při dokončení výroby produktů");
    }

    [Fact]
    public async Task Handle_PassesOverrideConfirmedAndChangeReasonToWorkflow()
    {
        // Arrange
        var request = BuildRequest();
        request.OverrideConfirmed = true;
        request.ChangeReason = "override";
        _workflowMock
            .Setup(w => w.ExecuteAsync(1, It.IsAny<Dictionary<int, decimal>>(), true, "override", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConfirmProductCompletionResult());

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _workflowMock.Verify(
            w => w.ExecuteAsync(1, It.IsAny<Dictionary<int, decimal>>(), true, "override", It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
```

- [ ] **Step 2: Run tests and verify they fail (handler doesn't exist yet)**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ConfirmProductCompletionHandlerTests"
```
Expected: FAIL — compile error.

- [ ] **Step 3: Create the handler**

First create the directory:
```bash
mkdir -p backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/ConfirmProductCompletion
```

Create `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/ConfirmProductCompletion/ConfirmProductCompletionHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Application.Features.Manufacture.Services.Workflows;
using Anela.Heblo.Application.Shared;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.ConfirmProductCompletion;

public class ConfirmProductCompletionHandler
    : IRequestHandler<ConfirmProductCompletionRequest, ConfirmProductCompletionResponse>
{
    private const string UnexpectedErrorMessage = "Došlo k neočekávané chybě při dokončení výroby produktů";

    private readonly IConfirmProductCompletionWorkflow _workflow;
    private readonly IMapper _mapper;
    private readonly ILogger<ConfirmProductCompletionHandler> _logger;

    public ConfirmProductCompletionHandler(
        IConfirmProductCompletionWorkflow workflow,
        IMapper mapper,
        ILogger<ConfirmProductCompletionHandler> logger)
    {
        _workflow = workflow;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<ConfirmProductCompletionResponse> Handle(
        ConfirmProductCompletionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var productActualQuantities = request.Products.ToDictionary(p => p.Id, p => p.ActualQuantity);

            var result = await _workflow.ExecuteAsync(
                request.Id,
                productActualQuantities,
                request.OverrideConfirmed,
                request.ChangeReason,
                cancellationToken);

            if (result.RequiresConfirmation)
            {
                if (result.Distribution is null)
                {
                    throw new InvalidOperationException("Distribution cannot be null when mapping to DTO");
                }

                return new ConfirmProductCompletionResponse
                {
                    RequiresConfirmation = true,
                    Distribution = _mapper.Map<ResidueDistributionDto>(result.Distribution),
                };
            }

            if (result.Success)
            {
                return new ConfirmProductCompletionResponse();
            }

            _logger.LogWarning(
                "ConfirmProductCompletion failed for order {OrderId}: {Message}",
                request.Id, result.ErrorMessage);

            return new ConfirmProductCompletionResponse(ErrorCodes.InvalidOperation)
            {
                Message = result.ErrorMessage,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error confirming product completion for order {OrderId}", request.Id);
            return new ConfirmProductCompletionResponse(ErrorCodes.InternalServerError)
            {
                Message = UnexpectedErrorMessage,
            };
        }
    }
}
```

- [ ] **Step 4: Run tests and verify they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ConfirmProductCompletionHandlerTests"
```
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/ConfirmProductCompletion/ConfirmProductCompletionHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Manufacture/ConfirmProductCompletionHandlerTests.cs
git commit -m "feat: add ConfirmProductCompletionHandler"
```

---

## Task 6: Refactor controller tests to use IMediator for confirm endpoints

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Controllers/ManufactureOrderControllerTests.cs`

We rewrite the `ConfirmSemiProductManufacture` tests to mock `IMediator` instead of `IManufactureOrderApplicationService`, and add equivalent tests for `ConfirmProductCompletion` that pin the four observable HTTP behaviors required by arch-review NFR-3. We also drop the `_applicationServiceMock` constructor parameter — the controller's ctor will change to take only `IMediator` and `IConfiguration` in Task 7.

> **Order note:** This task changes the test's `new ManufactureOrderController(...)` call to a 2-arg signature, which will not compile until Task 7 updates the controller. We commit this task **after** Task 7's controller refactor compiles. The TDD red→green order is: write the new tests (this task) → run them (RED, controller doesn't match) → update controller (Task 7) → tests turn GREEN → commit both together at the end of Task 7. To keep the steps independent and reviewable, we'll still do them in two tasks but defer the commit on this task's diff until after Task 7's code is in place.

- [ ] **Step 1: Update the test class constructor to drop the application service mock**

In `backend/test/Anela.Heblo.Tests/Controllers/ManufactureOrderControllerTests.cs`, replace lines 33-43 (the field declarations and constructor up to the `_controller = new ManufactureOrderController(...)` line) with:

```csharp
    private readonly Mock<IMediator> _mediatorMock;
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly ManufactureOrderController _controller;

    public ManufactureOrderControllerTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _configurationMock = new Mock<IConfiguration>();
        _controller = new ManufactureOrderController(_mediatorMock.Object, _configurationMock.Object);
```

Also remove the `using Anela.Heblo.Application.Features.Manufacture.Services;` import (no longer needed) **only after** verifying nothing else in the file uses it.

- [ ] **Step 2: Replace the ConfirmSemiProductManufacture region with mediator-based tests**

Find the `#region ConfirmSemiProductManufacture Tests` block (around lines 732-808) and replace its contents with the following four tests plus a new ConfirmProductCompletion region:

```csharp
    #region ConfirmSemiProductManufacture Tests

    [Fact]
    public async Task ConfirmSemiProductManufacture_Should_Return_Ok_When_Successful()
    {
        // Arrange
        var orderId = 1;
        var request = new ConfirmSemiProductManufactureRequest { Id = orderId, ActualQuantity = 10m };
        var handlerResponse = new ConfirmSemiProductManufactureResponse
        {
            Message = "Polotovar byl úspěšně vyroben se skutečným množstvím 10",
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<ConfirmSemiProductManufactureRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(handlerResponse);

        // Act
        var actionResult = await _controller.ConfirmSemiProductManufacture(orderId, request);

        // Assert
        actionResult.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task ConfirmSemiProductManufacture_Should_Return_500_When_InternalServerError()
    {
        // Arrange
        var orderId = 1;
        var request = new ConfirmSemiProductManufactureRequest { Id = orderId, ActualQuantity = 10m };
        var handlerResponse = new ConfirmSemiProductManufactureResponse(ErrorCodes.InternalServerError)
        {
            Message = "DB error",
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<ConfirmSemiProductManufactureRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(handlerResponse);

        // Act
        var actionResult = await _controller.ConfirmSemiProductManufacture(orderId, request);

        // Assert
        var statusResult = actionResult.Result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task ConfirmSemiProductManufacture_Should_Return_502_When_ErpGatewayError()
    {
        // Arrange
        var orderId = 1;
        var request = new ConfirmSemiProductManufactureRequest { Id = orderId, ActualQuantity = 10m };
        var handlerResponse = new ConfirmSemiProductManufactureResponse(ErrorCodes.ErpGatewayError)
        {
            Message = "ERP timeout",
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<ConfirmSemiProductManufactureRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(handlerResponse);

        // Act
        var actionResult = await _controller.ConfirmSemiProductManufacture(orderId, request);

        // Assert
        var statusResult = actionResult.Result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(502);
    }

    [Fact]
    public async Task ConfirmSemiProductManufacture_Should_Return_BadRequest_When_Id_Mismatch()
    {
        // Arrange
        var request = new ConfirmSemiProductManufactureRequest { Id = 2, ActualQuantity = 10m };

        // Act
        var actionResult = await _controller.ConfirmSemiProductManufacture(1, request);

        // Assert
        actionResult.Result.Should().BeOfType<BadRequestObjectResult>();
        _mediatorMock.Verify(
            m => m.Send(It.IsAny<ConfirmSemiProductManufactureRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region ConfirmProductCompletion Tests

    [Fact]
    public async Task ConfirmProductCompletion_Should_Return_Ok_When_Successful()
    {
        // Arrange
        var orderId = 1;
        var request = new ConfirmProductCompletionRequest
        {
            Id = orderId,
            Products = new List<ProductActualQuantityRequest>
            {
                new() { Id = 10, ActualQuantity = 5m },
            },
        };
        var handlerResponse = new ConfirmProductCompletionResponse();

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<ConfirmProductCompletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(handlerResponse);

        // Act
        var actionResult = await _controller.ConfirmProductCompletion(orderId, request);

        // Assert
        actionResult.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task ConfirmProductCompletion_Should_Return_Ok_When_RequiresConfirmation()
    {
        // Arrange
        var orderId = 1;
        var request = new ConfirmProductCompletionRequest
        {
            Id = orderId,
            Products = new List<ProductActualQuantityRequest>
            {
                new() { Id = 10, ActualQuantity = 5m },
            },
        };
        var handlerResponse = new ConfirmProductCompletionResponse
        {
            RequiresConfirmation = true,
            Distribution = new ResidueDistributionDto
            {
                ActualSemiProductQuantity = 15m,
                IsWithinAllowedThreshold = false,
            },
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<ConfirmProductCompletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(handlerResponse);

        // Act
        var actionResult = await _controller.ConfirmProductCompletion(orderId, request);

        // Assert
        var okResult = actionResult.Result.Should().BeOfType<OkObjectResult>().Subject;
        var responseValue = okResult.Value.Should().BeOfType<ConfirmProductCompletionResponse>().Subject;
        responseValue.RequiresConfirmation.Should().BeTrue();
        responseValue.Distribution.Should().NotBeNull();
    }

    [Fact]
    public async Task ConfirmProductCompletion_Should_Return_BadRequest_When_InvalidOperation()
    {
        // Arrange
        var orderId = 1;
        var request = new ConfirmProductCompletionRequest
        {
            Id = orderId,
            Products = new List<ProductActualQuantityRequest>
            {
                new() { Id = 10, ActualQuantity = 5m },
            },
        };
        var handlerResponse = new ConfirmProductCompletionResponse(ErrorCodes.InvalidOperation)
        {
            Message = "workflow failure",
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<ConfirmProductCompletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(handlerResponse);

        // Act
        var actionResult = await _controller.ConfirmProductCompletion(orderId, request);

        // Assert
        actionResult.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ConfirmProductCompletion_Should_Return_500_When_InternalServerError()
    {
        // Arrange
        var orderId = 1;
        var request = new ConfirmProductCompletionRequest
        {
            Id = orderId,
            Products = new List<ProductActualQuantityRequest>
            {
                new() { Id = 10, ActualQuantity = 5m },
            },
        };
        var handlerResponse = new ConfirmProductCompletionResponse(ErrorCodes.InternalServerError)
        {
            Message = "boom",
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<ConfirmProductCompletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(handlerResponse);

        // Act
        var actionResult = await _controller.ConfirmProductCompletion(orderId, request);

        // Assert
        var statusResult = actionResult.Result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task ConfirmProductCompletion_Should_Return_BadRequest_When_Id_Mismatch()
    {
        // Arrange
        var request = new ConfirmProductCompletionRequest
        {
            Id = 2,
            Products = new List<ProductActualQuantityRequest>
            {
                new() { Id = 10, ActualQuantity = 5m },
            },
        };

        // Act
        var actionResult = await _controller.ConfirmProductCompletion(1, request);

        // Assert
        actionResult.Result.Should().BeOfType<BadRequestObjectResult>();
        _mediatorMock.Verify(
            m => m.Send(It.IsAny<ConfirmProductCompletionRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion
```

- [ ] **Step 3: Try to build — confirm it fails because the controller constructor still takes 3 args**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```
Expected: FAIL — `ManufactureOrderController` constructor expects 3 args but test passes 2. This is the RED state. Proceed to Task 7 to fix the controller.

> **Do not commit yet.** The diff in this task does not compile on its own. Both this and Task 7 are committed together at the end of Task 7.

---

## Task 7: Refactor ManufactureOrderController to use MediatR for confirm endpoints

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/ManufactureOrderController.cs`

Drop `IManufactureOrderApplicationService` from constructor and field declarations, replace both endpoint bodies with a single-line MediatR dispatch (matching the other eight endpoints in this controller), and remove the `MapResidueDistributionToDto` private helper.

- [ ] **Step 1: Replace the controller's fields and constructor**

Replace lines 26-39 of `backend/src/Anela.Heblo.API/Controllers/ManufactureOrderController.cs`:

From:
```csharp
public class ManufactureOrderController : BaseApiController
{
    private readonly IMediator _mediator;
    private readonly IConfiguration _configuration;
    private readonly IManufactureOrderApplicationService _manufacturingApplicationService;

    public ManufactureOrderController(
        IMediator mediator,
        IConfiguration configuration,
        IManufactureOrderApplicationService manufacturingApplicationService)
    {
        _mediator = mediator;
        _configuration = configuration;
        _manufacturingApplicationService = manufacturingApplicationService;
    }
```

To:
```csharp
public class ManufactureOrderController : BaseApiController
{
    private readonly IMediator _mediator;
    private readonly IConfiguration _configuration;

    public ManufactureOrderController(
        IMediator mediator,
        IConfiguration configuration)
    {
        _mediator = mediator;
        _configuration = configuration;
    }
```

- [ ] **Step 2: Replace the ConfirmSemiProductManufacture endpoint body**

Replace lines 102-142 with:

```csharp
    /// <summary>
    /// Confirm semi-product manufacture with actual quantity and change state from Planned to SemiProductManufactured
    /// </summary>
    [HttpPost("{id}/confirm-semi-product")]
    public async Task<ActionResult<ConfirmSemiProductManufactureResponse>> ConfirmSemiProductManufacture(int id, [FromBody] ConfirmSemiProductManufactureRequest request)
    {
        if (id != request.Id)
        {
            return BadRequest("ID in URL does not match ID in request body.");
        }

        var response = await _mediator.Send(request);
        return HandleResponse(response);
    }
```

- [ ] **Step 3: Replace the ConfirmProductCompletion endpoint body and delete the private mapper**

Replace lines 144-222 (the entire `ConfirmProductCompletion` method plus the `MapResidueDistributionToDto` helper) with:

```csharp
    /// <summary>
    /// Confirm product completion with actual quantities and change state from SemiProductManufactured to Completed
    /// </summary>
    [HttpPost("{id}/confirm-products")]
    public async Task<ActionResult<ConfirmProductCompletionResponse>> ConfirmProductCompletion(int id, [FromBody] ConfirmProductCompletionRequest request)
    {
        if (id != request.Id)
        {
            return BadRequest("ID in URL does not match ID in request body.");
        }

        var response = await _mediator.Send(request);
        return HandleResponse(response);
    }
```

- [ ] **Step 4: Remove the now-unused `using Anela.Heblo.Application.Features.Manufacture.Services;` import**

Locate the using directives at the top of the file and remove the line:
```csharp
using Anela.Heblo.Application.Features.Manufacture.Services;
```

(Verify with a quick grep that no other type from that namespace is referenced from `ManufactureOrderController.cs`.)

- [ ] **Step 5: Build the solution**

```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: build succeeds. (If `ManufactureOrderControllerProtocolTests.cs` still passes the old 3-arg ctor, this build will fail — fix that file in Task 8 before this build can succeed. To allow this step to pass standalone, also apply Task 8 Step 1 inline here, then continue.)

> Note: `ManufactureOrderControllerProtocolTests.cs` line 26 calls `new ManufactureOrderController(_mediatorMock.Object, configMock.Object, serviceMock.Object)`. Update this call in this step too (drop `serviceMock.Object` and remove the `serviceMock` local) so the solution compiles. Move the full cleanup of that file into Task 8 if more lines need touching.

Apply this minimal edit to `backend/test/Anela.Heblo.Tests/Features/Manufacture/ManufactureOrderControllerProtocolTests.cs`:

From:
```csharp
        var configMock = new Mock<IConfiguration>();
        var serviceMock = new Mock<IManufactureOrderApplicationService>();

        _controller = new ManufactureOrderController(_mediatorMock.Object, configMock.Object, serviceMock.Object);
```

To:
```csharp
        var configMock = new Mock<IConfiguration>();

        _controller = new ManufactureOrderController(_mediatorMock.Object, configMock.Object);
```

Also remove the now-unused `using Anela.Heblo.Application.Features.Manufacture.Services;` import at the top of that file.

- [ ] **Step 6: Run the controller tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ManufactureOrderControllerTests|FullyQualifiedName~ManufactureOrderControllerProtocolTests"
```
Expected: PASS — all ManufactureOrderControllerTests (including the new ConfirmProductCompletion tests from Task 6) and the protocol tests.

- [ ] **Step 7: Commit Tasks 6 + 7 together**

```bash
git add backend/src/Anela.Heblo.API/Controllers/ManufactureOrderController.cs \
        backend/test/Anela.Heblo.Tests/Controllers/ManufactureOrderControllerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Manufacture/ManufactureOrderControllerProtocolTests.cs
git commit -m "refactor: route confirm-semi-product and confirm-products through MediatR"
```

---

## Task 8: Remove IManufactureOrderApplicationService and its tests

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureModule.cs`
- Delete: `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/IManufactureOrderApplicationService.cs`
- Delete: `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ManufactureOrderApplicationService.cs`
- Delete: `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/ManufactureOrderApplicationServiceTests.cs`

Per spec FR-7 and arch-review §Specification Amendments #5: the controller is the only production consumer of `IManufactureOrderApplicationService`. After Task 7 the service has zero remaining call sites and can be deleted entirely. The two workflows it delegated to (`IConfirmSemiProductManufactureWorkflow`, `IConfirmProductCompletionWorkflow`) remain registered and used directly by the new handlers — see `ManufactureModule.cs` lines 54-55.

- [ ] **Step 1: Verify no remaining usages of the service interface or implementation**

```bash
grep -r "IManufactureOrderApplicationService\|ManufactureOrderApplicationService" backend/src backend/test --include="*.cs"
```
Expected output: only matches inside the three files we're about to delete plus the one DI registration line in `ManufactureModule.cs`. If anything else appears, stop and re-evaluate before deleting.

- [ ] **Step 2: Remove the DI registration from `ManufactureModule.cs`**

In `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureModule.cs`, delete this line (currently line 51):

```csharp
        services.AddScoped<IManufactureOrderApplicationService, ManufactureOrderApplicationService>();
```

Also drop the `using Anela.Heblo.Application.Features.Manufacture.Services;` import **only if** no remaining types in `ManufactureModule.cs` come from that namespace. Verify by quickly scanning the file before removal — `IProductNameFormatter`, `IManufactureNameBuilder`, `IConfirmSemiProductManufactureWorkflow`, `IConfirmProductCompletionWorkflow` and others may still need it. The simplest safe action is to leave the `using` as-is.

- [ ] **Step 3: Delete the service files**

```bash
git rm backend/src/Anela.Heblo.Application/Features/Manufacture/Services/IManufactureOrderApplicationService.cs \
       backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ManufactureOrderApplicationService.cs
```

- [ ] **Step 4: Delete the service tests**

The two `ManufactureOrderApplicationServiceTests` cases only proved that the service delegated to its workflows — a redundant test once the service is gone. The underlying workflow behavior is independently covered by `ConfirmSemiProductManufactureWorkflowTests.cs` and `ConfirmProductCompletionWorkflowTests.cs`, and the new handler tests now cover the dispatch path that the service used to occupy.

```bash
git rm backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/ManufactureOrderApplicationServiceTests.cs
```

If the `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/` directory becomes empty after the delete, leave it — git won't track empty directories, and `Workflows/` is a sibling that still has files.

- [ ] **Step 5: Build the solution**

```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: PASS.

- [ ] **Step 6: Run the full backend test suite**

```bash
dotnet test backend/Anela.Heblo.sln
```
Expected: PASS (entire suite — verifies no other test relies on the deleted service).

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureModule.cs
git commit -m "refactor: remove unused IManufactureOrderApplicationService"
```

(The `git rm` calls in Steps 3 and 4 already staged the deletions for this commit.)

---

## Task 9: Final validation and OpenAPI client check

- [ ] **Step 1: Run `dotnet format`**

```bash
dotnet format backend/Anela.Heblo.sln
```
Expected: zero diffs (or only whitespace fixes — if so, re-stage and amend the last commit or create a small follow-up `chore: format` commit).

- [ ] **Step 2: Full backend build + test**

```bash
dotnet build backend/Anela.Heblo.sln && dotnet test backend/Anela.Heblo.sln
```
Expected: build succeeds, all tests pass.

- [ ] **Step 3: Regenerate the OpenAPI TypeScript client and verify zero diff in affected DTOs**

The TS client regenerates automatically on `npm run build` (see `docs/development/api-client-generation.md`). Run from the worktree root:

```bash
cd frontend && npm run build && cd ..
git status frontend/src/api-client/
```
Expected: `git status` reports **no changes** under `frontend/src/api-client/`. If a diff appears (especially in files containing `ConfirmSemiProductManufactureRequest`, `ConfirmProductCompletionRequest`, `ConfirmSemiProductManufactureResponse`, `ConfirmProductCompletionResponse`, `ResidueDistributionDto`, or `ProductConsumptionDistributionDto`), inspect it. Acceptable diffs are limited to comment/whitespace shuffles introduced by NSwag. Any real schema change — property name, type, optional-ness — fails NFR-1 (Behavioral equivalence) and must be investigated.

If `frontend/src/api-client/` is unchanged, no commit is needed. If only whitespace/comments changed and you want a clean tree, commit:

```bash
git add frontend/src/api-client/
git commit -m "chore: regenerate OpenAPI TypeScript client"
```

- [ ] **Step 4: Spot-check the four observable HTTP status codes via the controller tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ManufactureOrderControllerTests.ConfirmSemiProductManufacture|FullyQualifiedName~ManufactureOrderControllerTests.ConfirmProductCompletion"
```
Expected: all named tests pass, confirming the four NFR-3 status-code invariants:
- success → 200 (`ConfirmSemiProductManufacture_Should_Return_Ok_When_Successful`, `ConfirmProductCompletion_Should_Return_Ok_When_Successful`, `ConfirmProductCompletion_Should_Return_Ok_When_RequiresConfirmation`)
- workflow failure (confirm-products) → 400 (`ConfirmProductCompletion_Should_Return_BadRequest_When_InvalidOperation`)
- exception/internal error → 500 (`ConfirmSemiProductManufacture_Should_Return_500_When_InternalServerError`, `ConfirmProductCompletion_Should_Return_500_When_InternalServerError`)
- ERP gateway error → 502 (`ConfirmSemiProductManufacture_Should_Return_502_When_ErpGatewayError`)

- [ ] **Step 5: Final git status check**

```bash
git status
git log --oneline -8
```
Expected: clean working tree. Commits visible in order: Task 1, Task 2, Task 3, Task 4, Task 5, Task 6+7 combined, Task 8 (and optional Task 9 Step 3 commit).

---

## Self-Review

Performed against the spec (`spec.r1.md`) and arch review (`arch-review.r1.md`) — both included as input artifacts.

**Spec coverage:**
- FR-1 (mark `ConfirmSemiProductManufactureRequest` IRequest) → Task 1.
- FR-2 (mark `ConfirmProductCompletionRequest` IRequest) → Task 2.
- FR-3 (`ConfirmSemiProductManufactureHandler`) → Task 4. Acceptance criterion "registered automatically via existing MediatR assembly scan" — satisfied because `ApplicationModule.cs` already calls `cfg.RegisterServicesFromAssembly(typeof(ApplicationModule).Assembly)` and the new handler lives in that assembly. Handler-local `try/catch` per arch-review amendment #3 is in the implementation.
- FR-4 (`ConfirmProductCompletionHandler`) → Task 5. Same automatic registration applies; null-distribution `InvalidOperationException` invariant per arch-review amendment #4 is in the implementation.
- FR-5 (relocate `ResidueDistribution` mapping to AutoMapper profile) → Task 3, with the additional `ProductConsumptionDistribution → ProductConsumptionDistributionDto` map AutoMapper needs to traverse the nested list. The handler in Task 5 uses `IMapper.Map<ResidueDistributionDto>(...)`. The unit test covers representative inputs including zero-quantity rows and empty product list.
- FR-6 (controller refactor) → Tasks 6 (tests) + 7 (controller). Both endpoints reduce to `return HandleResponse(await _mediator.Send(request));` per arch-review amendment #2. No `try/catch`, no manual 500. The `IManufactureOrderApplicationService` field and ctor parameter are removed.
- FR-7 (delete unused service) → Task 8. Grep verified before deletion. Tests deleted because they only exercised delegation that no longer exists.
- NFR-1 (behavioral equivalence) → Preserved by exception-mapping in handlers + `HandleResponse` status-code mapping. Pinned by Task 9 Step 4.
- NFR-2 (performance) → No new I/O, no new allocations beyond a single MediatR dispatch hop. Out of scope to benchmark.
- NFR-3 (test coverage) → Handler tests in Tasks 4 & 5; mapping test in Task 3; controller tests in Task 6; full suite re-run in Task 9.
- NFR-4 (consistency) → Both handlers follow the `ResolveManualActionHandler` pattern (private const error message, private `_logger`, `try/catch` returning `XxxResponse(ErrorCodes.InternalServerError)`).

**Placeholder scan:** No "TBD", no "Similar to Task N", no "add appropriate error handling". Every code step shows full code blocks. Every command shows expected output.

**Type consistency:**
- `ConfirmSemiProductManufactureHandler` (Tasks 4, 6) — name consistent.
- `ConfirmProductCompletionHandler` (Tasks 5, 6) — name consistent.
- `ConfirmSemiProductManufactureResponse(ErrorCodes.InternalServerError)` and `ConfirmProductCompletionResponse(ErrorCodes.InternalServerError)` — both classes have this ctor (verified against `Contracts/ConfirmSemiProductManufactureResponse.cs:11` and `Contracts/ConfirmProductCompletionResponse.cs:13`).
- `ConfirmSemiProductManufactureResult(success, message, errorCode)` — positional ctor verified against `Contracts/ConfirmSemiProductManufactureResult.cs:11`.
- `ConfirmProductCompletionResult` constructors — verified: parameterless = success, `(string errorMessage)` = failure, `NeedsConfirmation(distribution)` static factory = confirmation required.
- `IConfirmSemiProductManufactureWorkflow.ExecuteAsync(int, decimal, string?, CancellationToken)` — verified against `Services/Workflows/ConfirmSemiProductManufactureWorkflow.cs:15-19`.
- `IConfirmProductCompletionWorkflow.ExecuteAsync(int, Dictionary<int, decimal>, bool, string?, CancellationToken)` — verified against `Services/Workflows/ConfirmProductCompletionWorkflow.cs:15-21`.
- `BaseApiController.HandleResponse<T>(T)` — verified against `BaseApiController.cs:28`. Returns `BadRequest`/`NotFound`/`Unauthorized`/`Forbid`/`StatusCode` based on `ErrorCodes` attribute. `InvalidOperation`'s attribute = `BadRequest` (verified at `ErrorCodes.cs:28-29`), `InternalServerError`'s = `InternalServerError` (line 32-33), `ErpGatewayError`'s = `BadGateway` (line 314-315) — all match the test expectations.
- `ResidueDistributionDto` 7 fields × `ProductConsumptionDistributionDto` 8 fields — names match `ResidueDistribution` / `ProductConsumptionDistribution` exactly (verified by side-by-side compare of `Contracts/ResidueDistributionDto.cs` and `Domain/Features/Manufacture/ResidueDistribution.cs`). AutoMapper convention mapping handles the lot.

Plan is consistent, complete, and trace-able to spec + arch review. Ready to execute.
