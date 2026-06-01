# Shipment Label Retrieval — Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `POST /api/shipment-labels` endpoint that fetches shipment label payloads (PDF URL + ZPL) from the Shoptet Delivery API given an order code, returning them for the Baleni kiosk to print.

**Architecture:** Thin `ShipmentLabels` feature module in Application layer defines `IShipmentClient`; the existing `Anela.Heblo.Adapters.ShoptetApi` adapter implements it with `ShoptetShipmentClient` (reusing `ShoptetApiSettings`). A MediatR handler `GetOrderShipmentLabelsHandler` drives business logic; a new controller dispatches it.

**Tech Stack:** .NET 8 · MediatR · FluentValidation · xUnit · FluentAssertions · Moq

---

## File Map

**Create:**
- `docs/integrations/shoptet-api.md` — add Delivery API section (mandatory before use, per CLAUDE.md)
- `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` — add 29XX codes
- `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/IShipmentClient.cs`
- `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/ShipmentLabel.cs`
- `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/Contracts/ShipmentLabelDto.cs`
- `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/UseCases/GetOrderShipmentLabels/GetOrderShipmentLabelsRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/UseCases/GetOrderShipmentLabels/GetOrderShipmentLabelsResponse.cs`
- `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/UseCases/GetOrderShipmentLabels/GetOrderShipmentLabelsHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/Validators/GetOrderShipmentLabelsRequestValidator.cs`
- `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/ShipmentLabelsModule.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/Dto/ShoptetShipmentListResponse.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/Dto/ShoptetShipmentDto.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/Dto/ShoptetPackageDto.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/Dto/ShoptetErrorDto.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/ShoptetShipmentClient.cs`
- `backend/src/Anela.Heblo.API/Controllers/ShipmentLabelsController.cs`
- `backend/test/Anela.Heblo.Tests/Application/ShipmentLabels/GetOrderShipmentLabelsHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Application/ShipmentLabels/GetOrderShipmentLabelsRequestValidatorTests.cs`
- `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetShipmentClientTests.cs`

**Modify:**
- `docs/integrations/shoptet-api.md` — add Delivery API section
- `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` — add 29XX enum values
- `backend/src/Anela.Heblo.Application/ApplicationModule.cs` — call `AddShipmentLabelsModule`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs` — register `ShoptetShipmentClient`

---

## Task 1: Document the Shoptet Delivery API

Per CLAUDE.md: API findings must be documented **before** the code relies on them.

**Files:**
- Modify: `docs/integrations/shoptet-api.md`

- [ ] **Step 1: Add Delivery API section to shoptet-api.md**

Append the following section to the end of `docs/integrations/shoptet-api.md`:

```markdown

---

## 10. Delivery API

### 10.1 Shipments Endpoint

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/shipments` | List shipments (filter by `orderCode`) |

### 10.2 Filtering

Pass `orderCode` as a query parameter to retrieve shipments for a specific order:

```
GET /api/shipments?orderCode={orderCode}
```

Optional `status` filter also available (not used in this integration).

### 10.3 Response Envelope

```json
{
  "data": {
    "items": [
      {
        "guid": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
        "orderCode": "0001234",
        "packages": [
          {
            "name": "Zásilka 1",
            "width": 20,
            "height": 10,
            "depth": 5,
            "weight": 0.5,
            "packagingId": 1,
            "labelUrl": "https://api.myshoptet.com/api/shipments/{guid}/label.pdf",
            "labelZpl": "^XA^FO50,50^ADN,36,20^FDHello ZPL^FS^XZ",
            "trackingNumber": "TRK123456",
            "trackingUrl": "https://carrier.cz/track/TRK123456"
          }
        ]
      }
    ],
    "paginator": {
      "totalCount": 1,
      "itemsPerPage": 10,
      "currentPage": 1,
      "pageCount": 1
    }
  },
  "errors": []
}
```

### 10.4 Error Envelope

When Shoptet returns an error (non-2xx or populated `errors[]`):

```json
{
  "data": null,
  "errors": [
    {
      "errorCode": "shipment-not-found",
      "message": "Shipment not found for given order",
      "instance": "/api/shipments?orderCode=0001234"
    }
  ]
}
```

### 10.5 Labels

- `labelUrl` — PDF download URL, may be `null` if the label has not been generated yet.
- `labelZpl` — Raw Zebra ZPL string for direct USB printing, may be `null`.
- An order may have multiple shipments, each with multiple packages. All are returned; the kiosk prints each.
- If both `labelUrl` and `labelZpl` are `null` for all packages, labels have not been generated yet.

### 10.6 Authentication

Same host (`https://api.myshoptet.com`) and `Shoptet-Private-API-Token` header as all other Shoptet endpoints. `ShoptetApiSettings.BaseUrl` and `ShoptetApiSettings.ApiToken` are reused — no new configuration keys.
```

- [ ] **Step 2: Commit**

```bash
git add docs/integrations/shoptet-api.md
git commit -m "docs: document Shoptet Delivery API GET /api/shipments endpoint"
```

---

## Task 2: Add ShipmentLabels Error Codes (29XX)

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`

