# Baleni Packing — Shipping Label Print Integration

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire the existing `POST /api/shipment-labels` and `BaleniPacking` kiosk together so scanning an order in packing state auto-prints its shipping label PDF — first label automatically, each additional gated behind a confirmation tap.

**Architecture:** A new backend `GET /api/shipment-labels/pdf` endpoint acts as a same-origin PDF proxy (required because browsers block `iframe.print()` on cross-origin documents). The frontend hook `useShipmentLabels` fetches label metadata via the existing POST endpoint; `PackingLabelPrinter` drives the auto-print and per-label confirmation flow; `printLabelPdf` loads the PDF into a hidden iframe and triggers the browser print dialog.

**Tech Stack:** .NET 8 / MediatR / xUnit / FluentAssertions / Moq (backend); React 18 / TanStack Query / Jest / React Testing Library / Playwright (frontend).

---

## File Map

| File | Action | Responsibility |
|------|--------|----------------|
| `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` | Modify | Add `ShipmentLabelPdfNotFound = 2904` |
| `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/UseCases/GetShipmentLabelPdf/GetShipmentLabelPdfRequest.cs` | Create | MediatR request (orderCode, shipmentGuid, packageName) |
| `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/UseCases/GetShipmentLabelPdf/GetShipmentLabelPdfResponse.cs` | Create | Response with `Stream? PdfStream` |
| `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/UseCases/GetShipmentLabelPdf/GetShipmentLabelPdfHandler.cs` | Create | Resolves labelUrl server-side, downloads PDF via HttpClient |
| `backend/src/Anela.Heblo.API/Controllers/ShipmentLabelsController.cs` | Modify | Add `GET /api/shipment-labels/pdf` endpoint |
| `backend/test/Anela.Heblo.Tests/Application/ShipmentLabels/GetShipmentLabelPdfHandlerTests.cs` | Create | Unit tests for the handler |
| `frontend/src/api/client.ts` | Modify | Add `shipmentLabels: ["shipmentLabels"]` to `QUERY_KEYS` |
| `frontend/src/api/hooks/useShipmentLabels.ts` | Create | Calls `shipmentLabels_GetLabels`; maps error codes to Czech messages |
| `frontend/src/api/hooks/__tests__/useShipmentLabels.test.ts` | Create | Unit tests for the hook |
| `frontend/src/components/baleni/printLabelPdf.ts` | Create | Builds same-origin URL, hidden iframe, triggers print |
| `frontend/src/components/baleni/__tests__/printLabelPdf.test.ts` | Create | Unit tests for the print utility |
| `frontend/src/components/baleni/PackingLabelPrinter.tsx` | Create | Auto-prints first label; confirmation button for each subsequent |
| `frontend/src/components/baleni/__tests__/PackingLabelPrinter.test.tsx` | Create | Component tests |
| `frontend/src/components/baleni/BaleniPacking.tsx` | Modify | Mount `<PackingLabelPrinter>` when order loaded and in packing state |
| `frontend/src/components/baleni/__tests__/BaleniPacking.test.tsx` | Modify | Add test that `PackingLabelPrinter` mounts when `isInPackingState` |
| `frontend/test/e2e/baleni/packing.spec.ts` | Modify | Assert confirmation button appears for multi-package order |

---

## Task 1: Add `ShipmentLabelPdfNotFound` error code

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`

- [ ] **Step 1: Add the new error code**

  Open `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`. Find the ShipmentLabels block (around line 313) and add one value:

  ```csharp
  // ShipmentLabels module errors (29XX)
  [HttpStatusCode(HttpStatusCode.NotFound)]
  ShipmentLabelsNoShipmentFound = 2902,
  [HttpStatusCode(HttpStatusCode.UnprocessableEntity)]
  ShipmentLabelsNotGenerated = 2903,
  [HttpStatusCode(HttpStatusCode.NotFound)]
  ShipmentLabelPdfNotFound = 2904,
  ```

- [ ] **Step 2: Verify the solution still builds**

  ```bash
  cd backend && dotnet build Anela.Heblo.sln --configuration Release -v quiet
  ```

  Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

  ```bash
  git add backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs
  git commit -m "feat(shipment-labels): add ShipmentLabelPdfNotFound error code 2904"
  ```

---

## Task 2: Write failing backend handler tests

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Application/ShipmentLabels/GetShipmentLabelPdfHandlerTests.cs`

