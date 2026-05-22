# Packaging Orchestration BE Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move shipment-label orchestration from a multi-step FE state machine into a single `POST /api/packaging/orders/{orderCode}/label` BE endpoint, collapsing the Balení packing screen to one HTTP round trip.

**Architecture:** A new `PackagingController` routes to a new `PrepareOrderLabelHandler` that encapsulates eligibility check, duplicate guard, shipment creation, and label polling. `GetPackingOrderResponse` gains a server-rendered `PackingEligibility` object, moving Czech warning strings out of the FE. Old `ShipmentLabelsController` routes stay as aliases this PR; deleted in a follow-up.

**Tech Stack:** .NET 8, MediatR, FluentValidation, xUnit, Moq, FluentAssertions; React, TanStack Query, TypeScript, Tailwind

---

## File Map

**Created (BE)**
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/GetPackingOrder/PackingEligibility.cs` — eligibility DTO
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/PrepareOrderLabel/PrepareOrderLabelRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/PrepareOrderLabel/PrepareOrderLabelResponse.cs`
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/PrepareOrderLabel/PrepareOrderLabelHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Packaging/Validators/PrepareOrderLabelRequestValidator.cs`
- `backend/src/Anela.Heblo.API/Controllers/PackagingController.cs`
- `backend/test/Anela.Heblo.Tests/Application/Packaging/PrepareOrderLabelHandlerTests.cs`

**Modified (BE)**
- `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` — add `OrderNotInPackingState = 3001`
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/GetPackingOrder/GetPackingOrderResponse.cs` — drop `StatusId`/`IsInPackingState`, add `Eligibility`
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/GetPackingOrder/GetPackingOrderHandler.cs` — emit `Eligibility` with localized warning text

**Created (FE)**
- `frontend/src/api/hooks/usePrepareOrderLabel.ts`

**Modified (FE)**
- `frontend/src/api/hooks/usePackingOrder.ts` — type update: drop `statusId`/`isInPackingState`, add `eligibility`
- `frontend/src/components/baleni/PackingStateWarning.tsx` — render BE warning strings
- `frontend/src/components/baleni/BaleniPacking.tsx` — gate on `eligibility.isEligible`
- `frontend/src/components/baleni/PackingShipmentCreator.tsx` — collapse state machine
- `frontend/src/components/baleni/PackingLabelPrinter.tsx` — accept `labels` prop, drop `useShipmentLabels`
- `frontend/src/components/baleni/printLabelPdf.ts` — point at new route
- `frontend/src/api/client.ts` — remove `shipmentLabels` key from `QUERY_KEYS`

**Deleted (FE)**
- `frontend/src/api/hooks/useCreateShipment.ts`
- `frontend/src/api/hooks/useShipmentLabels.ts`

---

## Task 1: Add `OrderNotInPackingState` error code

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`

- [ ] **Step 1: Add Packaging module error section to ErrorCodes.cs**

  Open `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`. After the last enum entry (`ShoptetApiError = 9005`), there is already a pattern. Find the `// External Service errors (90XX)` section (near the end of the file) and add the Packaging block **before** it, between `BackgroundJobs (19XX)` region and `KnowledgeBase (20XX)` region — actually insert it as a new module after `Inventory (28XX)` but before `WeatherForecast (29XX)`. Locate the line:

  ```csharp
      // WeatherForecast module errors (29XX)
  ```

  Insert just before that line:

  ```csharp
      // Packaging module errors (30XX)
      [HttpStatusCode(HttpStatusCode.Conflict)]
      OrderNotInPackingState = 3001,

  ```

- [ ] **Step 2: Verify the file compiles**

  ```bash
  cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/quebec && dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --no-restore 2>&1 | tail -5
  ```

  Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

  ```bash
  git add backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs
  git commit -m "feat(packaging): add OrderNotInPackingState error code (HTTP 409)"
  ```

---