- [ ] **Step 1: Add 29XX values to the ErrorCodes enum**

In `ErrorCodes.cs`, after the `// Inventory module errors (28XX)` block (after line 307 `LotHasEans = 2806,`) and before `// External Service errors (90XX)`, insert:

```csharp
    // ShipmentLabels module errors (29XX)
    [HttpStatusCode(HttpStatusCode.NotFound)]
    ShipmentLabelsNoShipmentFound = 2901,
    [HttpStatusCode(HttpStatusCode.UnprocessableEntity)]
    ShipmentLabelsNotGenerated = 2902,
```

- [ ] **Step 2: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs
git commit -m "feat(shipment-labels): add 29XX error codes for ShipmentLabels module"
```

---

## Task 3: Application Domain Types

These shells are required before tests can compile.

**Files:**
- Create: `IShipmentClient.cs`, `ShipmentLabel.cs`, `Contracts/ShipmentLabelDto.cs`
- Create: request, response (shells that compile), handler shell

- [ ] **Step 1: Create IShipmentClient.cs**

```csharp
// backend/src/Anela.Heblo.Application/Features/ShipmentLabels/IShipmentClient.cs
namespace Anela.Heblo.Application.Features.ShipmentLabels;

public interface IShipmentClient
{
    Task<IReadOnlyList<ShipmentLabel>> GetLabelsByOrderCodeAsync(string orderCode, CancellationToken ct = default);
}
```

- [ ] **Step 2: Create ShipmentLabel.cs**

```csharp
// backend/src/Anela.Heblo.Application/Features/ShipmentLabels/ShipmentLabel.cs
namespace Anela.Heblo.Application.Features.ShipmentLabels;

public class ShipmentLabel
{
    public Guid ShipmentGuid { get; set; }
    public string OrderCode { get; set; } = null!;
    public string PackageName { get; set; } = null!;
    public string? LabelUrl { get; set; }
    public string? LabelZpl { get; set; }
    public string? TrackingNumber { get; set; }
    public string? TrackingUrl { get; set; }
}
```

- [ ] **Step 3: Create Contracts/ShipmentLabelDto.cs**

```csharp
// backend/src/Anela.Heblo.Application/Features/ShipmentLabels/Contracts/ShipmentLabelDto.cs
namespace Anela.Heblo.Application.Features.ShipmentLabels.Contracts;

public class ShipmentLabelDto
{
    public Guid ShipmentGuid { get; set; }
    public string PackageName { get; set; } = null!;
    public string? LabelUrl { get; set; }
    public string? LabelZpl { get; set; }
    public bool HasPdf => LabelUrl is not null;
    public bool HasZpl => LabelZpl is not null;
    public string? TrackingNumber { get; set; }
    public string? TrackingUrl { get; set; }
}
```

- [ ] **Step 4: Create GetOrderShipmentLabelsRequest.cs**

```csharp
// backend/src/Anela.Heblo.Application/Features/ShipmentLabels/UseCases/GetOrderShipmentLabels/GetOrderShipmentLabelsRequest.cs
using MediatR;

namespace Anela.Heblo.Application.Features.ShipmentLabels.UseCases.GetOrderShipmentLabels;

public class GetOrderShipmentLabelsRequest : IRequest<GetOrderShipmentLabelsResponse>
{
    public string OrderCode { get; set; } = null!;
}
```

- [ ] **Step 5: Create GetOrderShipmentLabelsResponse.cs**

```csharp
// backend/src/Anela.Heblo.Application/Features/ShipmentLabels/UseCases/GetOrderShipmentLabels/GetOrderShipmentLabelsResponse.cs
using Anela.Heblo.Application.Features.ShipmentLabels.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.ShipmentLabels.UseCases.GetOrderShipmentLabels;

public class GetOrderShipmentLabelsResponse : BaseResponse
{
    public IReadOnlyList<ShipmentLabelDto> Labels { get; set; } = [];

    public GetOrderShipmentLabelsResponse(IReadOnlyList<ShipmentLabelDto> labels)
    {
        Labels = labels;
    }