- [ ] **Step 1: Create the test file**

  ```csharp
  using System.Net;
  using System.Net.Http.Headers;
  using Anela.Heblo.Application.Features.ShipmentLabels;
  using Anela.Heblo.Application.Features.ShipmentLabels.UseCases.GetShipmentLabelPdf;
  using Anela.Heblo.Application.Shared;
  using FluentAssertions;
  using Microsoft.Extensions.Logging.Abstractions;
  using Moq;

  namespace Anela.Heblo.Tests.Application.ShipmentLabels;

  public class GetShipmentLabelPdfHandlerTests
  {
      private readonly Mock<IShipmentClient> _clientMock = new();
      private readonly Mock<IHttpClientFactory> _httpFactoryMock = new();

      private static HttpClient BuildHttpClient(HttpResponseMessage response)
      {
          var handler = new FakeHttpMessageHandler(response);
          return new HttpClient(handler);
      }

      private GetShipmentLabelPdfHandler CreateHandler()
      {
          _httpFactoryMock
              .Setup(f => f.CreateClient(It.IsAny<string>()))
              .Returns(BuildHttpClient(new HttpResponseMessage(HttpStatusCode.OK)
              {
                  Content = new ByteArrayContent(new byte[] { 1, 2, 3 })
                  {
                      Headers = { ContentType = new MediaTypeHeaderValue("application/pdf") }
                  }
              }));

          return new GetShipmentLabelPdfHandler(
              _clientMock.Object,
              _httpFactoryMock.Object,
              NullLogger<GetShipmentLabelPdfHandler>.Instance);
      }

      [Fact]
      public async Task Handle_ValidPackageWithLabelUrl_ReturnsPdfStream()
      {
          // Arrange
          var guid = Guid.NewGuid();
          _clientMock
              .Setup(c => c.GetLabelsByOrderCodeAsync("0001234", It.IsAny<CancellationToken>()))
              .ReturnsAsync([new ShipmentLabel
              {
                  ShipmentGuid = guid,
                  OrderCode = "0001234",
                  PackageName = "Zásilka 1",
                  LabelUrl = "https://carrier.example.com/label.pdf",
              }]);

          var pdfBytes = new byte[] { 1, 2, 3 };
          var fakePdfResponse = new HttpResponseMessage(HttpStatusCode.OK)
          {
              Content = new ByteArrayContent(pdfBytes)
              {
                  Headers = { ContentType = new MediaTypeHeaderValue("application/pdf") }
              }
          };
          _httpFactoryMock
              .Setup(f => f.CreateClient(It.IsAny<string>()))
              .Returns(BuildHttpClient(fakePdfResponse));

          var handler = new GetShipmentLabelPdfHandler(
              _clientMock.Object,
              _httpFactoryMock.Object,
              NullLogger<GetShipmentLabelPdfHandler>.Instance);

          // Act
          var response = await handler.Handle(
              new GetShipmentLabelPdfRequest
              {
                  OrderCode = "0001234",
                  ShipmentGuid = guid,
                  PackageName = "Zásilka 1",
              },
              CancellationToken.None);

          // Assert
          response.Success.Should().BeTrue();
          response.PdfStream.Should().NotBeNull();
          var bytes = new byte[3];
          _ = await response.PdfStream!.ReadAsync(bytes);
          bytes.Should().Equal(pdfBytes);
      }

      [Fact]
      public async Task Handle_OrderHasNoShipments_ReturnsNotFound()
      {
          // Arrange
          _clientMock
              .Setup(c => c.GetLabelsByOrderCodeAsync("0001111", It.IsAny<CancellationToken>()))
              .ReturnsAsync([]);

          // Act
          var response = await CreateHandler().Handle(
              new GetShipmentLabelPdfRequest
              {
                  OrderCode = "0001111",
                  ShipmentGuid = Guid.NewGuid(),
                  PackageName = "Zásilka 1",
              },
              CancellationToken.None);

          // Assert
          response.Success.Should().BeFalse();
          response.ErrorCode.Should().Be(ErrorCodes.ShipmentLabelPdfNotFound);
      }

      [Fact]
      public async Task Handle_PackageNameNotFound_ReturnsNotFound()
      {
          // Arrange
          var guid = Guid.NewGuid();
          _clientMock
              .Setup(c => c.GetLabelsByOrderCodeAsync("0002222", It.IsAny<CancellationToken>()))
              .ReturnsAsync([new ShipmentLabel
              {
                  ShipmentGuid = guid,
                  OrderCode = "0002222",
                  PackageName = "Zásilka 1",
                  LabelUrl = "https://carrier.example.com/label.pdf",
              }]);

          // Act — request with a different PackageName that doesn't exist
          var response = await CreateHandler().Handle(
              new GetShipmentLabelPdfRequest
              {
                  OrderCode = "0002222",
                  ShipmentGuid = guid,
                  PackageName = "Zásilka 99",
              },
              CancellationToken.None);

          // Assert
          response.Success.Should().BeFalse();
          response.ErrorCode.Should().Be(ErrorCodes.ShipmentLabelPdfNotFound);
      }

      [Fact]
      public async Task Handle_PackageHasNoLabelUrl_ReturnsNotFound()
      {
          // Arrange
          var guid = Guid.NewGuid();
          _clientMock
              .Setup(c => c.GetLabelsByOrderCodeAsync("0003333", It.IsAny<CancellationToken>()))
              .ReturnsAsync([new ShipmentLabel
              {
                  ShipmentGuid = guid,
                  OrderCode = "0003333",
                  PackageName = "Zásilka 1",
                  LabelUrl = null, // ZPL-only, no PDF
              }]);

          // Act
          var response = await CreateHandler().Handle(
              new GetShipmentLabelPdfRequest
              {
                  OrderCode = "0003333",
                  ShipmentGuid = guid,
                  PackageName = "Zásilka 1",
              },
              CancellationToken.None);

          // Assert
          response.Success.Should().BeFalse();
          response.ErrorCode.Should().Be(ErrorCodes.ShipmentLabelPdfNotFound);
      }

      [Fact]
      public async Task Handle_ShipmentClientThrows_ReturnsInternalServerError()
      {
          // Arrange
          _clientMock
              .Setup(c => c.GetLabelsByOrderCodeAsync("0004444", It.IsAny<CancellationToken>()))
              .ThrowsAsync(new HttpRequestException("Shoptet unavailable"));

          // Act
          var response = await CreateHandler().Handle(
              new GetShipmentLabelPdfRequest
              {
                  OrderCode = "0004444",
                  ShipmentGuid = Guid.NewGuid(),
                  PackageName = "Zásilka 1",
              },
              CancellationToken.None);

          // Assert
          response.Success.Should().BeFalse();
          response.ErrorCode.Should().Be(ErrorCodes.InternalServerError);
      }

      [Fact]
      public async Task Handle_PdfDownloadFails_ReturnsInternalServerError()
      {
          // Arrange
          var guid = Guid.NewGuid();
          _clientMock
              .Setup(c => c.GetLabelsByOrderCodeAsync("0005555", It.IsAny<CancellationToken>()))
              .ReturnsAsync([new ShipmentLabel
              {
                  ShipmentGuid = guid,
                  OrderCode = "0005555",
                  PackageName = "Zásilka 1",
                  LabelUrl = "https://carrier.example.com/label.pdf",
              }]);

          var badResponse = new HttpResponseMessage(HttpStatusCode.InternalServerError);
          _httpFactoryMock
              .Setup(f => f.CreateClient(It.IsAny<string>()))
              .Returns(BuildHttpClient(badResponse));

          var handler = new GetShipmentLabelPdfHandler(
              _clientMock.Object,
              _httpFactoryMock.Object,
              NullLogger<GetShipmentLabelPdfHandler>.Instance);

          // Act
          var response = await handler.Handle(
              new GetShipmentLabelPdfRequest
              {
                  OrderCode = "0005555",
                  ShipmentGuid = guid,
                  PackageName = "Zásilka 1",
              },
              CancellationToken.None);

          // Assert
          response.Success.Should().BeFalse();
          response.ErrorCode.Should().Be(ErrorCodes.InternalServerError);
      }

      private sealed class FakeHttpMessageHandler : HttpMessageHandler
      {
          private readonly HttpResponseMessage _response;
          public FakeHttpMessageHandler(HttpResponseMessage response) => _response = response;
          protected override Task<HttpResponseMessage> SendAsync(
              HttpRequestMessage request, CancellationToken cancellationToken) =>
              Task.FromResult(_response);
      }
  }
  ```