## Task 2: Add `PackingEligibility` DTO and update `GetPackingOrderResponse` + handler

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/GetPackingOrder/PackingEligibility.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/GetPackingOrder/GetPackingOrderResponse.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/GetPackingOrder/GetPackingOrderHandler.cs`

- [ ] **Step 1: Write failing tests for updated GetPackingOrderHandler**

  Check if a test file exists:
  ```bash
  find backend/test -name "GetPackingOrderHandlerTests.cs"
  ```

  If it exists, open it and add the two test cases below. If it does not exist, create the file at `backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/GetPackingOrderHandlerTests.cs` with this content:

  ```csharp
  using Anela.Heblo.Application.Features.ShoptetOrders;
  using Anela.Heblo.Application.Features.ShoptetOrders.UseCases.GetPackingOrder;
  using Anela.Heblo.Application.Shared;
  using FluentAssertions;
  using Microsoft.Extensions.Options;
  using Moq;
  using Xunit;

  namespace Anela.Heblo.Tests.Application.ShoptetOrders;

  public class GetPackingOrderHandlerTests
  {
      private readonly Mock<IPackingOrderClient> _clientMock = new();
      private readonly ShoptetOrdersSettings _settings = new() { PackingStateId = 26 };
      private readonly Mock<Microsoft.Extensions.Logging.ILogger<GetPackingOrderHandler>> _loggerMock = new();

      private GetPackingOrderHandler CreateHandler() =>
          new(_clientMock.Object, Options.Create(_settings), _loggerMock.Object);

      [Fact]
      public async Task Handle_WhenOrderIsInPackingState_ReturnsEligibleWithNullWarning()
      {
          // Arrange
          var order = new PackingOrder
          {
              Code = "ORD001",
              CustomerName = "Jan Novák",
              ShippingMethodName = "PPL",
              StatusId = 26,
              Items = [],
          };
          _clientMock
              .Setup(x => x.GetPackingOrderAsync("ORD001", It.IsAny<CancellationToken>()))
              .ReturnsAsync(order);
          var handler = CreateHandler();

          // Act
          var result = await handler.Handle(new GetPackingOrderRequest { Code = "ORD001" }, CancellationToken.None);

          // Assert
          result.Success.Should().BeTrue();
          result.Eligibility.IsEligible.Should().BeTrue();
          result.Eligibility.WarningTitle.Should().BeNull();
          result.Eligibility.WarningBody.Should().BeNull();
      }

      [Fact]
      public async Task Handle_WhenOrderIsNotInPackingState_ReturnsIneligibleWithCzechWarning()
      {
          // Arrange
          var order = new PackingOrder
          {
              Code = "ORD002",
              CustomerName = "Jana Nováková",
              ShippingMethodName = "PPL",
              StatusId = 99,
              Items = [],
          };
          _clientMock
              .Setup(x => x.GetPackingOrderAsync("ORD002", It.IsAny<CancellationToken>()))
              .ReturnsAsync(order);
          var handler = CreateHandler();

          // Act
          var result = await handler.Handle(new GetPackingOrderRequest { Code = "ORD002" }, CancellationToken.None);

          // Assert
          result.Success.Should().BeTrue();
          result.Eligibility.IsEligible.Should().BeFalse();
          result.Eligibility.WarningTitle.Should().Be("Objednávka není ve stavu „Balí se"");
          result.Eligibility.WarningBody.Should().Be("Tuto objednávku nezpracovávejte, dokud nebude ve správném stavu.");
      }

      [Fact]
      public async Task Handle_WhenOrderNotFound_ReturnsNotFoundError()
      {
          // Arrange
          _clientMock
              .Setup(x => x.GetPackingOrderAsync("MISSING", It.IsAny<CancellationToken>()))
              .ReturnsAsync((PackingOrder?)null);
          var handler = CreateHandler();

          // Act
          var result = await handler.Handle(new GetPackingOrderRequest { Code = "MISSING" }, CancellationToken.None);

          // Assert
          result.Success.Should().BeFalse();
          result.ErrorCode.Should().Be(ErrorCodes.ShoptetOrderNotFound);
      }
  }
  ```

- [ ] **Step 2: Run tests — expect FAIL (Eligibility property does not exist yet)**

  ```bash
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetPackingOrderHandlerTests" --no-build 2>&1 | tail -20
  ```

  Expected: Compilation error mentioning `Eligibility` does not exist on the response type.

- [ ] **Step 3: Create `PackingEligibility.cs`**

  Create `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/GetPackingOrder/PackingEligibility.cs`:

  ```csharp
  namespace Anela.Heblo.Application.Features.ShoptetOrders.UseCases.GetPackingOrder;

  public class PackingEligibility
  {
      public bool IsEligible { get; set; }
      public string? WarningTitle { get; set; }
      public string? WarningBody { get; set; }
  }
  ```

- [ ] **Step 4: Update `GetPackingOrderResponse.cs`**

  Replace the `StatusId` and `IsInPackingState` properties with `Eligibility`. The full updated file:

  ```csharp
  using Anela.Heblo.Application.Shared;
  using Anela.Heblo.Domain.Features.Catalog;

  namespace Anela.Heblo.Application.Features.ShoptetOrders.UseCases.GetPackingOrder;

  public class GetPackingOrderResponse : BaseResponse
  {
      public GetPackingOrderResponse()
      {
      }

      public GetPackingOrderResponse(ErrorCodes errorCode, Dictionary<string, string>? @params = null)
          : base(errorCode, @params)
      {
      }

      public string Code { get; set; } = string.Empty;
      public string CustomerName { get; set; } = string.Empty;
      public string ShippingMethodName { get; set; } = string.Empty;
      public Cooling Cooling { get; set; } = Cooling.None;
      public bool IsCooled { get; set; }
      public PackingEligibility Eligibility { get; set; } = new();
      public string? CustomerNote { get; set; }
      public string? EshopNote { get; set; }
      public List<PackingOrderItem> Items { get; set; } = new();
  }
  ```

- [ ] **Step 5: Update `GetPackingOrderHandler.cs`**

  Replace the success return block in `Handle`. The full updated handler:

  ```csharp
  using Anela.Heblo.Application.Shared;
  using MediatR;
  using Microsoft.Extensions.Logging;
  using Microsoft.Extensions.Options;

  namespace Anela.Heblo.Application.Features.ShoptetOrders.UseCases.GetPackingOrder;

  public class GetPackingOrderHandler : IRequestHandler<GetPackingOrderRequest, GetPackingOrderResponse>
  {
      private readonly IPackingOrderClient _client;
      private readonly IOptions<ShoptetOrdersSettings> _settings;
      private readonly ILogger<GetPackingOrderHandler> _logger;

      public GetPackingOrderHandler(
          IPackingOrderClient client,
          IOptions<ShoptetOrdersSettings> settings,
          ILogger<GetPackingOrderHandler> logger)
      {
          _client = client;
          _settings = settings;
          _logger = logger;
      }

      public async Task<GetPackingOrderResponse> Handle(
          GetPackingOrderRequest request,
          CancellationToken cancellationToken)
      {
          try
          {
              var order = await _client.GetPackingOrderAsync(request.Code, cancellationToken);

              if (order == null)
              {
                  return new GetPackingOrderResponse(
                      ErrorCodes.ShoptetOrderNotFound,
                      new Dictionary<string, string> { { "orderCode", request.Code } });
              }

              var isEligible = order.StatusId == _settings.Value.PackingStateId;

              return new GetPackingOrderResponse
              {
                  Code = order.Code,
                  CustomerName = order.CustomerName,
                  ShippingMethodName = order.ShippingMethodName,
                  Cooling = order.Cooling,
                  IsCooled = order.IsCooled,
                  Eligibility = new PackingEligibility
                  {
                      IsEligible = isEligible,
                      WarningTitle = isEligible ? null : "Objednávka není ve stavu „Balí se"",
                      WarningBody = isEligible ? null : "Tuto objednávku nezpracovávejte, dokud nebude ve správném stavu.",
                  },
                  CustomerNote = order.CustomerNote,
                  EshopNote = order.EshopNote,
                  Items = order.Items,
              };
          }
          catch (Exception ex)
          {
              _logger.LogError(ex, "Failed to load packing order {OrderCode}", request.Code);
              return new GetPackingOrderResponse(ErrorCodes.InternalServerError);
          }
      }
  }
  ```

- [ ] **Step 6: Run tests — expect PASS**

  ```bash
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetPackingOrderHandlerTests" 2>&1 | tail -10
  ```

  Expected: `3 passed, 0 failed`

- [ ] **Step 7: Full build to confirm nothing else broke**

  ```bash
  dotnet build backend/Anela.Heblo.sln --no-restore 2>&1 | tail -5
  ```

  Expected: `Build succeeded.`

- [ ] **Step 8: Commit**

  ```bash
  git add backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/GetPackingOrder/PackingEligibility.cs \
          backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/GetPackingOrder/GetPackingOrderResponse.cs \
          backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/GetPackingOrder/GetPackingOrderHandler.cs \
          backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/GetPackingOrderHandlerTests.cs
  git commit -m "feat(packaging): replace IsInPackingState with server-rendered PackingEligibility"
  ```

---

## Task 3: Create `PrepareOrderLabel` request/response DTOs

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/PrepareOrderLabel/PrepareOrderLabelRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/PrepareOrderLabel/PrepareOrderLabelResponse.cs`

- [ ] **Step 1: Create folder structure**

  ```bash
  mkdir -p backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/PrepareOrderLabel
  mkdir -p backend/src/Anela.Heblo.Application/Features/Packaging/Validators
  mkdir -p backend/test/Anela.Heblo.Tests/Application/Packaging
  ```

- [ ] **Step 2: Create `PrepareOrderLabelRequest.cs`**

  ```csharp
  using MediatR;

  namespace Anela.Heblo.Application.Features.Packaging.UseCases.PrepareOrderLabel;

  public class PrepareOrderLabelRequest : IRequest<PrepareOrderLabelResponse>
  {
      public string OrderCode { get; set; } = null!;
      public bool ForceRecreate { get; set; }
  }
  ```

- [ ] **Step 3: Create `PrepareOrderLabelResponse.cs`**

  ```csharp
  using Anela.Heblo.Application.Features.ShipmentLabels.Contracts;
  using Anela.Heblo.Application.Shared;

  namespace Anela.Heblo.Application.Features.Packaging.UseCases.PrepareOrderLabel;

  public class PrepareOrderLabelResponse : BaseResponse
  {
      public bool ExistingShipmentFound { get; set; }
      public bool LabelReady { get; set; }
      public IReadOnlyList<ShipmentLabelDto> Labels { get; set; } = [];

      // Success: new or recreated shipment
      public PrepareOrderLabelResponse(bool labelReady, IReadOnlyList<ShipmentLabelDto> labels)
      {
          LabelReady = labelReady;
          Labels = labels;
      }

      // Success: labels already existed and forceRecreate=false
      public PrepareOrderLabelResponse(IReadOnlyList<ShipmentLabelDto> existingLabels)
      {
          ExistingShipmentFound = true;
          LabelReady = existingLabels.Any(l => l.LabelUrl is not null);
          Labels = existingLabels;
      }

      // Error
      public PrepareOrderLabelResponse(ErrorCodes errorCode) : base(errorCode)
      {
      }
  }
  ```

- [ ] **Step 4: Verify compilation**

  ```bash
  dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --no-restore 2>&1 | tail -5
  ```

  Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

  ```bash
  git add backend/src/Anela.Heblo.Application/Features/Packaging/
  git commit -m "feat(packaging): add PrepareOrderLabel request and response DTOs"
  ```

---

## Task 4: Write `PrepareOrderLabelHandler` tests (TDD — RED)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Application/Packaging/PrepareOrderLabelHandlerTests.cs`

> The handler does not exist yet — the test file will compile but all tests will fail with a missing type error until Task 5 is complete.

- [ ] **Step 1: Create `PrepareOrderLabelHandlerTests.cs`**

  ```csharp
  using Anela.Heblo.Application.Features.Packaging.UseCases.PrepareOrderLabel;
  using Anela.Heblo.Application.Features.ShipmentLabels;
  using Anela.Heblo.Application.Features.ShipmentLabels.Contracts;
  using Anela.Heblo.Application.Features.ShoptetOrders;
  using Anela.Heblo.Application.Shared;
  using FluentAssertions;
  using Microsoft.Extensions.Logging;
  using Microsoft.Extensions.Options;
  using Moq;
  using Xunit;

  namespace Anela.Heblo.Tests.Application.Packaging;

  public class PrepareOrderLabelHandlerTests
  {
      private readonly Mock<IShipmentClient> _shipmentClient = new();
      private readonly Mock<IPackingOrderClient> _orderClient = new();
      private readonly ShipmentLabelsSettings _shipmentSettings = new()
      {
          MinPackageWeightGrams = 100,
          DefaultPackageWidthMm = 300,
          DefaultPackageHeightMm = 200,
          DefaultPackageDepthMm = 150,
      };
      private readonly ShoptetOrdersSettings _orderSettings = new() { PackingStateId = 26 };
      private readonly Mock<ILogger<PrepareOrderLabelHandler>> _logger = new();

      private PrepareOrderLabelHandler CreateHandler() =>
          new(
              _shipmentClient.Object,
              _orderClient.Object,
              Options.Create(_shipmentSettings),
              Options.Create(_orderSettings),
              _logger.Object);

      private static PackingOrder OrderInPackingState(string code = "ORD001") => new()
      {
          Code = code,
          StatusId = 26,
          Items = [new PackingOrderItem { Name = "Produkt A", Quantity = 1, WeightGrams = 500 }],
      };

      private static ShipmentLabel MakeLabel(string? labelUrl = "https://cdn/label.pdf") => new()
      {
          ShipmentGuid = Guid.NewGuid(),
          OrderCode = "ORD001",
          PackageName = "package-1",
          LabelUrl = labelUrl,
      };

      // ── 1. Eligibility check ─────────────────────────────────────────────────

      [Fact]
      public async Task Handle_WhenOrderNotInPackingState_ReturnsOrderNotInPackingState()
      {
          // Arrange
          var order = OrderInPackingState();
          order.StatusId = 99; // wrong state
          _orderClient.Setup(x => x.GetPackingOrderAsync("ORD001", It.IsAny<CancellationToken>()))
              .ReturnsAsync(order);

          // Act
          var result = await CreateHandler().Handle(
              new PrepareOrderLabelRequest { OrderCode = "ORD001" }, CancellationToken.None);

          // Assert
          result.Success.Should().BeFalse();
          result.ErrorCode.Should().Be(ErrorCodes.OrderNotInPackingState);
          _shipmentClient.Verify(x => x.GetLabelsByOrderCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
      }

      // ── 2. Duplicate guard ───────────────────────────────────────────────────

      [Fact]
      public async Task Handle_WhenLabelsExistAndForceRecreateIsFalse_ReturnsExistingShipmentFound()
      {
          // Arrange
          var order = OrderInPackingState();
          _orderClient.Setup(x => x.GetPackingOrderAsync("ORD001", It.IsAny<CancellationToken>()))
              .ReturnsAsync(order);

          var existingLabel = MakeLabel();
          _shipmentClient.Setup(x => x.GetLabelsByOrderCodeAsync("ORD001", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<ShipmentLabel> { existingLabel });

          // Act
          var result = await CreateHandler().Handle(
              new PrepareOrderLabelRequest { OrderCode = "ORD001", ForceRecreate = false }, CancellationToken.None);

          // Assert
          result.Success.Should().BeTrue();
          result.ExistingShipmentFound.Should().BeTrue();
          result.Labels.Should().HaveCount(1);
          _shipmentClient.Verify(x => x.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()), Times.Never);
      }

      // ── 3. Force recreate ────────────────────────────────────────────────────

      [Fact]
      public async Task Handle_WhenLabelsExistAndForceRecreateIsTrue_CreatesNewShipment()
      {
          // Arrange
          var order = OrderInPackingState();
          _orderClient.Setup(x => x.GetPackingOrderAsync("ORD001", It.IsAny<CancellationToken>()))
              .ReturnsAsync(order);

          var existingLabel = MakeLabel();
          var newLabel = MakeLabel();
          var callCount = 0;
          _shipmentClient.Setup(x => x.GetLabelsByOrderCodeAsync("ORD001", It.IsAny<CancellationToken>()))
              .ReturnsAsync(() => callCount++ == 0
                  ? (IReadOnlyList<ShipmentLabel>)new List<ShipmentLabel> { existingLabel }
                  : new List<ShipmentLabel> { newLabel });

          _shipmentClient.Setup(x => x.GetShippingOptionsAsync("ORD001", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<ShippingOption> { new() { CarrierCode = "PPL" } });

          _shipmentClient.Setup(x => x.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new CreatedShipment { ShipmentGuid = Guid.NewGuid(), Status = "created" });

          // Act
          var result = await CreateHandler().Handle(
              new PrepareOrderLabelRequest { OrderCode = "ORD001", ForceRecreate = true }, CancellationToken.None);

          // Assert
          result.Success.Should().BeTrue();
          result.ExistingShipmentFound.Should().BeFalse();
          _shipmentClient.Verify(x => x.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()), Times.Once);
      }

      // ── 4. Missing weight ────────────────────────────────────────────────────

      [Fact]
      public async Task Handle_WhenOrderItemsHaveZeroWeight_ReturnsShipmentOrderWeightUnavailable()
      {
          // Arrange
          var order = new PackingOrder
          {
              Code = "ORD001",
              StatusId = 26,
              Items = [new PackingOrderItem { Name = "Produkt A", Quantity = 1, WeightGrams = 0 }],
          };
          _orderClient.Setup(x => x.GetPackingOrderAsync("ORD001", It.IsAny<CancellationToken>()))
              .ReturnsAsync(order);
          _shipmentClient.Setup(x => x.GetLabelsByOrderCodeAsync("ORD001", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<ShipmentLabel>());

          // Act
          var result = await CreateHandler().Handle(
              new PrepareOrderLabelRequest { OrderCode = "ORD001" }, CancellationToken.None);

          // Assert
          result.Success.Should().BeFalse();
          result.ErrorCode.Should().Be(ErrorCodes.ShipmentOrderWeightUnavailable);
      }

      // ── 5. Missing carrier ───────────────────────────────────────────────────

      [Fact]
      public async Task Handle_WhenNoShippingOptionsAvailable_ReturnsShipmentCarrierNotResolved()
      {
          // Arrange
          var order = OrderInPackingState();
          _orderClient.Setup(x => x.GetPackingOrderAsync("ORD001", It.IsAny<CancellationToken>()))
              .ReturnsAsync(order);
          _shipmentClient.Setup(x => x.GetLabelsByOrderCodeAsync("ORD001", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<ShipmentLabel>());
          _shipmentClient.Setup(x => x.GetShippingOptionsAsync("ORD001", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<ShippingOption>());

          // Act
          var result = await CreateHandler().Handle(
              new PrepareOrderLabelRequest { OrderCode = "ORD001" }, CancellationToken.None);

          // Assert
          result.Success.Should().BeFalse();
          result.ErrorCode.Should().Be(ErrorCodes.ShipmentCarrierNotResolved);
      }

      // ── 6. Create failure ────────────────────────────────────────────────────

      [Fact]
      public async Task Handle_WhenCreateShipmentThrows_ReturnsShipmentCreationFailed()
      {
          // Arrange
          var order = OrderInPackingState();
          _orderClient.Setup(x => x.GetPackingOrderAsync("ORD001", It.IsAny<CancellationToken>()))
              .ReturnsAsync(order);
          _shipmentClient.Setup(x => x.GetLabelsByOrderCodeAsync("ORD001", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<ShipmentLabel>());
          _shipmentClient.Setup(x => x.GetShippingOptionsAsync("ORD001", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<ShippingOption> { new() { CarrierCode = "PPL" } });
          _shipmentClient.Setup(x => x.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new HttpRequestException("Shoptet API error"));

          // Act
          var result = await CreateHandler().Handle(
              new PrepareOrderLabelRequest { OrderCode = "ORD001" }, CancellationToken.None);

          // Assert
          result.Success.Should().BeFalse();
          result.ErrorCode.Should().Be(ErrorCodes.ShipmentCreationFailed);
      }

      // ── 7. Retry success path ────────────────────────────────────────────────

      [Fact]
      public async Task Handle_WhenFirstLabelPollReturnsEmpty_RetriesAndReturnsLabels()
      {
          // Arrange
          var order = OrderInPackingState();
          _orderClient.Setup(x => x.GetPackingOrderAsync("ORD001", It.IsAny<CancellationToken>()))
              .ReturnsAsync(order);

          // First call (duplicate guard): no labels
          // Second call (after create): no labels yet
          // Third call (after delay): labels ready
          var callCount = 0;
          _shipmentClient.Setup(x => x.GetLabelsByOrderCodeAsync("ORD001", It.IsAny<CancellationToken>()))
              .ReturnsAsync(() =>
              {
                  callCount++;
                  return callCount <= 2
                      ? (IReadOnlyList<ShipmentLabel>)new List<ShipmentLabel>()
                      : new List<ShipmentLabel> { MakeLabel() };
              });

          _shipmentClient.Setup(x => x.GetShippingOptionsAsync("ORD001", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<ShippingOption> { new() { CarrierCode = "PPL" } });
          _shipmentClient.Setup(x => x.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new CreatedShipment { ShipmentGuid = Guid.NewGuid(), Status = "created" });

          // Act
          var result = await CreateHandler().Handle(
              new PrepareOrderLabelRequest { OrderCode = "ORD001" }, CancellationToken.None);

          // Assert
          result.Success.Should().BeTrue();
          result.LabelReady.Should().BeTrue();
          result.Labels.Should().HaveCount(1);
          // This test takes ~3 seconds due to Task.Delay(3000) in the handler.
      }
  }
  ```

- [ ] **Step 2: Attempt to build — expect compilation error (handler missing)**

  ```bash
  dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-restore 2>&1 | grep -i "error" | head -10
  ```

  Expected: error about `PrepareOrderLabelHandler` type not found.

- [ ] **Step 3: Commit tests**

  ```bash
  git add backend/test/Anela.Heblo.Tests/Application/Packaging/PrepareOrderLabelHandlerTests.cs
  git commit -m "test(packaging): add PrepareOrderLabelHandler tests (RED)"
  ```

---

## Task 5: Implement `PrepareOrderLabelHandler` (TDD — GREEN)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/PrepareOrderLabel/PrepareOrderLabelHandler.cs`

- [ ] **Step 1: Create `PrepareOrderLabelHandler.cs`**

  ```csharp
  using Anela.Heblo.Application.Features.ShipmentLabels;
  using Anela.Heblo.Application.Features.ShipmentLabels.Contracts;
  using Anela.Heblo.Application.Features.ShoptetOrders;
  using Anela.Heblo.Application.Shared;
  using MediatR;
  using Microsoft.Extensions.Logging;
  using Microsoft.Extensions.Options;

  namespace Anela.Heblo.Application.Features.Packaging.UseCases.PrepareOrderLabel;

  public class PrepareOrderLabelHandler
      : IRequestHandler<PrepareOrderLabelRequest, PrepareOrderLabelResponse>
  {
      private readonly IShipmentClient _shipmentClient;
      private readonly IPackingOrderClient _orderClient;
      private readonly ShipmentLabelsSettings _shipmentSettings;
      private readonly ShoptetOrdersSettings _orderSettings;
      private readonly ILogger<PrepareOrderLabelHandler> _logger;

      public PrepareOrderLabelHandler(
          IShipmentClient shipmentClient,
          IPackingOrderClient orderClient,
          IOptions<ShipmentLabelsSettings> shipmentSettings,
          IOptions<ShoptetOrdersSettings> orderSettings,
          ILogger<PrepareOrderLabelHandler> logger)
      {
          _shipmentClient = shipmentClient;
          _orderClient = orderClient;
          _shipmentSettings = shipmentSettings.Value;
          _orderSettings = orderSettings.Value;
          _logger = logger;
      }

      public async Task<PrepareOrderLabelResponse> Handle(
          PrepareOrderLabelRequest request,
          CancellationToken cancellationToken)
      {
          try
          {
              // 1. Load order and enforce eligibility
              var order = await _orderClient.GetPackingOrderAsync(request.OrderCode, cancellationToken);
              if (order is null)
              {
                  return new PrepareOrderLabelResponse(ErrorCodes.ShoptetOrderNotFound);
              }

              if (order.StatusId != _orderSettings.PackingStateId)
              {
                  return new PrepareOrderLabelResponse(ErrorCodes.OrderNotInPackingState);
              }

              // 2. Duplicate guard
              var existingLabels = await _shipmentClient.GetLabelsByOrderCodeAsync(
                  request.OrderCode, cancellationToken);

              if (existingLabels.Count > 0 && !request.ForceRecreate)
              {
                  return new PrepareOrderLabelResponse(MapToDtos(existingLabels));
              }

              // 3. Compute package weight
              var totalWeightGrams = order.Items.Sum(i => i.WeightGrams * i.Quantity);
              if (totalWeightGrams == 0)
              {
                  return new PrepareOrderLabelResponse(ErrorCodes.ShipmentOrderWeightUnavailable);
              }

              var packageWeightGrams = Math.Max(totalWeightGrams, _shipmentSettings.MinPackageWeightGrams);

              // 4. Resolve carrier
              var shippingOptions = await _shipmentClient.GetShippingOptionsAsync(
                  request.OrderCode, cancellationToken);

              if (shippingOptions.Count == 0)
              {
                  return new PrepareOrderLabelResponse(ErrorCodes.ShipmentCarrierNotResolved);
              }

              // 5. Create shipment
              var command = new CreateShipmentCommand
              {
                  OrderCode = request.OrderCode,
                  CarrierCode = shippingOptions[0].CarrierCode,
                  Package = new ShipmentPackage
                  {
                      WidthMm = _shipmentSettings.DefaultPackageWidthMm,
                      HeightMm = _shipmentSettings.DefaultPackageHeightMm,
                      DepthMm = _shipmentSettings.DefaultPackageDepthMm,
                      WeightGrams = packageWeightGrams,
                  },
              };

              try
              {
                  await _shipmentClient.CreateShipmentAsync(command, cancellationToken);
              }
              catch (Exception ex)
              {
                  _logger.LogError(ex,
                      "Shoptet API failed to create shipment for order {OrderCode}", request.OrderCode);
                  return new PrepareOrderLabelResponse(ErrorCodes.ShipmentCreationFailed);
              }

              // 6. Fetch labels with one retry after 3-second delay
              var labels = await _shipmentClient.GetLabelsByOrderCodeAsync(
                  request.OrderCode, cancellationToken);
              var labelReady = labels.Any(l => l.LabelUrl is not null);

              if (!labelReady)
              {
                  await Task.Delay(3000, cancellationToken);
                  labels = await _shipmentClient.GetLabelsByOrderCodeAsync(
                      request.OrderCode, cancellationToken);
                  labelReady = labels.Any(l => l.LabelUrl is not null);
              }

              return new PrepareOrderLabelResponse(labelReady, MapToDtos(labels));
          }
          catch (Exception ex)
          {
              _logger.LogError(ex,
                  "Unexpected error preparing label for order {OrderCode}", request.OrderCode);
              return new PrepareOrderLabelResponse(ErrorCodes.InternalServerError);
          }
      }

      private static IReadOnlyList<ShipmentLabelDto> MapToDtos(IReadOnlyList<ShipmentLabel> labels) =>
          labels.Select(l => new ShipmentLabelDto
          {
              ShipmentGuid = l.ShipmentGuid,
              PackageName = l.PackageName,
              LabelUrl = l.LabelUrl,
              LabelZpl = l.LabelZpl,
              TrackingNumber = l.TrackingNumber,
              TrackingUrl = l.TrackingUrl,
          }).ToList();
  }
  ```

- [ ] **Step 2: Run tests — expect PASS (test 7 takes ~3 seconds)**

  ```bash
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PrepareOrderLabelHandlerTests" 2>&1 | tail -15
  ```

  Expected: `7 passed, 0 failed`

- [ ] **Step 3: Commit**

  ```bash
  git add backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/PrepareOrderLabel/PrepareOrderLabelHandler.cs
  git commit -m "feat(packaging): implement PrepareOrderLabelHandler"
  ```

---

## Task 6: Create `PrepareOrderLabelRequestValidator`

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Packaging/Validators/PrepareOrderLabelRequestValidator.cs`

- [ ] **Step 1: Create the validator**

  ```csharp
  using Anela.Heblo.Application.Features.Packaging.UseCases.PrepareOrderLabel;
  using FluentValidation;

  namespace Anela.Heblo.Application.Features.Packaging.Validators;

  public class PrepareOrderLabelRequestValidator : AbstractValidator<PrepareOrderLabelRequest>
  {
      public PrepareOrderLabelRequestValidator()
      {
          RuleFor(x => x.OrderCode).NotEmpty();
      }
  }
  ```

- [ ] **Step 2: Verify build**

  ```bash
  dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --no-restore 2>&1 | tail -5
  ```

  Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

  ```bash
  git add backend/src/Anela.Heblo.Application/Features/Packaging/Validators/PrepareOrderLabelRequestValidator.cs
  git commit -m "feat(packaging): add PrepareOrderLabelRequestValidator"
  ```

---

## Task 7: Create `PackagingController`

**Files:**
- Create: `backend/src/Anela.Heblo.API/Controllers/PackagingController.cs`

- [ ] **Step 1: Create `PackagingController.cs`**

  ```csharp
  using Anela.Heblo.Application.Features.Packaging.UseCases.PrepareOrderLabel;
  using Anela.Heblo.Application.Features.ShipmentLabels.UseCases.GetShipmentLabelPdf;
  using Anela.Heblo.Application.Shared;
  using MediatR;
  using Microsoft.AspNetCore.Authorization;
  using Microsoft.AspNetCore.Mvc;
  using System.Text.Json.Serialization;

  namespace Anela.Heblo.API.Controllers;

  [Authorize]
  [ApiController]
  [Route("api/packaging")]
  public class PackagingController : BaseApiController
  {
      private readonly IMediator _mediator;

      public PackagingController(IMediator mediator)
      {
          _mediator = mediator;
      }

      /// <summary>
      /// Ensures a printable label exists for the order:
      /// checks eligibility, returns existing labels if present (unless forceRecreate),
      /// otherwise creates a shipment and polls until labels are ready.
      /// </summary>
      [HttpPost("orders/{orderCode}/label")]
      public async Task<ActionResult<PrepareOrderLabelResponse>> PrepareLabel(
          [FromRoute] string orderCode,
          [FromBody] PrepareOrderLabelBody body,
          CancellationToken cancellationToken)
      {
          var response = await _mediator.Send(new PrepareOrderLabelRequest
          {
              OrderCode = orderCode,
              ForceRecreate = body.ForceRecreate,
          }, cancellationToken);

          return HandleResponse(response);
      }

      /// <summary>
      /// Proxies a shipment label PDF same-origin so the kiosk iframe can print it.
      /// </summary>
      [HttpGet("orders/{orderCode}/label/pdf")]
      public async Task<IActionResult> GetLabelPdf(
          [FromRoute] string orderCode,
          [FromQuery] Guid shipmentGuid,
          [FromQuery] string packageName,
          CancellationToken cancellationToken)
      {
          var response = await _mediator.Send(new GetShipmentLabelPdfRequest
          {
              OrderCode = orderCode,
              ShipmentGuid = shipmentGuid,
              PackageName = packageName,
          }, cancellationToken);

          if (!response.Success)
          {
              return response.ErrorCode == ErrorCodes.ShipmentLabelPdfNotFound
                  ? NotFound(new { errorCode = response.ErrorCode?.ToString() })
                  : StatusCode(500, new { errorCode = response.ErrorCode?.ToString() });
          }

          return File(response.PdfStream!, "application/pdf");
      }
  }

  public class PrepareOrderLabelBody
  {
      [JsonPropertyName("forceRecreate")]
      public bool ForceRecreate { get; set; }
  }
  ```

- [ ] **Step 2: Full solution build**

  ```bash
  dotnet build backend/Anela.Heblo.sln --no-restore 2>&1 | tail -5
  ```

  Expected: `Build succeeded.`

- [ ] **Step 3: Run full test suite**

  ```bash
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj 2>&1 | tail -10
  ```

  Expected: All tests pass.

- [ ] **Step 4: Commit**

  ```bash
  git add backend/src/Anela.Heblo.API/Controllers/PackagingController.cs
  git commit -m "feat(packaging): add PackagingController with PrepareLabel and GetLabelPdf endpoints"
  ```

---

## Task 8: Regenerate TypeScript client

The TypeScript client is auto-generated on `dotnet build`. After the build, verify the new methods appear.

- [ ] **Step 1: Run full build from the repository root (this regenerates the TS client)**

  ```bash
  dotnet build backend/Anela.Heblo.sln 2>&1 | tail -5
  ```

  Expected: `Build succeeded.`

- [ ] **Step 2: Verify new endpoint methods exist in generated client**

  ```bash
  grep -n "packaging" frontend/src/api/generated/api-client.ts | head -20
  ```

  Expected: Lines referencing `packaging_PrepareLabel` (or similar name) and `packaging_GetLabelPdf`.

- [ ] **Step 3: Verify `OrderNotInPackingState` is in generated ErrorCodes enum**

  ```bash
  grep "OrderNotInPackingState" frontend/src/api/generated/api-client.ts
  ```

  Expected: One matching line showing the enum member.

- [ ] **Step 4: Verify `eligibility` appears in the generated packing order type**

  ```bash
  grep -n "eligibility\|Eligibility" frontend/src/api/generated/api-client.ts | head -10
  ```

  Expected: Lines showing the `eligibility` property and `PackingEligibility` type.

---

## Task 9: Update `usePackingOrder.ts` types

**Files:**
- Modify: `frontend/src/api/hooks/usePackingOrder.ts`

- [ ] **Step 1: Update the `PackingOrder` interface**

  Replace the `statusId` and `isInPackingState` fields with an `eligibility` object. The full updated file:

  ```typescript
  import { useQuery } from '@tanstack/react-query';
  import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';

  export type Cooling = 'None' | 'L1' | 'L2';

  export interface PackingOrderItem {
    name: string;
    quantity: number;
    imageUrl: string | null;
    setName: string | null;
  }

  export interface PackingEligibility {
    isEligible: boolean;
    warningTitle: string | null;
    warningBody: string | null;
  }

  export interface PackingOrder {
    code: string;
    customerName: string;
    shippingMethodName: string;
    cooling: Cooling;
    isCooled: boolean;
    eligibility: PackingEligibility;
    customerNote: string | null;
    eshopNote: string | null;
    items: PackingOrderItem[];
  }

  /** Thrown when the scanned order code does not exist in Shoptet. */
  export class PackingOrderNotFoundError extends Error {
    constructor(code: string) {
      super(`Order not found: ${code}`);
      this.name = 'PackingOrderNotFoundError';
    }
  }

  const fetchPackingOrder = async (code: string): Promise<PackingOrder> => {
    const apiClient = getAuthenticatedApiClient(false);
    const fullUrl = `${(apiClient as any).baseUrl}/api/shoptet-orders/${encodeURIComponent(code)}/packing`;
    const response = await (apiClient as any).http.fetch(fullUrl, {
      method: 'GET',
      headers: { Accept: 'application/json' },
    });

    if (response.status === 404) {
      throw new PackingOrderNotFoundError(code);
    }
    if (!response.ok) {
      throw new Error(`Failed to load packing order: ${response.status}`);
    }
    return response.json();
  };

  /** Loads a packing order by scanned code. Disabled until a code is provided. */
  export const usePackingOrder = (code: string | null) =>
    useQuery({
      queryKey: [...QUERY_KEYS.packingOrder, code],
      queryFn: () => fetchPackingOrder(code as string),
      enabled: !!code,
      retry: false,
      gcTime: 0,
    });
  ```

- [ ] **Step 2: Verify TypeScript compiles (will fail until components are also updated)**

  ```bash
  cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/quebec/frontend && npx tsc --noEmit 2>&1 | grep "usePackingOrder\|statusId\|isInPackingState" | head -20
  ```

  Expected: Errors about `statusId`/`isInPackingState` in component files — that is correct at this stage.

- [ ] **Step 3: Commit**

  ```bash
  git add frontend/src/api/hooks/usePackingOrder.ts
  git commit -m "feat(packaging): update PackingOrder type — replace statusId/isInPackingState with eligibility"
  ```

---

## Task 10: Update `PackingStateWarning.tsx`

**Files:**
- Modify: `frontend/src/components/baleni/PackingStateWarning.tsx`

- [ ] **Step 1: Replace hardcoded Czech strings with BE-provided warning fields**

  ```tsx
  import { AlertTriangle } from 'lucide-react';
  import type { PackingOrder } from '../../api/hooks/usePackingOrder';

  interface PackingStateWarningProps {
    order: PackingOrder;
  }

  function PackingStateWarning({ order }: PackingStateWarningProps) {
    if (order.eligibility.isEligible) {
      return null;
    }

    return (
      <div
        data-testid="packing-state-warning"
        role="alert"
        className="flex items-center gap-4 rounded-xl border-2 border-red-500 bg-red-50 px-5 py-4"
      >
        <AlertTriangle className="h-12 w-12 shrink-0 text-red-600" strokeWidth={2.5} />
        <div>
          {order.eligibility.warningTitle && (
            <p className="text-xl font-bold text-red-700">{order.eligibility.warningTitle}</p>
          )}
          {order.eligibility.warningBody && (
            <p className="text-sm text-red-600">{order.eligibility.warningBody}</p>
          )}
        </div>
      </div>
    );
  }

  export default PackingStateWarning;
  ```

- [ ] **Step 2: Commit**

  ```bash
  git add frontend/src/components/baleni/PackingStateWarning.tsx
  git commit -m "feat(packaging): render BE-provided eligibility warning strings in PackingStateWarning"
  ```

---

## Task 11: Update `BaleniPacking.tsx`

**Files:**
- Modify: `frontend/src/components/baleni/BaleniPacking.tsx`

- [ ] **Step 1: Change the two references from `isInPackingState` to `eligibility.isEligible`**

  Find line in `BaleniPacking.tsx`:
  ```tsx
  {data && data.isInPackingState && (
    <PackingShipmentCreator orderCode={data.code} />
  )}
  ```

  Replace with:
  ```tsx
  {data && data.eligibility.isEligible && (
    <PackingShipmentCreator orderCode={data.code} />
  )}
  ```

  No other changes needed — `PackingStateWarning` already receives the full `order` object.

- [ ] **Step 2: Commit**

  ```bash
  git add frontend/src/components/baleni/BaleniPacking.tsx
  git commit -m "feat(packaging): gate PackingShipmentCreator on eligibility.isEligible"
  ```

---

## Task 12: Create `usePrepareOrderLabel.ts`

**Files:**
- Create: `frontend/src/api/hooks/usePrepareOrderLabel.ts`

- [ ] **Step 1: Create the hook**

  ```typescript
  import { useMutation } from '@tanstack/react-query';
  import { getAuthenticatedApiClient } from '../client';
  import { ErrorCodes, ShipmentLabelDto } from '../generated/api-client';

  interface ApiClientWithInternals {
    baseUrl: string;
    http: { fetch(url: RequestInfo, init?: RequestInit): Promise<Response> };
  }

  export interface PrepareOrderLabelInput {
    orderCode: string;
    forceRecreate: boolean;
  }

  export interface PrepareOrderLabelResult {
    existingShipmentFound: boolean;
    labelReady: boolean;
    labels: ShipmentLabelDto[];
  }

  const MESSAGES: Partial<Record<string, string>> = {
    [ErrorCodes.OrderNotInPackingState]: 'Objednávka není ve stavu Balí se — zásilku nelze vytvořit',
    [ErrorCodes.ShipmentCarrierNotResolved]: 'Dopravce se nepodařilo určit pro tuto objednávku',
    [ErrorCodes.ShipmentCreationFailed]: 'Shoptet nemohl vytvořit zásilku — zkuste znovu',
    [ErrorCodes.ShipmentOrderWeightUnavailable]: 'Nelze zjistit hmotnost objednávky',
  };

  const GENERIC_ERROR = 'Zásilku se nepodařilo vytvořit';

  const prepareOrderLabel = async (input: PrepareOrderLabelInput): Promise<PrepareOrderLabelResult> => {
    const apiClient = getAuthenticatedApiClient(false) as unknown as ApiClientWithInternals;
    const response = await apiClient.http.fetch(
      `${apiClient.baseUrl}/api/packaging/orders/${encodeURIComponent(input.orderCode)}/label`,
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ forceRecreate: input.forceRecreate }),
      }
    );

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const data = (await response.json()) as any;

    if (!data.success) {
      const message = (data.errorCode && MESSAGES[data.errorCode as string]) ?? GENERIC_ERROR;
      throw new Error(message);
    }

    return {
      existingShipmentFound: (data.existingShipmentFound as boolean) ?? false,
      labelReady: (data.labelReady as boolean) ?? false,
      labels: (data.labels as ShipmentLabelDto[]) ?? [],
    };
  };

  export const usePrepareOrderLabel = () =>
    useMutation<PrepareOrderLabelResult, Error, PrepareOrderLabelInput>({
      mutationFn: prepareOrderLabel,
    });
  ```

- [ ] **Step 2: Commit**

  ```bash
  git add frontend/src/api/hooks/usePrepareOrderLabel.ts
  git commit -m "feat(packaging): add usePrepareOrderLabel hook"
  ```

---

## Task 13: Simplify `PackingShipmentCreator.tsx`

**Files:**
- Modify: `frontend/src/components/baleni/PackingShipmentCreator.tsx`

The state machine collapses: single hook, labels passed as props to `PackingLabelPrinter`.

- [ ] **Step 1: Rewrite `PackingShipmentCreator.tsx`**

  ```tsx
  import { useState } from 'react';
  import { Loader2 } from 'lucide-react';
  import { usePrepareOrderLabel } from '../../api/hooks/usePrepareOrderLabel';
  import PackingLabelPrinter from './PackingLabelPrinter';

  interface PackingShipmentCreatorProps {
    orderCode: string;
  }

  function PackingShipmentCreator({ orderCode }: PackingShipmentCreatorProps) {
    const mutation = usePrepareOrderLabel();
    const [useExisting, setUseExisting] = useState(false);

    const handleCreate = (forceRecreate: boolean) => {
      setUseExisting(false);
      mutation.mutate({ orderCode, forceRecreate });
    };

    if (mutation.isPending) {
      return (
        <div data-testid="shipment-creating-spinner" className="flex items-center gap-2 text-neutral-gray">
          <Loader2 className="h-5 w-5 animate-spin" />
          <span>Vytvářím zásilku…</span>
        </div>
      );
    }

    if (mutation.isError) {
      return (
        <div
          data-testid="shipment-error-banner"
          className="rounded border border-red-300 bg-red-50 px-4 py-2 text-sm text-red-700"
        >
          {mutation.error?.message ?? 'Zásilku se nepodařilo vytvořit'}
          <button
            className="ml-4 underline"
            onClick={() => mutation.reset()}
          >
            Zpět
          </button>
        </div>
      );
    }

    const result = mutation.data;

    if (result?.existingShipmentFound) {
      if (useExisting) {
        return <PackingLabelPrinter orderCode={orderCode} labels={result.labels} />;
      }
      return (
        <div className="flex flex-col gap-3">
          <p className="text-sm font-semibold text-amber-700">
            Zásilka již existuje pro tuto objednávku.
          </p>
          <div className="flex gap-3">
            <button
              className="rounded-lg border border-neutral-300 bg-white px-5 py-3 text-sm font-medium shadow"
              onClick={() => setUseExisting(true)}
            >
              Použít existující
            </button>
            <button
              className="rounded-lg bg-brand-600 px-5 py-3 text-sm font-semibold text-white shadow active:scale-95"
              onClick={() => handleCreate(true)}
            >
              Vytvořit novou
            </button>
          </div>
        </div>
      );
    }

    if (result?.labelReady && result.labels.length > 0) {
      return <PackingLabelPrinter orderCode={orderCode} labels={result.labels} />;
    }

    if (result && !result.labelReady) {
      return (
        <button
          className="rounded-lg border border-neutral-300 bg-white px-5 py-3 text-sm font-medium shadow"
          onClick={() => handleCreate(false)}
        >
          Zkusit znovu
        </button>
      );
    }

    return (
      <button
        className="rounded-lg bg-brand-600 px-6 py-4 text-lg font-semibold text-white shadow active:scale-95"
        onClick={() => handleCreate(false)}
      >
        Vytvořit zásilku
      </button>
    );
  }

  export default PackingShipmentCreator;
  ```

- [ ] **Step 2: Commit**

  ```bash
  git add frontend/src/components/baleni/PackingShipmentCreator.tsx
  git commit -m "feat(packaging): simplify PackingShipmentCreator to single usePrepareOrderLabel hook"
  ```

---

## Task 14: Update `PackingLabelPrinter.tsx`

**Files:**
- Modify: `frontend/src/components/baleni/PackingLabelPrinter.tsx`

`PackingLabelPrinter` now receives labels as a prop; it no longer fetches them.

- [ ] **Step 1: Rewrite `PackingLabelPrinter.tsx`**

  ```tsx
  import { useEffect, useState } from 'react';
  import { printLabelPdf } from './printLabelPdf';
  import type { ShipmentLabelDto } from '../../api/generated/api-client';

  interface PackingLabelPrinterProps {
    orderCode: string;
    labels: ShipmentLabelDto[];
  }

  function PackingLabelPrinter({ orderCode, labels }: PackingLabelPrinterProps) {
    const [printedCount, setPrintedCount] = useState(0);

    useEffect(() => {
      setPrintedCount(0);
    }, [orderCode]);

    useEffect(() => {
      if (labels.length > 0 && printedCount === 0) {
        printLabelPdf(orderCode, labels[0]);
        setPrintedCount(1);
      }
    }, [labels, orderCode, printedCount]);

    if (labels.length === 0 || printedCount === 0 || printedCount >= labels.length) {
      return null;
    }

    const total = labels.length;

    return (
      <button
        data-testid="print-next-label-button"
        className="rounded-lg bg-brand-600 px-6 py-4 text-lg font-semibold text-white shadow active:scale-95"
        onClick={() => {
          printLabelPdf(orderCode, labels[printedCount]);
          setPrintedCount((c) => c + 1);
        }}
      >
        {`Vytisknout štítek ${printedCount + 1}/${total}`}
      </button>
    );
  }

  export default PackingLabelPrinter;
  ```

- [ ] **Step 2: Commit**

  ```bash
  git add frontend/src/components/baleni/PackingLabelPrinter.tsx
  git commit -m "feat(packaging): PackingLabelPrinter accepts labels prop, removes useShipmentLabels dependency"
  ```

---

## Task 15: Update `printLabelPdf.ts`

**Files:**
- Modify: `frontend/src/components/baleni/printLabelPdf.ts`

- [ ] **Step 1: Change the URL to the new packaging route**

  Replace the `url` construction. Only the URL string changes — everything else (blob, iframe, print) stays identical:

  ```typescript
  import { ShipmentLabelDto } from '../../api/generated/api-client';
  import { getAuthenticatedApiClient } from '../../api/client';

  export const printLabelPdf = (orderCode: string, label: ShipmentLabelDto): void => {
    if (!label.shipmentGuid || !label.packageName) {
      throw new Error('Invalid label: missing shipmentGuid or packageName');
    }

    const apiClient = getAuthenticatedApiClient(false);
    const baseUrl = (apiClient as any).baseUrl as string;
    const url =
      `${baseUrl}/api/packaging/orders/${encodeURIComponent(orderCode)}/label/pdf` +
      `?shipmentGuid=${encodeURIComponent(label.shipmentGuid)}` +
      `&packageName=${encodeURIComponent(label.packageName)}`;

    void fetch(url)
      .then(res => {
        if (!res.ok) throw new Error(`Label PDF unavailable: ${res.status}`);
        return res.blob();
      })
      .then(blob => {
        const blobUrl = URL.createObjectURL(blob);
        const iframe = document.createElement('iframe');
        iframe.style.display = 'none';
        iframe.src = blobUrl;
        iframe.onload = () => {
          iframe.contentWindow?.print();
          document.body.removeChild(iframe);
          URL.revokeObjectURL(blobUrl);
        };
        document.body.appendChild(iframe);
      })
      .catch(() => {
        // silently ignore — the print simply won't fire if the PDF is unavailable
      });
  };
  ```

- [ ] **Step 2: Commit**

  ```bash
  git add frontend/src/components/baleni/printLabelPdf.ts
  git commit -m "feat(packaging): point printLabelPdf at new /api/packaging route"
  ```

---

## Task 16: Delete old hooks and clean up `QUERY_KEYS`

**Files:**
- Delete: `frontend/src/api/hooks/useCreateShipment.ts`
- Delete: `frontend/src/api/hooks/useShipmentLabels.ts`
- Modify: `frontend/src/api/client.ts`

- [ ] **Step 1: Delete old hooks**

  ```bash
  rm frontend/src/api/hooks/useCreateShipment.ts
  rm frontend/src/api/hooks/useShipmentLabels.ts
  ```

- [ ] **Step 2: Remove `shipmentLabels` from `QUERY_KEYS` in `client.ts`**

  Find this line in `frontend/src/api/client.ts`:
  ```typescript
    shipmentLabels: ["shipmentLabels"] as const,
  ```

  Delete it (the trailing comma on the line above, if any, should also be adjusted to keep valid syntax).

- [ ] **Step 3: Search for any remaining references to deleted hooks**

  ```bash
  grep -r "useCreateShipment\|useShipmentLabels\|shipmentLabels" frontend/src --include="*.ts" --include="*.tsx" -l
  ```

  Expected: No output. If any files appear, open them and remove the stale import/usage.

- [ ] **Step 4: Commit**

  ```bash
  git add -u
  git commit -m "feat(packaging): delete useCreateShipment and useShipmentLabels hooks"
  ```

---

## Task 17: Frontend build validation

- [ ] **Step 1: Run TypeScript type check**

  ```bash
  cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/quebec/frontend && npx tsc --noEmit 2>&1
  ```

  Expected: No output (no errors).

- [ ] **Step 2: Run lint**

  ```bash
  npm run lint 2>&1 | tail -10
  ```

  Expected: No errors or warnings.

- [ ] **Step 3: Run production build**

  ```bash
  npm run build 2>&1 | tail -10
  ```

  Expected: `✓ built in ...` (Vite success message).

- [ ] **Step 4: Run full dotnet test suite one final time**

  ```bash
  cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/quebec && dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj 2>&1 | tail -10
  ```

  Expected: All tests pass.

- [ ] **Step 5: Run dotnet format**

  ```bash
  dotnet format backend/Anela.Heblo.sln --verify-no-changes 2>&1 | tail -5
  ```

  If it reports formatting issues, run `dotnet format backend/Anela.Heblo.sln` and commit.

---

## Self-Review Checklist

- [x] **ErrorCodes.OrderNotInPackingState** added (HTTP 409) — Task 1
- [x] **PackingEligibility** class created — Task 2
- [x] **GetPackingOrderResponse** drops `StatusId`/`IsInPackingState`, gains `Eligibility` — Task 2
- [x] **GetPackingOrderHandler** emits Czech warning strings server-side — Task 2
- [x] **PrepareOrderLabelRequest/Response** created as classes (not records) — Task 3
- [x] **PrepareOrderLabelHandler** covers all 7 specified test branches — Task 4/5
- [x] **PrepareOrderLabelRequestValidator** validates `OrderCode` not empty — Task 6
- [x] **PackagingController** exposes `POST .../label` and `GET .../label/pdf` — Task 7
- [x] **Old ShipmentLabels routes kept as aliases** — controller untouched
- [x] **usePackingOrder.ts** drops `statusId`/`isInPackingState`, adds `eligibility` — Task 9
- [x] **PackingStateWarning** renders BE warning strings, no hardcoded Czech text — Task 10
- [x] **BaleniPacking** gates on `eligibility.isEligible` — Task 11
- [x] **usePrepareOrderLabel** consolidates error messages from both old hooks — Task 12
- [x] **PackingShipmentCreator** collapsed to single hook, labels passed as prop — Task 13
- [x] **PackingLabelPrinter** accepts `labels` prop, no second fetch — Task 14
- [x] **printLabelPdf** points at `/api/packaging/orders/{orderCode}/label/pdf` — Task 15
- [x] **useCreateShipment + useShipmentLabels deleted**, `QUERY_KEYS.shipmentLabels` removed — Task 16
- [x] **API hooks use absolute URLs** (`${apiClient.baseUrl}${relativeUrl}`) — confirmed in Task 12/15
- [x] **DTOs are classes, not records** — confirmed Tasks 2/3