    public GetOrderShipmentLabelsResponse(ErrorCodes errorCode, Dictionary<string, string>? @params = null)
        : base(errorCode, @params)
    {
    }
}
```

- [ ] **Step 6: Create GetOrderShipmentLabelsHandler.cs (shell that compiles)**

```csharp
// backend/src/Anela.Heblo.Application/Features/ShipmentLabels/UseCases/GetOrderShipmentLabels/GetOrderShipmentLabelsHandler.cs
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.ShipmentLabels.UseCases.GetOrderShipmentLabels;

public class GetOrderShipmentLabelsHandler : IRequestHandler<GetOrderShipmentLabelsRequest, GetOrderShipmentLabelsResponse>
{
    private readonly IShipmentClient _shipmentClient;
    private readonly ILogger<GetOrderShipmentLabelsHandler> _logger;

    public GetOrderShipmentLabelsHandler(
        IShipmentClient shipmentClient,
        ILogger<GetOrderShipmentLabelsHandler> logger)
    {
        _shipmentClient = shipmentClient;
        _logger = logger;
    }

    public Task<GetOrderShipmentLabelsResponse> Handle(
        GetOrderShipmentLabelsRequest request,
        CancellationToken cancellationToken)
        => throw new NotImplementedException();
}
```

- [ ] **Step 7: Verify the project compiles**

```bash
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --no-restore 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ShipmentLabels/
git commit -m "feat(shipment-labels): add application domain types, request/response, handler shell"
```

---

## Task 4: Handler Unit Tests → Implement → Green

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Application/ShipmentLabels/GetOrderShipmentLabelsHandlerTests.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/UseCases/GetOrderShipmentLabels/GetOrderShipmentLabelsHandler.cs`

- [ ] **Step 1: Write the failing handler tests**

```csharp
// backend/test/Anela.Heblo.Tests/Application/ShipmentLabels/GetOrderShipmentLabelsHandlerTests.cs
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShipmentLabels.UseCases.GetOrderShipmentLabels;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Tests.Application.ShipmentLabels;

public class GetOrderShipmentLabelsHandlerTests
{
    private readonly Mock<IShipmentClient> _clientMock = new();

    private GetOrderShipmentLabelsHandler CreateHandler() =>
        new(_clientMock.Object, NullLogger<GetOrderShipmentLabelsHandler>.Instance);

    [Fact]
    public async Task Handle_OrderWithSinglePackage_ReturnsLabelDto()
    {
        // Arrange
        var shipmentGuid = Guid.NewGuid();
        _clientMock.Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ShipmentLabel
                {
                    ShipmentGuid = shipmentGuid,
                    OrderCode = "0001234",
                    PackageName = "Zásilka 1",
                    LabelUrl = "https://example.com/label.pdf",
                    LabelZpl = "^XA^XZ",
                    TrackingNumber = "TRK001",
                    TrackingUrl = "https://carrier.cz/TRK001",
                }
            ]);

        // Act
        var response = await CreateHandler().Handle(
            new GetOrderShipmentLabelsRequest { OrderCode = "0001234" },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.Labels.Should().HaveCount(1);
        var dto = response.Labels[0];
        dto.ShipmentGuid.Should().Be(shipmentGuid);
        dto.PackageName.Should().Be("Zásilka 1");
        dto.LabelUrl.Should().Be("https://example.com/label.pdf");
        dto.LabelZpl.Should().Be("^XA^XZ");
        dto.HasPdf.Should().BeTrue();
        dto.HasZpl.Should().BeTrue();
        dto.TrackingNumber.Should().Be("TRK001");
        dto.TrackingUrl.Should().Be("https://carrier.cz/TRK001");
    }

    [Fact]
    public async Task Handle_OrderWithMultiplePackages_ReturnsAllLabels()
    {
        // Arrange
        _clientMock.Setup(c => c.GetLabelsByOrderCodeAsync("0002345", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ShipmentLabel { ShipmentGuid = Guid.NewGuid(), OrderCode = "0002345", PackageName = "P1", LabelUrl = "https://x.com/1.pdf" },
                new ShipmentLabel { ShipmentGuid = Guid.NewGuid(), OrderCode = "0002345", PackageName = "P2", LabelZpl = "^XA^XZ" },
            ]);

        // Act
        var response = await CreateHandler().Handle(
            new GetOrderShipmentLabelsRequest { OrderCode = "0002345" },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.Labels.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_OrderWithLabelUrlOnlyPackage_HasPdfTrueHasZplFalse()
    {
        // Arrange
        _clientMock.Setup(c => c.GetLabelsByOrderCodeAsync("0003456", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ShipmentLabel { ShipmentGuid = Guid.NewGuid(), OrderCode = "0003456", PackageName = "P1", LabelUrl = "https://x.com/1.pdf", LabelZpl = null }
            ]);

        // Act
        var response = await CreateHandler().Handle(
            new GetOrderShipmentLabelsRequest { OrderCode = "0003456" },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.Labels[0].HasPdf.Should().BeTrue();
        response.Labels[0].HasZpl.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_NoShipmentForOrder_ReturnsNoShipmentFoundError()
    {
        // Arrange
        _clientMock.Setup(c => c.GetLabelsByOrderCodeAsync("0001111", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var response = await CreateHandler().Handle(
            new GetOrderShipmentLabelsRequest { OrderCode = "0001111" },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ShipmentLabelsNoShipmentFound);
        response.Params.Should().ContainKey("orderCode").WhoseValue.Should().Be("0001111");
    }

    [Fact]
    public async Task Handle_AllPackagesHaveNullLabels_ReturnsNotGeneratedError()
    {
        // Arrange
        _clientMock.Setup(c => c.GetLabelsByOrderCodeAsync("0002222", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ShipmentLabel { ShipmentGuid = Guid.NewGuid(), OrderCode = "0002222", PackageName = "P1", LabelUrl = null, LabelZpl = null }
            ]);

        // Act
        var response = await CreateHandler().Handle(
            new GetOrderShipmentLabelsRequest { OrderCode = "0002222" },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ShipmentLabelsNotGenerated);
        response.Params.Should().ContainKey("orderCode").WhoseValue.Should().Be("0002222");
    }

    [Fact]
    public async Task Handle_ShipmentClientThrows_ReturnsInternalServerError()
    {
        // Arrange
        _clientMock.Setup(c => c.GetLabelsByOrderCodeAsync("0003333", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Shoptet unavailable"));

        // Act
        var response = await CreateHandler().Handle(
            new GetOrderShipmentLabelsRequest { OrderCode = "0003333" },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.InternalServerError);
    }
}
```