- [ ] **Step 2: Run tests to confirm they all fail (files not yet created)**

  ```bash
  cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "GetShipmentLabelPdfHandlerTests" -v minimal
  ```

  Expected: Build error — `GetShipmentLabelPdfRequest`, `GetShipmentLabelPdfResponse`, `GetShipmentLabelPdfHandler` do not exist yet.

---

## Task 3: Implement the `GetShipmentLabelPdf` use case

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/UseCases/GetShipmentLabelPdf/GetShipmentLabelPdfRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/UseCases/GetShipmentLabelPdf/GetShipmentLabelPdfResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/UseCases/GetShipmentLabelPdf/GetShipmentLabelPdfHandler.cs`

- [ ] **Step 1: Create the request**

  ```csharp
  // backend/src/Anela.Heblo.Application/Features/ShipmentLabels/UseCases/GetShipmentLabelPdf/GetShipmentLabelPdfRequest.cs
  using MediatR;

  namespace Anela.Heblo.Application.Features.ShipmentLabels.UseCases.GetShipmentLabelPdf;

  public class GetShipmentLabelPdfRequest : IRequest<GetShipmentLabelPdfResponse>
  {
      public string OrderCode { get; set; } = null!;
      public Guid ShipmentGuid { get; set; }
      public string PackageName { get; set; } = null!;
  }
  ```

- [ ] **Step 2: Create the response**

  ```csharp
  // backend/src/Anela.Heblo.Application/Features/ShipmentLabels/UseCases/GetShipmentLabelPdf/GetShipmentLabelPdfResponse.cs
  using Anela.Heblo.Application.Shared;

  namespace Anela.Heblo.Application.Features.ShipmentLabels.UseCases.GetShipmentLabelPdf;

  public class GetShipmentLabelPdfResponse : BaseResponse
  {
      public Stream? PdfStream { get; set; }

      public GetShipmentLabelPdfResponse(Stream pdfStream)
      {
          PdfStream = pdfStream;
      }

      public GetShipmentLabelPdfResponse(ErrorCodes errorCode)
          : base(errorCode)
      {
      }
  }
  ```

- [ ] **Step 3: Create the handler**

  ```csharp
  // backend/src/Anela.Heblo.Application/Features/ShipmentLabels/UseCases/GetShipmentLabelPdf/GetShipmentLabelPdfHandler.cs
  using Anela.Heblo.Application.Shared;
  using MediatR;
  using Microsoft.Extensions.Logging;

  namespace Anela.Heblo.Application.Features.ShipmentLabels.UseCases.GetShipmentLabelPdf;

  public class GetShipmentLabelPdfHandler
      : IRequestHandler<GetShipmentLabelPdfRequest, GetShipmentLabelPdfResponse>
  {
      private readonly IShipmentClient _shipmentClient;
      private readonly IHttpClientFactory _httpClientFactory;
      private readonly ILogger<GetShipmentLabelPdfHandler> _logger;

      public GetShipmentLabelPdfHandler(
          IShipmentClient shipmentClient,
          IHttpClientFactory httpClientFactory,
          ILogger<GetShipmentLabelPdfHandler> logger)
      {
          _shipmentClient = shipmentClient;
          _httpClientFactory = httpClientFactory;
          _logger = logger;
      }

      public async Task<GetShipmentLabelPdfResponse> Handle(
          GetShipmentLabelPdfRequest request,
          CancellationToken cancellationToken)
      {
          try
          {
              var labels = await _shipmentClient.GetLabelsByOrderCodeAsync(
                  request.OrderCode, cancellationToken);

              var package = labels.FirstOrDefault(l =>
                  l.ShipmentGuid == request.ShipmentGuid &&
                  l.PackageName == request.PackageName);

              if (package is null || package.LabelUrl is null)
              {
                  return new GetShipmentLabelPdfResponse(ErrorCodes.ShipmentLabelPdfNotFound);
              }

              var httpClient = _httpClientFactory.CreateClient();
              var pdfResponse = await httpClient.GetAsync(package.LabelUrl, cancellationToken);

              if (!pdfResponse.IsSuccessStatusCode)
              {
                  _logger.LogError(
                      "PDF download failed for order {OrderCode} package {PackageName}: HTTP {StatusCode}",
                      request.OrderCode, request.PackageName, (int)pdfResponse.StatusCode);
                  return new GetShipmentLabelPdfResponse(ErrorCodes.InternalServerError);
              }

              var ms = new MemoryStream();
              await pdfResponse.Content.CopyToAsync(ms, cancellationToken);
              ms.Position = 0;
              return new GetShipmentLabelPdfResponse(ms);
          }
          catch (Exception ex)
          {
              _logger.LogError(ex,
                  "Failed to get label PDF for order {OrderCode} package {PackageName}",
                  request.OrderCode, request.PackageName);
              return new GetShipmentLabelPdfResponse(ErrorCodes.InternalServerError);
          }
      }
  }
  ```

- [ ] **Step 4: Run the handler tests**

  ```bash
  cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "GetShipmentLabelPdfHandlerTests" -v minimal
  ```

  Expected: `Passed! - 6 test(s)`

- [ ] **Step 5: Run the full test suite to confirm no regressions**

  ```bash
  cd backend && dotnet test Anela.Heblo.sln -v minimal
  ```

  Expected: All tests pass.

- [ ] **Step 6: Commit**

  ```bash
  git add backend/src/Anela.Heblo.Application/Features/ShipmentLabels/UseCases/GetShipmentLabelPdf/
  git add backend/test/Anela.Heblo.Tests/Application/ShipmentLabels/GetShipmentLabelPdfHandlerTests.cs
  git commit -m "feat(shipment-labels): implement GetShipmentLabelPdf use case with unit tests"
  ```

---

## Task 4: Add the PDF proxy endpoint to the controller

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/ShipmentLabelsController.cs`