- [ ] **Step 2: Run tests — verify they fail (NotImplementedException)**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetOrderShipmentLabelsHandlerTests" \
  --no-build 2>&1 | tail -20
```

Expected: Tests run and fail with `NotImplementedException` or similar.

- [ ] **Step 3: Implement the handler**

Replace the `Handle` method body in `GetOrderShipmentLabelsHandler.cs`:

```csharp
// backend/src/Anela.Heblo.Application/Features/ShipmentLabels/UseCases/GetOrderShipmentLabels/GetOrderShipmentLabelsHandler.cs
using Anela.Heblo.Application.Features.ShipmentLabels.Contracts;
using Anela.Heblo.Application.Shared;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.ShipmentLabels.UseCases.GetOrderShipmentLabels;

public class GetOrderShipmentLabelsHandler : IRequestHandler<GetOrderShipmentLabelsRequest, GetOrderShipmentLabelsResponse>
{
    private readonly IShipmentClient _shipmentClient;
    private readonly ILogger<GetOrderShipmentLabelsHandler> _logger;

    public GetOrderShipmentLabelsHandler(
        IShipmentClient shipmentClient,
        ILogger<GetOrderShipmentLabelsHandler> logger)
    {
        _shipmentClient = shipmentClient;
        _logger = logger;
    }

    public async Task<GetOrderShipmentLabelsResponse> Handle(
        GetOrderShipmentLabelsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var labels = await _shipmentClient.GetLabelsByOrderCodeAsync(request.OrderCode, cancellationToken);

            if (labels.Count == 0)
            {
                return new GetOrderShipmentLabelsResponse(
                    ErrorCodes.ShipmentLabelsNoShipmentFound,
                    new Dictionary<string, string> { { "orderCode", request.OrderCode } });
            }

            if (labels.All(l => l.LabelUrl is null && l.LabelZpl is null))
            {
                return new GetOrderShipmentLabelsResponse(
                    ErrorCodes.ShipmentLabelsNotGenerated,
                    new Dictionary<string, string> { { "orderCode", request.OrderCode } });
            }

            var dtos = labels.Select(l => new ShipmentLabelDto
            {
                ShipmentGuid = l.ShipmentGuid,
                PackageName = l.PackageName,
                LabelUrl = l.LabelUrl,
                LabelZpl = l.LabelZpl,
                TrackingNumber = l.TrackingNumber,
                TrackingUrl = l.TrackingUrl,
            }).ToList();

            return new GetOrderShipmentLabelsResponse(dtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get shipment labels for order {OrderCode}", request.OrderCode);
            return new GetOrderShipmentLabelsResponse(ErrorCodes.InternalServerError);
        }
    }
}
```

- [ ] **Step 4: Run tests — verify all pass**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetOrderShipmentLabelsHandlerTests" \
  --no-build 2>&1 | tail -10
```

Expected: `6 passed, 0 failed`

- [ ] **Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Application/ShipmentLabels/GetOrderShipmentLabelsHandlerTests.cs \
        backend/src/Anela.Heblo.Application/Features/ShipmentLabels/UseCases/GetOrderShipmentLabels/GetOrderShipmentLabelsHandler.cs