- [ ] **Step 1: Add usings and the new endpoint**

  Open `backend/src/Anela.Heblo.API/Controllers/ShipmentLabelsController.cs`. The current file is:

  ```csharp
  using Anela.Heblo.Application.Features.ShipmentLabels.UseCases.GetOrderShipmentLabels;
  using MediatR;
  using Microsoft.AspNetCore.Authorization;
  using Microsoft.AspNetCore.Mvc;
  using System.Text.Json.Serialization;
  ```

  Replace the usings block and add the new endpoint so the full file reads:

  ```csharp
  using Anela.Heblo.Application.Features.ShipmentLabels.UseCases.GetOrderShipmentLabels;
  using Anela.Heblo.Application.Features.ShipmentLabels.UseCases.GetShipmentLabelPdf;
  using Anela.Heblo.Application.Shared;
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

      /// <summary>
      /// Proxies a shipment label PDF same-origin so the kiosk iframe can print it.
      /// Resolves the carrier URL server-side — the frontend never receives a raw external URL.
      /// </summary>
      [HttpGet("pdf")]
      public async Task<IActionResult> GetLabelPdf(
          [FromQuery] string orderCode,
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

  public class GetShipmentLabelsRequest
  {
      [JsonPropertyName("orderCode")]
      public string OrderCode { get; set; } = null!;
  }
  ```

- [ ] **Step 2: Build the solution**

  ```bash
  cd backend && dotnet build Anela.Heblo.sln --configuration Release -v quiet
  ```

  Expected: `Build succeeded.`

- [ ] **Step 3: Run dotnet format**

  ```bash
  cd backend && dotnet format Anela.Heblo.sln --verify-no-changes
  ```

  Expected: No changes (or apply any suggested changes).

- [ ] **Step 4: Commit**

  ```bash
  git add backend/src/Anela.Heblo.API/Controllers/ShipmentLabelsController.cs
  git commit -m "feat(shipment-labels): add GET /api/shipment-labels/pdf proxy endpoint"
  ```

---

## Task 5: Add `shipmentLabels` query key and write the failing hook test

**Files:**
- Modify: `frontend/src/api/client.ts`
- Create: `frontend/src/api/hooks/__tests__/useShipmentLabels.test.ts`

- [ ] **Step 1: Add `shipmentLabels` to QUERY_KEYS in `frontend/src/api/client.ts`**

  Find the `QUERY_KEYS` export (around line 404). Add one entry before the closing `} as const`:

  ```typescript
  packingOrder: ["packingOrder"] as const,
  shipmentLabels: ["shipmentLabels"] as const,
  // Add more query keys as needed
  ```

- [ ] **Step 2: Create the hook test file**

  ```typescript
  // frontend/src/api/hooks/__tests__/useShipmentLabels.test.ts
  import { renderHook, waitFor } from '@testing-library/react';
  import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
  import React from 'react';
  import { useShipmentLabels } from '../useShipmentLabels';
  import * as clientModule from '../../client';
  import { ErrorCodes } from '../../generated/api-client';

  jest.mock('../../client', () => ({
    getAuthenticatedApiClient: jest.fn(),
    QUERY_KEYS: {
      shipmentLabels: ['shipmentLabels'],
    },
  }));

  const mockGetAuthenticatedApiClient =
    clientModule.getAuthenticatedApiClient as jest.MockedFunction<
      typeof clientModule.getAuthenticatedApiClient
    >;

  const mockApiClient = {
    shipmentLabels_GetLabels: jest.fn(),
  };

  const createWrapper = ({ children }: { children: React.ReactNode }) => {
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });
    return React.createElement(QueryClientProvider, { client: queryClient }, children);
  };

  beforeEach(() => {
    jest.clearAllMocks();
    mockGetAuthenticatedApiClient.mockReturnValue(mockApiClient as any);
  });

  describe('useShipmentLabels', () => {
    it('does not fetch when enabled is false', () => {
      renderHook(() => useShipmentLabels('250001', false), { wrapper: createWrapper });
      expect(mockApiClient.shipmentLabels_GetLabels).not.toHaveBeenCalled();
    });

    it('does not fetch when orderCode is null', () => {
      renderHook(() => useShipmentLabels(null, true), { wrapper: createWrapper });
      expect(mockApiClient.shipmentLabels_GetLabels).not.toHaveBeenCalled();
    });

    it('returns labels on success', async () => {
      mockApiClient.shipmentLabels_GetLabels.mockResolvedValue({
        success: true,
        labels: [
          { shipmentGuid: 'guid-1', packageName: 'Zásilka 1', labelUrl: 'https://x.com/1.pdf' },
        ],
      });

      const { result } = renderHook(() => useShipmentLabels('250001', true), {
        wrapper: createWrapper,
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toHaveLength(1);
      expect(result.current.data![0].packageName).toBe('Zásilka 1');
    });

    it('throws error with Czech message for error code 2902 (ShipmentLabelsNoShipmentFound)', async () => {
      mockApiClient.shipmentLabels_GetLabels.mockResolvedValue({
        success: false,
        errorCode: ErrorCodes.ShipmentLabelsNoShipmentFound,
        labels: [],
      });

      const { result } = renderHook(() => useShipmentLabels('250001', true), {
        wrapper: createWrapper,
      });

      await waitFor(() => expect(result.current.isError).toBe(true));
      expect((result.current.error as Error).message).toBe(
        'Štítek nelze vytisknout — zásilka zatím nebyla vytvořena'
      );
    });

    it('throws error with Czech message for error code 2903 (ShipmentLabelsNotGenerated)', async () => {
      mockApiClient.shipmentLabels_GetLabels.mockResolvedValue({
        success: false,
        errorCode: ErrorCodes.ShipmentLabelsNotGenerated,
        labels: [],
      });

      const { result } = renderHook(() => useShipmentLabels('250001', true), {
        wrapper: createWrapper,
      });

      await waitFor(() => expect(result.current.isError).toBe(true));
      expect((result.current.error as Error).message).toBe(
        'Štítky zatím nebyly vygenerovány'
      );
    });

    it('throws generic error message for unknown error codes', async () => {
      mockApiClient.shipmentLabels_GetLabels.mockResolvedValue({
        success: false,
        errorCode: 'InternalServerError',
        labels: [],
      });

      const { result } = renderHook(() => useShipmentLabels('250001', true), {
        wrapper: createWrapper,
      });

      await waitFor(() => expect(result.current.isError).toBe(true));
      expect((result.current.error as Error).message).toBe(
        'Štítek se nepodařilo načíst'
      );
    });
  });
  ```

- [ ] **Step 3: Run the test to confirm it fails (hook not yet created)**

  ```bash
  cd frontend && npx jest useShipmentLabels --no-coverage 2>&1 | tail -20
  ```

  Expected: error — cannot find module `../useShipmentLabels`.

---

## Task 6: Implement `useShipmentLabels`

**Files:**
- Create: `frontend/src/api/hooks/useShipmentLabels.ts`

- [ ] **Step 1: Create the hook**

  ```typescript
  // frontend/src/api/hooks/useShipmentLabels.ts
  import { useQuery } from '@tanstack/react-query';
  import { ErrorCodes, ShipmentLabelDto } from '../generated/api-client';
  import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';

  const MESSAGES: Record<string, string> = {
    [ErrorCodes.ShipmentLabelsNoShipmentFound]:
      'Štítek nelze vytisknout — zásilka zatím nebyla vytvořena',
    [ErrorCodes.ShipmentLabelsNotGenerated]: 'Štítky zatím nebyly vygenerovány',
  };

  const GENERIC_ERROR = 'Štítek se nepodařilo načíst';

  const fetchShipmentLabels = async (orderCode: string): Promise<ShipmentLabelDto[]> => {
    const apiClient = getAuthenticatedApiClient(false);
    const response = await apiClient.shipmentLabels_GetLabels({ orderCode });
    if (!response.success) {
      const message =
        (response.errorCode && MESSAGES[response.errorCode]) ?? GENERIC_ERROR;
      throw new Error(message);
    }
    return response.labels ?? [];
  };

  export const useShipmentLabels = (orderCode: string | null, enabled: boolean) =>
    useQuery({
      queryKey: [...QUERY_KEYS.shipmentLabels, orderCode],
      queryFn: () => fetchShipmentLabels(orderCode as string),
      enabled: enabled && !!orderCode,
      retry: false,
      gcTime: 0,
    });
  ```

- [ ] **Step 2: Run the hook tests**

  ```bash
  cd frontend && npx jest useShipmentLabels --no-coverage 2>&1 | tail -20
  ```

  Expected: `Tests: 5 passed, 5 total`

- [ ] **Step 3: Commit**

  ```bash
  git add frontend/src/api/client.ts \
          frontend/src/api/hooks/useShipmentLabels.ts \
          frontend/src/api/hooks/__tests__/useShipmentLabels.test.ts
  git commit -m "feat(baleni): add useShipmentLabels hook with error-code mapping"
  ```

---

## Task 7: Write failing `printLabelPdf` test and implement

**Files:**
- Create: `frontend/src/components/baleni/__tests__/printLabelPdf.test.ts`
- Create: `frontend/src/components/baleni/printLabelPdf.ts`

- [ ] **Step 1: Create the test**

  ```typescript
  // frontend/src/components/baleni/__tests__/printLabelPdf.test.ts
  import { printLabelPdf } from '../printLabelPdf';
  import * as clientModule from '../../../api/client';

  jest.mock('../../../api/client', () => ({
    getAuthenticatedApiClient: jest.fn(),
  }));

  const mockGetAuthenticatedApiClient =
    clientModule.getAuthenticatedApiClient as jest.MockedFunction<
      typeof clientModule.getAuthenticatedApiClient
    >;

  beforeEach(() => {
    jest.clearAllMocks();
    mockGetAuthenticatedApiClient.mockReturnValue({
      baseUrl: 'http://localhost:5001',
    } as any);
  });

  describe('printLabelPdf', () => {
    it('appends a hidden iframe with the correct same-origin PDF URL', () => {
      const appendChildSpy = jest.spyOn(document.body, 'appendChild');

      printLabelPdf('250001', {
        shipmentGuid: 'abc-guid-123',
        packageName: 'Zásilka 1',
      });

      expect(appendChildSpy).toHaveBeenCalledTimes(1);
      const iframe = appendChildSpy.mock.calls[0][0] as HTMLIFrameElement;
      expect(iframe.tagName).toBe('IFRAME');
      expect(iframe.style.display).toBe('none');
      expect(iframe.src).toContain('http://localhost:5001/api/shipment-labels/pdf');
      expect(iframe.src).toContain('orderCode=250001');
      expect(iframe.src).toContain('shipmentGuid=abc-guid-123');
      expect(iframe.src).toContain('packageName=Z%C3%A1silka%201');

      appendChildSpy.mockRestore();
    });

    it('calls contentWindow.print() when the iframe loads', () => {
      const appendChildSpy = jest.spyOn(document.body, 'appendChild');
      const removeChildSpy = jest
        .spyOn(document.body, 'removeChild')
        .mockImplementation(() => document.createElement('iframe'));

      printLabelPdf('250001', { shipmentGuid: 'abc-guid-123', packageName: 'Zásilka 1' });

      const iframe = appendChildSpy.mock.calls[0][0] as HTMLIFrameElement;

      const printMock = jest.fn();
      Object.defineProperty(iframe, 'contentWindow', {
        value: { print: printMock },
        configurable: true,
      });

      iframe.onload!(new Event('load'));

      expect(printMock).toHaveBeenCalledTimes(1);
      expect(removeChildSpy).toHaveBeenCalledWith(iframe);

      appendChildSpy.mockRestore();
      removeChildSpy.mockRestore();
    });
  });
  ```

- [ ] **Step 2: Run the test to confirm it fails**

  ```bash
  cd frontend && npx jest printLabelPdf --no-coverage 2>&1 | tail -10
  ```

  Expected: error — cannot find module `../printLabelPdf`.

- [ ] **Step 3: Implement `printLabelPdf`**

  ```typescript
  // frontend/src/components/baleni/printLabelPdf.ts
  import { getAuthenticatedApiClient } from '../../api/client';

  interface LabelIdentifier {
    shipmentGuid: string;
    packageName: string;
  }

  export const printLabelPdf = (orderCode: string, label: LabelIdentifier): void => {
    const apiClient = getAuthenticatedApiClient(false);
    const baseUrl = (apiClient as any).baseUrl as string;
    const url =
      `${baseUrl}/api/shipment-labels/pdf` +
      `?orderCode=${encodeURIComponent(orderCode)}` +
      `&shipmentGuid=${encodeURIComponent(label.shipmentGuid)}` +
      `&packageName=${encodeURIComponent(label.packageName)}`;

    const iframe = document.createElement('iframe');
    iframe.style.display = 'none';
    iframe.src = url;
    iframe.onload = () => {
      iframe.contentWindow?.print();
      document.body.removeChild(iframe);
    };
    document.body.appendChild(iframe);
  };
  ```