git commit -m "feat(shipment-labels): implement GetOrderShipmentLabelsHandler with unit tests"
```

---

## Task 5: Validator + Module + Wire ApplicationModule

**Files:**
- Create: `Validators/GetOrderShipmentLabelsRequestValidator.cs`
- Create: `ShipmentLabelsModule.cs`
- Create: `backend/test/Anela.Heblo.Tests/Application/ShipmentLabels/GetOrderShipmentLabelsRequestValidatorTests.cs`
- Modify: `backend/src/Anela.Heblo.Application/ApplicationModule.cs`

- [ ] **Step 1: Write failing validator tests**

```csharp
// backend/test/Anela.Heblo.Tests/Application/ShipmentLabels/GetOrderShipmentLabelsRequestValidatorTests.cs
using Anela.Heblo.Application.Features.ShipmentLabels.UseCases.GetOrderShipmentLabels;
using Anela.Heblo.Application.Features.ShipmentLabels.Validators;
using FluentValidation.TestHelper;

namespace Anela.Heblo.Tests.Application.ShipmentLabels;

public class GetOrderShipmentLabelsRequestValidatorTests
{
    private readonly GetOrderShipmentLabelsRequestValidator _validator = new();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyOrWhitespaceOrderCode_ReturnsValidationError(string orderCode)
    {
        // Arrange
        var request = new GetOrderShipmentLabelsRequest { OrderCode = orderCode };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.OrderCode);
    }

    [Fact]
    public void Validate_NullOrderCode_ReturnsValidationError()
    {
        // Arrange
        var request = new GetOrderShipmentLabelsRequest { OrderCode = null! };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.OrderCode);
    }

    [Fact]
    public void Validate_ValidOrderCode_ReturnsNoErrors()
    {
        // Arrange
        var request = new GetOrderShipmentLabelsRequest { OrderCode = "0001234" };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }
}
```

- [ ] **Step 2: Run tests — verify they fail (type not found)**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetOrderShipmentLabelsRequestValidatorTests" 2>&1 | tail -10
```

Expected: Compile error `GetOrderShipmentLabelsRequestValidator not found`.

- [ ] **Step 3: Create GetOrderShipmentLabelsRequestValidator.cs**

```csharp
// backend/src/Anela.Heblo.Application/Features/ShipmentLabels/Validators/GetOrderShipmentLabelsRequestValidator.cs
using Anela.Heblo.Application.Features.ShipmentLabels.UseCases.GetOrderShipmentLabels;
using FluentValidation;

namespace Anela.Heblo.Application.Features.ShipmentLabels.Validators;

public class GetOrderShipmentLabelsRequestValidator : AbstractValidator<GetOrderShipmentLabelsRequest>
{
    public GetOrderShipmentLabelsRequestValidator()
    {
        RuleFor(x => x.OrderCode)
            .NotEmpty()
            .WithMessage("OrderCode is required.");
    }
}
```

- [ ] **Step 4: Create ShipmentLabelsModule.cs**

```csharp
// backend/src/Anela.Heblo.Application/Features/ShipmentLabels/ShipmentLabelsModule.cs
using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.ShipmentLabels.UseCases.GetOrderShipmentLabels;
using Anela.Heblo.Application.Features.ShipmentLabels.Validators;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.ShipmentLabels;

public static class ShipmentLabelsModule
{
    public static IServiceCollection AddShipmentLabelsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IValidator<GetOrderShipmentLabelsRequest>, GetOrderShipmentLabelsRequestValidator>();
        services.AddScoped<
            IPipelineBehavior<GetOrderShipmentLabelsRequest, GetOrderShipmentLabelsResponse>,
            ValidationBehavior<GetOrderShipmentLabelsRequest, GetOrderShipmentLabelsResponse>>();

        return services;
    }
}
```

- [ ] **Step 5: Run validator tests — verify all pass**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetOrderShipmentLabelsRequestValidatorTests" \
  --no-build 2>&1 | tail -10
```

Expected: `4 passed, 0 failed`

- [ ] **Step 6: Wire ApplicationModule.cs**

In `ApplicationModule.cs`, add the `using` directive at the top:

```csharp
using Anela.Heblo.Application.Features.ShipmentLabels;
```

Then add the module call after `services.AddShoptetOrdersModule(configuration);` (line 90):

```csharp
        services.AddShipmentLabelsModule(configuration);
```

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ShipmentLabels/Validators/ \
        backend/src/Anela.Heblo.Application/Features/ShipmentLabels/ShipmentLabelsModule.cs \
        backend/src/Anela.Heblo.Application/ApplicationModule.cs \
        backend/test/Anela.Heblo.Tests/Application/ShipmentLabels/GetOrderShipmentLabelsRequestValidatorTests.cs
git commit -m "feat(shipment-labels): add validator, module registration, wire ApplicationModule"
```

---

## Task 6: Adapter DTOs + Client Tests → Implement → Green

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/Dto/ShoptetShipmentListResponse.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/Dto/ShoptetShipmentDto.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/Dto/ShoptetPackageDto.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/Dto/ShoptetErrorDto.cs`
- Create: `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetShipmentClientTests.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/ShoptetShipmentClient.cs`

- [ ] **Step 1: Create adapter DTOs**

```csharp
// backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/Dto/ShoptetShipmentListResponse.cs
using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Shipments.Dto;

public class ShoptetShipmentListResponse
{
    [JsonPropertyName("data")]
    public ShoptetShipmentListData? Data { get; set; }

    [JsonPropertyName("errors")]
    public List<ShoptetErrorDto>? Errors { get; set; }
}

public class ShoptetShipmentListData
{
    [JsonPropertyName("items")]
    public List<ShoptetShipmentDto>? Items { get; set; }
}
```

```csharp
// backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/Dto/ShoptetShipmentDto.cs
using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Shipments.Dto;

public class ShoptetShipmentDto
{
    [JsonPropertyName("guid")]
    public Guid Guid { get; set; }

    [JsonPropertyName("orderCode")]
    public string? OrderCode { get; set; }

    [JsonPropertyName("packages")]
    public List<ShoptetPackageDto>? Packages { get; set; }
}
```

```csharp
// backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/Dto/ShoptetPackageDto.cs
using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Shipments.Dto;

public class ShoptetPackageDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("labelUrl")]
    public string? LabelUrl { get; set; }

    [JsonPropertyName("labelZpl")]
    public string? LabelZpl { get; set; }

    [JsonPropertyName("trackingNumber")]
    public string? TrackingNumber { get; set; }

    [JsonPropertyName("trackingUrl")]
    public string? TrackingUrl { get; set; }
}
```

```csharp
// backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/Dto/ShoptetErrorDto.cs
using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Shipments.Dto;

public class ShoptetErrorDto
{
    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("instance")]
    public string? Instance { get; set; }
}
```

- [ ] **Step 2: Write the failing client tests**

```csharp
// backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetShipmentClientTests.cs
using System.Net;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.Shipments;
using FluentAssertions;

namespace Anela.Heblo.Tests.Adapters.ShoptetApi;

public class ShoptetShipmentClientTests
{
    private static ShoptetShipmentClient BuildClient(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var http = new HttpClient(new FakeDelegatingHandler(handler))
        {
            BaseAddress = new Uri("https://fake.shoptet.cz"),
        };
        return new ShoptetShipmentClient(http);
    }

    private static HttpResponseMessage Json(object obj)
    {
        var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    [Fact]
    public async Task GetLabelsByOrderCodeAsync_WithSinglePackage_ReturnsMappedLabel()
    {
        // Arrange
        var shipmentGuid = Guid.NewGuid();
        var client = BuildClient(_ => Json(new
        {
            data = new
            {
                items = new[]
                {
                    new
                    {
                        guid = shipmentGuid,
                        orderCode = "0001234",
                        packages = new[]
                        {
                            new
                            {
                                name = "Zásilka 1",
                                labelUrl = "https://api.myshoptet.com/label.pdf",
                                labelZpl = "^XA^XZ",
                                trackingNumber = "TRK001",
                                trackingUrl = "https://carrier.cz/TRK001",
                            }
                        },
                    }
                },
            },
            errors = Array.Empty<object>(),
        }));

        // Act
        var result = await client.GetLabelsByOrderCodeAsync("0001234");

        // Assert
        result.Should().HaveCount(1);
        var label = result[0];
        label.ShipmentGuid.Should().Be(shipmentGuid);
        label.OrderCode.Should().Be("0001234");
        label.PackageName.Should().Be("Zásilka 1");
        label.LabelUrl.Should().Be("https://api.myshoptet.com/label.pdf");
        label.LabelZpl.Should().Be("^XA^XZ");
        label.TrackingNumber.Should().Be("TRK001");
        label.TrackingUrl.Should().Be("https://carrier.cz/TRK001");
    }

    [Fact]
    public async Task GetLabelsByOrderCodeAsync_WithMultipleShipmentsAndPackages_ReturnsAllFlattened()
    {
        // Arrange
        var guid1 = Guid.NewGuid();
        var guid2 = Guid.NewGuid();
        var client = BuildClient(_ => Json(new
        {
            data = new
            {
                items = new[]
                {
                    new
                    {
                        guid = guid1,
                        orderCode = "0001234",
                        packages = new[]
                        {
                            new { name = "P1", labelUrl = "https://x.com/1.pdf", labelZpl = (string?)null, trackingNumber = (string?)null, trackingUrl = (string?)null },
                            new { name = "P2", labelUrl = (string?)null, labelZpl = "^XA^XZ", trackingNumber = (string?)null, trackingUrl = (string?)null },
                        },
                    },
                    new
                    {
                        guid = guid2,
                        orderCode = "0001234",
                        packages = new[]
                        {
                            new { name = "P3", labelUrl = "https://x.com/3.pdf", labelZpl = (string?)null, trackingNumber = (string?)null, trackingUrl = (string?)null },
                        },
                    },
                },
            },
            errors = Array.Empty<object>(),
        }));

        // Act
        var result = await client.GetLabelsByOrderCodeAsync("0001234");

        // Assert
        result.Should().HaveCount(3);
        result.Select(l => l.PackageName).Should().Equal("P1", "P2", "P3");
        result[0].ShipmentGuid.Should().Be(guid1);
        result[2].ShipmentGuid.Should().Be(guid2);
    }

    [Fact]
    public async Task GetLabelsByOrderCodeAsync_WithEmptyItems_ReturnsEmptyList()
    {
        // Arrange
        var client = BuildClient(_ => Json(new
        {
            data = new { items = Array.Empty<object>() },
            errors = Array.Empty<object>(),
        }));

        // Act
        var result = await client.GetLabelsByOrderCodeAsync("0001234");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLabelsByOrderCodeAsync_WhenShoptetReturnsNonSuccess_ThrowsHttpRequestException()
    {
        // Arrange
        var client = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("Service Unavailable", Encoding.UTF8, "text/plain"),
        });

        // Act
        var act = () => client.GetLabelsByOrderCodeAsync("0001234");

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*503*");
    }