- [ ] **Step 4: Run the tests**

  ```bash
  cd frontend && npx jest printLabelPdf --no-coverage 2>&1 | tail -10
  ```

  Expected: `Tests: 2 passed, 2 total`

- [ ] **Step 5: Commit**

  ```bash
  git add frontend/src/components/baleni/printLabelPdf.ts \
          frontend/src/components/baleni/__tests__/printLabelPdf.test.ts
  git commit -m "feat(baleni): add printLabelPdf utility (same-origin iframe print)"
  ```

---

## Task 8: Write failing `PackingLabelPrinter` tests

**Files:**
- Create: `frontend/src/components/baleni/__tests__/PackingLabelPrinter.test.tsx`

- [ ] **Step 1: Create the test file**

  ```typescript
  // frontend/src/components/baleni/__tests__/PackingLabelPrinter.test.tsx
  import React from 'react';
  import { render, screen, fireEvent, act } from '@testing-library/react';
  import PackingLabelPrinter from '../PackingLabelPrinter';
  import * as useShipmentLabelsModule from '../../../api/hooks/useShipmentLabels';
  import * as printLabelPdfModule from '../printLabelPdf';

  jest.mock('../../../api/hooks/useShipmentLabels', () => ({
    useShipmentLabels: jest.fn(),
  }));

  jest.mock('../printLabelPdf', () => ({
    printLabelPdf: jest.fn(),
  }));

  const mockUseShipmentLabels =
    useShipmentLabelsModule.useShipmentLabels as jest.MockedFunction<
      typeof useShipmentLabelsModule.useShipmentLabels
    >;
  const mockPrintLabelPdf = printLabelPdfModule.printLabelPdf as jest.MockedFunction<
    typeof printLabelPdfModule.printLabelPdf
  >;

  const baseQueryResult = {
    data: undefined,
    isLoading: false,
    isSuccess: false,
    isError: false,
    error: null,
  } as any;

  const label1 = { shipmentGuid: 'guid-1', packageName: 'Zásilka 1', labelUrl: 'https://x.com/1.pdf' };
  const label2 = { shipmentGuid: 'guid-1', packageName: 'Zásilka 2', labelUrl: 'https://x.com/2.pdf' };
  const label3 = { shipmentGuid: 'guid-1', packageName: 'Zásilka 3', labelUrl: 'https://x.com/3.pdf' };

  beforeEach(() => {
    jest.clearAllMocks();
  });

  describe('PackingLabelPrinter', () => {
    it('renders nothing while loading', () => {
      mockUseShipmentLabels.mockReturnValue({ ...baseQueryResult, isLoading: true });
      const { container } = render(<PackingLabelPrinter orderCode="250001" />);
      expect(container).toBeEmptyDOMElement();
    });

    it('auto-prints the first label when labels load', () => {
      mockUseShipmentLabels.mockReturnValue({
        ...baseQueryResult,
        isSuccess: true,
        data: [label1],
      });

      render(<PackingLabelPrinter orderCode="250001" />);

      expect(mockPrintLabelPdf).toHaveBeenCalledTimes(1);
      expect(mockPrintLabelPdf).toHaveBeenCalledWith('250001', label1);
    });

    it('renders nothing visible after auto-printing the only label', () => {
      mockUseShipmentLabels.mockReturnValue({
        ...baseQueryResult,
        isSuccess: true,
        data: [label1],
      });

      const { container } = render(<PackingLabelPrinter orderCode="250001" />);
      expect(container).toBeEmptyDOMElement();
    });

    it('shows confirmation button for each label after the first', () => {
      mockUseShipmentLabels.mockReturnValue({
        ...baseQueryResult,
        isSuccess: true,
        data: [label1, label2, label3],
      });

      render(<PackingLabelPrinter orderCode="250001" />);

      expect(mockPrintLabelPdf).toHaveBeenCalledTimes(1);
      expect(mockPrintLabelPdf).toHaveBeenCalledWith('250001', label1);
      expect(screen.getByTestId('print-next-label-button')).toHaveTextContent(
        'Vytisknout štítek 2/3'
      );
    });

    it('prints the next label and updates the button when tapped', () => {
      mockUseShipmentLabels.mockReturnValue({
        ...baseQueryResult,
        isSuccess: true,
        data: [label1, label2, label3],
      });

      render(<PackingLabelPrinter orderCode="250001" />);

      fireEvent.click(screen.getByTestId('print-next-label-button'));

      expect(mockPrintLabelPdf).toHaveBeenCalledTimes(2);
      expect(mockPrintLabelPdf).toHaveBeenNthCalledWith(2, '250001', label2);
      expect(screen.getByTestId('print-next-label-button')).toHaveTextContent(
        'Vytisknout štítek 3/3'
      );
    });

    it('hides the button after all labels are printed', () => {
      mockUseShipmentLabels.mockReturnValue({
        ...baseQueryResult,
        isSuccess: true,
        data: [label1, label2],
      });

      render(<PackingLabelPrinter orderCode="250001" />);
      fireEvent.click(screen.getByTestId('print-next-label-button'));

      expect(screen.queryByTestId('print-next-label-button')).not.toBeInTheDocument();
    });

    it('shows an error banner when the hook reports an error', () => {
      mockUseShipmentLabels.mockReturnValue({
        ...baseQueryResult,
        isError: true,
        error: new Error('Štítky zatím nebyly vygenerovány'),
      });

      render(<PackingLabelPrinter orderCode="250001" />);

      expect(screen.getByTestId('label-print-error')).toHaveTextContent(
        'Štítky zatím nebyly vygenerovány'
      );
      expect(mockPrintLabelPdf).not.toHaveBeenCalled();
    });

    it('resets printedCount when the orderCode changes', () => {
      mockUseShipmentLabels.mockReturnValue({
        ...baseQueryResult,
        isSuccess: true,
        data: [label1, label2],
      });

      const { rerender } = render(<PackingLabelPrinter orderCode="250001" />);

      // After first render: auto-printed label1, button shows "2/2"
      expect(mockPrintLabelPdf).toHaveBeenCalledTimes(1);
      expect(screen.getByTestId('print-next-label-button')).toHaveTextContent('2/2');

      // New scan
      rerender(<PackingLabelPrinter orderCode="250002" />);

      // auto-prints the first label of the new order
      expect(mockPrintLabelPdf).toHaveBeenCalledTimes(2);
      expect(mockPrintLabelPdf).toHaveBeenLastCalledWith('250002', label1);
    });
  });
  ```