    [Fact]
    public async Task GetLabelsByOrderCodeAsync_WhenShoptetReturnsErrorsArray_ThrowsHttpRequestException()
    {
        // Arrange
        var client = BuildClient(_ => Json(new
        {
            data = (object?)null,
            errors = new[]
            {
                new { errorCode = "shipment-not-found", message = "Shipment not found", instance = "/api/shipments?orderCode=0001234" }
            },
        }));

        // Act
        var act = () => client.GetLabelsByOrderCodeAsync("0001234");

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*Shipment not found*");
    }

    [Fact]
    public async Task GetLabelsByOrderCodeAsync_UsesCorrectQueryString()
    {
        // Arrange
        HttpRequestMessage? capturedRequest = null;
        var client = BuildClient(req =>
        {
            capturedRequest = req;
            return Json(new { data = new { items = Array.Empty<object>() }, errors = Array.Empty<object>() });
        });

        // Act
        await client.GetLabelsByOrderCodeAsync("MY-ORDER-CODE");

        // Assert
        capturedRequest!.RequestUri!.PathAndQuery.Should().Be("/api/shipments?orderCode=MY-ORDER-CODE");
    }
}
```

- [ ] **Step 3: Run tests — verify they fail (type not found)**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ShoptetShipmentClientTests" 2>&1 | tail -10
```

Expected: Compile error `ShoptetShipmentClient not found`.

- [ ] **Step 4: Create ShoptetShipmentClient.cs**

```csharp
// backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/ShoptetShipmentClient.cs
using System.Net.Http.Json;
using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.Shipments.Dto;
using Anela.Heblo.Application.Features.ShipmentLabels;

namespace Anela.Heblo.Adapters.ShoptetApi.Shipments;

public class ShoptetShipmentClient : IShipmentClient
{
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ShoptetShipmentClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<ShipmentLabel>> GetLabelsByOrderCodeAsync(
        string orderCode,
        CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/shipments?orderCode={orderCode}", ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"GET /api/shipments?orderCode={orderCode} returned {(int)response.StatusCode}: {body}");
        }

        var data = await response.Content.ReadFromJsonAsync<ShoptetShipmentListResponse>(JsonOptions, ct);

        if (data?.Errors is { Count: > 0 })
        {
            var errorMsg = string.Join("; ", data.Errors.Select(e => e.Message));
            throw new HttpRequestException($"Shoptet Delivery API error for order {orderCode}: {errorMsg}");
        }

        var items = data?.Data?.Items ?? [];

        return items
            .SelectMany(shipment => (shipment.Packages ?? [])
                .Select(pkg => new ShipmentLabel
                {
                    ShipmentGuid = shipment.Guid,
                    OrderCode = orderCode,
                    PackageName = pkg.Name ?? string.Empty,
                    LabelUrl = pkg.LabelUrl,
                    LabelZpl = pkg.LabelZpl,
                    TrackingNumber = pkg.TrackingNumber,
                    TrackingUrl = pkg.TrackingUrl,
                }))
            .ToList();
    }
}
```