- [ ] **Step 2: Run the test to confirm it fails**

  ```bash
  cd frontend && npx jest PackingLabelPrinter --no-coverage 2>&1 | tail -10
  ```

  Expected: error — cannot find module `../PackingLabelPrinter`.

---

## Task 9: Implement `PackingLabelPrinter`

**Files:**
- Create: `frontend/src/components/baleni/PackingLabelPrinter.tsx`

- [ ] **Step 1: Create the component**

  ```tsx
  // frontend/src/components/baleni/PackingLabelPrinter.tsx
  import { useEffect, useState } from 'react';
  import { useShipmentLabels } from '../../api/hooks/useShipmentLabels';
  import { printLabelPdf } from './printLabelPdf';

  interface PackingLabelPrinterProps {
    orderCode: string;
  }

  function PackingLabelPrinter({ orderCode }: PackingLabelPrinterProps) {
    const { data: labels, isError, error } = useShipmentLabels(orderCode, true);
    const [printedCount, setPrintedCount] = useState(0);

    useEffect(() => {
      setPrintedCount(0);
    }, [orderCode]);

    useEffect(() => {
      if (labels && labels.length > 0 && printedCount === 0) {
        printLabelPdf(orderCode, {
          shipmentGuid: labels[0].shipmentGuid ?? '',
          packageName: labels[0].packageName ?? '',
        });
        setPrintedCount(1);
      }
    }, [labels, orderCode, printedCount]);

    if (isError && error) {
      return (
        <div
          data-testid="label-print-error"
          className="rounded border border-red-300 bg-red-50 px-4 py-2 text-sm text-red-700"
        >
          {(error as Error).message}
        </div>
      );
    }

    if (!labels || printedCount === 0 || printedCount >= labels.length) {
      return null;
    }

    const total = labels.length;

    return (
      <button
        data-testid="print-next-label-button"
        className="rounded-lg bg-brand-600 px-6 py-4 text-lg font-semibold text-white shadow active:scale-95"
        onClick={() => {
          printLabelPdf(orderCode, {
            shipmentGuid: labels[printedCount].shipmentGuid ?? '',
            packageName: labels[printedCount].packageName ?? '',
          });
          setPrintedCount((c) => c + 1);
        }}
      >
        {`Vytisknout štítek ${printedCount + 1}/${total}`}
      </button>
    );
  }

  export default PackingLabelPrinter;
  ```

- [ ] **Step 2: Run the PackingLabelPrinter tests**

  ```bash
  cd frontend && npx jest PackingLabelPrinter --no-coverage 2>&1 | tail -15
  ```

  Expected: `Tests: 8 passed, 8 total`

- [ ] **Step 3: Commit**

  ```bash
  git add frontend/src/components/baleni/PackingLabelPrinter.tsx \
          frontend/src/components/baleni/__tests__/PackingLabelPrinter.test.tsx
  git commit -m "feat(baleni): implement PackingLabelPrinter component"
  ```

---

## Task 10: Wire `PackingLabelPrinter` into `BaleniPacking`

**Files:**
- Modify: `frontend/src/components/baleni/BaleniPacking.tsx`
- Modify: `frontend/src/components/baleni/__tests__/BaleniPacking.test.tsx`

- [ ] **Step 1: Add the import and mount `<PackingLabelPrinter>` in `BaleniPacking.tsx`**

  Open `frontend/src/components/baleni/BaleniPacking.tsx`. Add the import at the top (after existing imports):

  ```tsx
  import PackingLabelPrinter from './PackingLabelPrinter';
  ```

  Then inside the JSX return, after the existing `{data && ( <PackingOrderNotes ... /> )}` block and before the items count paragraph, add:

  ```tsx
  {data && data.isInPackingState && (
    <PackingLabelPrinter orderCode={data.code} />
  )}
  ```

  The full updated JSX return should look like this (only the relevant section shown):

  ```tsx
  return (
    <div className="flex flex-col gap-4" data-testid="baleni-packing">
      {data && <PackingStateWarning order={data} />}
      <div className="flex items-start justify-between gap-4">
        <div className="min-w-0">
          {data && <PackingOrderMeta order={data} />}
        </div>
        <div className="flex flex-1 justify-center">
          {data && <PackingCoolingIndicator order={data} />}
        </div>
        <div className="w-72 shrink-0">
          <ScanInput
            label="Sken čísla objednávky"
            placeholder="Naskenujte objednávku…"
            onScan={handleScan}
            loading={isLoading}
            autoFocusOnMount
            refocusOnBlur
            allowKeyboardToggle
          />
        </div>
      </div>
      {data && (
        <PackingOrderNotes customerNote={data.customerNote} eshopNote={data.eshopNote} />
      )}
      {data && data.isInPackingState && (
        <PackingLabelPrinter orderCode={data.code} />
      )}
      {data && (
        <p className="text-xs uppercase tracking-wide text-neutral-gray">
          Položky ({data.items.length})
        </p>
      )}
      {renderBody()}
    </div>
  );
  ```

- [ ] **Step 2: Add a test to `BaleniPacking.test.tsx` verifying `PackingLabelPrinter` mounts**

  Open `frontend/src/components/baleni/__tests__/BaleniPacking.test.tsx`. At the top, add a mock for `PackingLabelPrinter` alongside the existing mock:

  ```tsx
  jest.mock('../PackingLabelPrinter', () => ({
    __esModule: true,
    default: ({ orderCode }: { orderCode: string }) => (
      <div data-testid="packing-label-printer" data-order-code={orderCode} />
    ),
  }));
  ```

  Then add two new test cases inside the `describe('BaleniPacking', ...)` block:

  ```tsx
  it('mounts PackingLabelPrinter when order is in packing state', () => {
    mockHook.mockReturnValue({
      ...baseResult,
      data: {
        code: '250001',
        customerName: 'Jan Novák',
        shippingMethodName: 'PPL',
        cooling: 'None',
        isCooled: false,
        statusId: 26,
        isInPackingState: true,
        customerNote: null,
        eshopNote: null,
        items: [],
      },
    });

    render(<BaleniPacking />);
    expect(screen.getByTestId('packing-label-printer')).toBeInTheDocument();
    expect(screen.getByTestId('packing-label-printer')).toHaveAttribute(
      'data-order-code',
      '250001'
    );
  });

  it('does not mount PackingLabelPrinter when order is not in packing state', () => {
    mockHook.mockReturnValue({
      ...baseResult,
      data: {
        code: '250001',
        customerName: 'Jan Novák',
        shippingMethodName: 'PPL',
        cooling: 'None',
        isCooled: false,
        statusId: 5,
        isInPackingState: false,
        customerNote: null,
        eshopNote: null,
        items: [],
      },
    });

    render(<BaleniPacking />);
    expect(screen.queryByTestId('packing-label-printer')).not.toBeInTheDocument();
  });
  ```

- [ ] **Step 3: Run the BaleniPacking tests**

  ```bash
  cd frontend && npx jest BaleniPacking --no-coverage 2>&1 | tail -15
  ```

  Expected: All tests pass.

- [ ] **Step 4: Run the full frontend test suite**

  ```bash
  cd frontend && npx jest --no-coverage 2>&1 | tail -20
  ```

  Expected: All tests pass.

- [ ] **Step 5: Commit**

  ```bash
  git add frontend/src/components/baleni/BaleniPacking.tsx \
          frontend/src/components/baleni/__tests__/BaleniPacking.test.tsx
  git commit -m "feat(baleni): wire PackingLabelPrinter into BaleniPacking"
  ```

---

## Task 11: Add E2E test for multi-package confirmation button

**Files:**
- Modify: `frontend/test/e2e/baleni/packing.spec.ts`

> **Note:** The test data fixture for a multi-package order in packing state must exist in `frontend/test/e2e/fixtures/test-data.ts`. Check for a suitable order code there. If none exists, throw (per project conventions) — never skip — and document in `docs/integrations/shoptet-api.md` what to set up.

- [ ] **Step 1: Read the test-data fixtures**

  ```bash
  cat frontend/test/e2e/fixtures/test-data.ts | grep -i "pack\|label\|shipment" -A3
  ```

  Identify an order code that is in the packing state and has multiple packages (shipment labels). If one exists, use it below. If not, add a `MULTI_PACKAGE_PACKING_ORDER_CODE` constant to `test-data.ts` with the real order code and document the Shoptet setup needed in `docs/integrations/shoptet-api.md`.

- [ ] **Step 2: Extend `packing.spec.ts`**

  Add this test at the end of the `test.describe` block. Replace `MULTI_PACKAGE_ORDER_CODE` with the actual constant from `test-data.ts`:

  ```typescript
  import { TEST_DATA } from '../fixtures/test-data';

  // Inside the existing describe block:
  test('shows a confirmation button for each additional package label', async ({ page }) => {
    const orderCode = TEST_DATA.MULTI_PACKAGE_PACKING_ORDER_CODE;
    if (!orderCode) {
      throw new Error(
        'MULTI_PACKAGE_PACKING_ORDER_CODE fixture missing — add a real multi-package order to test-data.ts'
      );
    }

    const input = page.getByRole('textbox');
    await input.fill(orderCode);
    await input.press('Enter');

    await expect(
      page.getByTestId('print-next-label-button')
    ).toBeVisible({ timeout: 15000 });

    await expect(
      page.getByTestId('print-next-label-button')
    ).toHaveText(/Vytisknout štítek 2\//);
  });
  ```

- [ ] **Step 3: Commit**

  ```bash
  git add frontend/test/e2e/baleni/packing.spec.ts
  git commit -m "test(e2e/baleni): assert confirmation button appears for multi-package orders"
  ```

---

## Task 12: Final validation

- [ ] **Step 1: Backend build and format**

  ```bash
  cd backend && dotnet build Anela.Heblo.sln --configuration Release -v quiet
  ```

  Expected: `Build succeeded.`

  ```bash
  cd backend && dotnet format Anela.Heblo.sln --verify-no-changes
  ```

  Expected: No changes.

  ```bash
  cd backend && dotnet test Anela.Heblo.sln -v minimal
  ```

  Expected: All tests pass.

- [ ] **Step 2: Frontend build and lint**

  ```bash
  cd frontend && npm run build 2>&1 | tail -10
  ```

  Expected: Build completes without errors. The OpenAPI TypeScript client is regenerated automatically — confirm `ShipmentLabelPdfNotFound` now appears in `frontend/src/api/generated/api-client.ts`.

  ```bash
  cd frontend && npm run lint 2>&1 | tail -10
  ```

  Expected: No lint errors.

- [ ] **Step 3: Full frontend unit test suite**

  ```bash
  cd frontend && npx jest --no-coverage 2>&1 | tail -10
  ```

  Expected: All tests pass.

---

## Self-Review

### Spec coverage check

| Spec requirement | Task |
|---|---|
| Same-origin PDF proxy `GET /api/shipment-labels/pdf` | Tasks 1–4 |
| Handler resolves `labelUrl` server-side (SSRF prevention) | Task 3 handler code |
| 404 on missing package/labelUrl | Task 3 handler; error code Task 1 |
| Logged + returned as 500 on HttpClient failure | Task 3 handler |
| `useShipmentLabels(orderCode, enabled)` with error mapping | Tasks 5–6 |
| Auto-print `labels[0]` | Task 9 (useEffect) |
| Confirmation button for each subsequent label | Task 9 |
| `printLabelPdf` builds same-origin URL | Task 7 |
| Hidden iframe + `contentWindow.print()` | Task 7 |
| Reset on new `orderCode` | Task 9 (orderCode useEffect) |
| Error banner; never blocks packer | Task 9 (error branch) |
| Mount only when `isInPackingState === true` | Task 10 |
| `gcTime: 0`, no retry | Task 6 hook config |
| E2E confirmation button test | Task 11 |
| BE `dotnet build` + `dotnet format` | Task 12 |
| FE `npm run build` + `npm run lint` | Task 12 |

### Placeholder scan

None — all steps contain complete code.

### Type consistency

- `ShipmentLabelDto.shipmentGuid` and `ShipmentLabelDto.packageName` are `string | undefined` in the generated client; the handler passes them through `?? ''` guards in Task 9.
- `GetShipmentLabelPdfRequest` fields (Task 3) match the controller query parameters (Task 4) and the URL params in `printLabelPdf` (Task 7).
- `printLabelPdf` accepts `{ shipmentGuid: string; packageName: string }` (internal interface, Task 7) — `PackingLabelPrinter` passes the same shape (Task 9).