- [ ] **Step 5: Run tests — verify all pass**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ShoptetShipmentClientTests" \
  --no-build 2>&1 | tail -10
```

Expected: `6 passed, 0 failed`

- [ ] **Step 6: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/ \
        backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetShipmentClientTests.cs
git commit -m "feat(shipment-labels): add ShoptetShipmentClient with adapter DTOs and unit tests"
```

---

## Task 7: Wire Adapter DI + Create Controller

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs`
- Create: `backend/src/Anela.Heblo.API/Controllers/ShipmentLabelsController.cs`

- [ ] **Step 1: Register ShoptetShipmentClient in the adapter DI extension**

In `ShoptetApiAdapterServiceCollectionExtensions.cs`, add the missing `using` directives at the top:

```csharp
using Anela.Heblo.Adapters.ShoptetApi.Shipments;
using Anela.Heblo.Application.Features.ShipmentLabels;
```

Then add the following registration after the `ShoptetInvoiceClient` block (after line 52 `});`):

```csharp
        services.AddHttpClient<IShipmentClient, ShoptetShipmentClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<ShoptetApiSettings>>().Value;
            client.BaseAddress = new Uri(settings.BaseUrl);
            client.DefaultRequestHeaders.Add("Shoptet-Private-API-Token", settings.ApiToken);
        });
```

- [ ] **Step 2: Create ShipmentLabelsController.cs**

```csharp
// backend/src/Anela.Heblo.API/Controllers/ShipmentLabelsController.cs
using Anela.Heblo.Application.Features.ShipmentLabels.UseCases.GetOrderShipmentLabels;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[ApiController]
[Route("api/shipment-labels")]
public class ShipmentLabelsController : BaseApiController
{
    private readonly IMediator _mediator;

    public ShipmentLabelsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Returns shipment label payloads (PDF URL and/or ZPL) for an order.
    /// The Baleni kiosk uses these to print on a USB-connected Zebra printer.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<GetOrderShipmentLabelsResponse>> GetLabels(
        [FromBody] GetShipmentLabelsRequest body)
    {
        var response = await _mediator.Send(new GetOrderShipmentLabelsRequest
        {
            OrderCode = body.OrderCode,
        });

        return HandleResponse(response);
    }
}

public class GetShipmentLabelsRequest
{
    [JsonPropertyName("orderCode")]
    public string OrderCode { get; set; } = null!;
}
```

- [ ] **Step 3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs \
        backend/src/Anela.Heblo.API/Controllers/ShipmentLabelsController.cs
git commit -m "feat(shipment-labels): wire DI, add ShipmentLabelsController POST /api/shipment-labels"
```

---

## Task 8: Build Verification

- [ ] **Step 1: dotnet build — full solution**

```bash
cd backend && dotnet build 2>&1 | tail -10
```

Expected: `Build succeeded.`  
If it fails: fix the reported compile errors before proceeding.

- [ ] **Step 2: dotnet format — verify no formatting changes needed**

```bash
cd backend && dotnet format --verify-no-changes 2>&1 | tail -5
```

If there are formatting issues (exit code non-zero), run `dotnet format` to apply them and stage the changes.

- [ ] **Step 3: Run all new unit tests**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ShipmentLabels" 2>&1 | tail -15
```

Expected: All tests pass (handler × 6, validator × 4, client × 6 = 16 total).

- [ ] **Step 4: Run the full test suite — confirm no regressions**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj 2>&1 | tail -10
```

Expected: All previously passing tests still pass.

- [ ] **Step 5: Commit any formatting fixes (if step 2 found issues)**

```bash
git add -u
git commit -m "chore: apply dotnet format to shipment-labels files"
```

- [ ] **Step 6: Confirm OpenAPI client will regenerate (informational)**

```bash
cd backend && grep -r "shipment-labels\|ShipmentLabels" src/Anela.Heblo.API/Controllers/ShipmentLabelsController.cs | head -3
```

Expected: `Route("api/shipment-labels")` visible — the TypeScript client will pick up the new endpoint on the next `npm run build`.

---

## Verification Checklist

Before declaring done:

- [ ] `dotnet build` passes with zero errors
- [ ] `dotnet format --verify-no-changes` passes (or format applied and committed)
- [ ] All 16 unit tests pass (handler, validator, client)
- [ ] No regressions in the full test suite
- [ ] `POST /api/shipment-labels` route is present in the built API (visible in Swagger or OpenAPI spec)
- [ ] Manual staging check (out of scope for this plan — see spec §Verification step 3)
