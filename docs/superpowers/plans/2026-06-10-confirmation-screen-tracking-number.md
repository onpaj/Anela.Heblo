# Confirmation-Screen Tracking Number Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The packing kiosk confirmation screen ("Zakázka byla vyexpedována") shows the real carrier tracking number instead of the `"Vlastní balení"` package-name placeholder, by fetching the latest active shipment's tracking number once the label PDF has printed.

**Architecture:** A new read endpoint `GET /api/packaging/orders/{orderCode}/tracking-number` returns the tracking number of the latest active (non-cancelled) shipment via the existing `IShipmentClient.GetLatestActiveTrackingNumberAsync`. As a side effect it backfills the order's null-tracking `Package` rows so the Zásilky page updates immediately (no 10-minute job lag). On the frontend, `PackingLabelPrinter` calls this endpoint once printing completes (`isDone`) and passes the resolved number into `PackingShipmentDoneView`.

**Why this timing works:** The `/label.pdf` endpoint (`GetPackageLabelPdfHandler`) already polls Shoptet until the carrier label is ready. Shoptet assigns the `labelUrl` and the `trackingNumber` together, so by the time the label has printed and `isDone` is true, the tracking number is guaranteed present in Shoptet.

**Tech Stack:** .NET 8, EF Core 8, MediatR, MVC controllers, xUnit, Moq, FluentAssertions · React, TypeScript, @tanstack/react-query, Jest, @testing-library/react

---

## File Map

| Action | Path | Responsibility |
|--------|------|----------------|
| Modify | `backend/src/Anela.Heblo.Domain/Features/Packaging/IPackageRepository.cs` | Add `SetTrackingNumberByOrderCodeAsync` |
| Modify | `backend/src/Anela.Heblo.Persistence/Repositories/Packaging/PackageRepository.cs` | Implement `SetTrackingNumberByOrderCodeAsync` |
| Create | `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/GetOrderTrackingNumber/GetOrderTrackingNumberRequest.cs` | MediatR request |
| Create | `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/GetOrderTrackingNumber/GetOrderTrackingNumberResponse.cs` | MediatR response (extends `BaseResponse`) |
| Create | `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/GetOrderTrackingNumber/GetOrderTrackingNumberHandler.cs` | Fetch latest active tracking, persist, return it |
| Create | `backend/test/Anela.Heblo.Tests/Application/Packaging/GetOrderTrackingNumberHandlerTests.cs` | Handler unit tests |
| Modify | `backend/src/Anela.Heblo.API/Controllers/PackagingController.cs` | Add `GET orders/{orderCode}/tracking-number` |
| Create | `frontend/src/api/hooks/useOrderTrackingNumber.ts` | React-query hook for the endpoint |
| Create | `frontend/src/api/hooks/__tests__/useOrderTrackingNumber.test.ts` | Hook unit tests |
| Modify | `frontend/src/components/baleni/PackingShipmentDoneView.tsx` | Add `resolvedTrackingNumber` prop |
| Modify | `frontend/src/components/baleni/__tests__/PackingShipmentDoneView.test.tsx` | Tests for the new prop |
| Modify | `frontend/src/components/baleni/PackingLabelPrinter.tsx` | Fetch tracking on `isDone`, pass to done view |
| Modify | `frontend/src/components/baleni/__tests__/PackingLabelPrinter.test.tsx` | Keep green + assert wiring |

**Note on `IShipmentClient.GetLatestActiveTrackingNumberAsync`:** already exists (added in the FillTrackingNumbers fix on this branch). Do not re-create it.

**Note on the OpenAPI TS client:** the baleni module uses hand-written fetch hooks with absolute URLs (`${apiClient.baseUrl}${relativeUrl}`) — see `useScanPackingOrder.ts` / `useResetOrderShipment.ts`. Follow that pattern; do NOT use the generated client for this endpoint. The generated client will still pick up the new endpoint on `npm run build`, which is harmless.

---

## Task 1: Add `SetTrackingNumberByOrderCodeAsync` to the package repository

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Packaging/IPackageRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Repositories/Packaging/PackageRepository.cs`

- [ ] **Step 1: Add the interface method**

In `IPackageRepository.cs`, add this method signature after the existing `SetTrackingNumberAsync` declaration (keep all existing members unchanged):

```csharp
    /// <summary>
    /// Sets <paramref name="trackingNumber"/> on every package row for the given order
    /// whose <see cref="Package.TrackingNumber"/> is currently null. No-ops if there are none.
    /// </summary>
    Task SetTrackingNumberByOrderCodeAsync(string orderCode, string trackingNumber, CancellationToken cancellationToken = default);
```

- [ ] **Step 2: Implement it in `PackageRepository`**

In `PackageRepository.cs`, add this method directly after the existing `SetTrackingNumberAsync` method (before the `private static string EscapeLike` helper):

```csharp
    public async Task SetTrackingNumberByOrderCodeAsync(
        string orderCode,
        string trackingNumber,
        CancellationToken cancellationToken = default)
    {
        var packages = await _db.Packages
            .Where(p => p.OrderCode == orderCode && p.TrackingNumber == null)
            .ToListAsync(cancellationToken);

        if (packages.Count == 0)
            return;

        foreach (var package in packages)
            package.TrackingNumber = trackingNumber;

        await _db.SaveChangesAsync(cancellationToken);
    }
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build Anela.Heblo.sln`
Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Packaging/IPackageRepository.cs \
        backend/src/Anela.Heblo.Persistence/Repositories/Packaging/PackageRepository.cs
git commit -m "feat(packaging): add SetTrackingNumberByOrderCodeAsync to IPackageRepository"
```

---

## Task 2: Create the `GetOrderTrackingNumber` use case (TDD)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/GetOrderTrackingNumber/GetOrderTrackingNumberRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/GetOrderTrackingNumber/GetOrderTrackingNumberResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/GetOrderTrackingNumber/GetOrderTrackingNumberHandler.cs`
- Test: `backend/test/Anela.Heblo.Tests/Application/Packaging/GetOrderTrackingNumberHandlerTests.cs`

**Behavior contract:**
- Calls `IShipmentClient.GetLatestActiveTrackingNumberAsync(orderCode)`.
- If it returns a non-empty tracking number → persists it via `IPackageRepository.SetTrackingNumberByOrderCodeAsync(orderCode, tracking)` and returns it in the response.
- If it returns null/empty → does NOT persist; returns a success response with `TrackingNumber = null`.
- If Shoptet throws → logs a warning and returns a success response with `TrackingNumber = null` (kiosk falls back to the package name; the screen must never error here).

- [ ] **Step 1: Create the request type**

`GetOrderTrackingNumberRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.GetOrderTrackingNumber;

public class GetOrderTrackingNumberRequest : IRequest<GetOrderTrackingNumberResponse>
{
    public string OrderCode { get; set; } = null!;
}
```

- [ ] **Step 2: Create the response type (MUST extend `BaseResponse`)**

`GetOrderTrackingNumberResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.GetOrderTrackingNumber;

public class GetOrderTrackingNumberResponse : BaseResponse
{
    public string? TrackingNumber { get; set; }
}
```

> A reflection contract test in CI fails for any Application `*Response` that does not extend `BaseResponse`. This one does — keep it that way.

- [ ] **Step 3: Write the failing handler tests**

`GetOrderTrackingNumberHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Packaging.UseCases.GetOrderTrackingNumber;
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Domain.Features.Packaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Application.Packaging;

public class GetOrderTrackingNumberHandlerTests
{
    private static (GetOrderTrackingNumberHandler Sut, Mock<IShipmentClient> Client, Mock<IPackageRepository> Repo)
        MakeSut()
    {
        var client = new Mock<IShipmentClient>();
        var repo = new Mock<IPackageRepository>();
        var sut = new GetOrderTrackingNumberHandler(
            client.Object, repo.Object, NullLogger<GetOrderTrackingNumberHandler>.Instance);
        return (sut, client, repo);
    }

    [Fact]
    public async Task Handle_ReturnsAndPersistsTracking_WhenLatestActiveShipmentHasIt()
    {
        var (sut, client, repo) = MakeSut();
        client.Setup(c => c.GetLatestActiveTrackingNumberAsync("126000034", It.IsAny<CancellationToken>()))
            .ReturnsAsync("2421907688");

        var response = await sut.Handle(new GetOrderTrackingNumberRequest { OrderCode = "126000034" }, default);

        response.Success.Should().BeTrue();
        response.TrackingNumber.Should().Be("2421907688");
        repo.Verify(r => r.SetTrackingNumberByOrderCodeAsync("126000034", "2421907688", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsNullAndDoesNotPersist_WhenNoActiveTrackingYet()
    {
        var (sut, client, repo) = MakeSut();
        client.Setup(c => c.GetLatestActiveTrackingNumberAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var response = await sut.Handle(new GetOrderTrackingNumberRequest { OrderCode = "ORD-1" }, default);

        response.Success.Should().BeTrue();
        response.TrackingNumber.Should().BeNull();
        repo.Verify(r => r.SetTrackingNumberByOrderCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsNullAndDoesNotThrow_WhenShoptetThrows()
    {
        var (sut, client, repo) = MakeSut();
        client.Setup(c => c.GetLatestActiveTrackingNumberAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Shoptet 500"));

        var response = await sut.Handle(new GetOrderTrackingNumberRequest { OrderCode = "ORD-1" }, default);

        response.Success.Should().BeTrue();
        response.TrackingNumber.Should().BeNull();
        repo.Verify(r => r.SetTrackingNumberByOrderCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 4: Run the tests to confirm they fail to compile (RED)**

Run: `cd backend/test/Anela.Heblo.Tests && dotnet build`
Expected: build FAILS with `The type or namespace name 'GetOrderTrackingNumberHandler' could not be found`.

- [ ] **Step 5: Implement the handler (GREEN)**

`GetOrderTrackingNumberHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Domain.Features.Packaging;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.GetOrderTrackingNumber;

public class GetOrderTrackingNumberHandler
    : IRequestHandler<GetOrderTrackingNumberRequest, GetOrderTrackingNumberResponse>
{
    private readonly IShipmentClient _shipmentClient;
    private readonly IPackageRepository _packageRepository;
    private readonly ILogger<GetOrderTrackingNumberHandler> _logger;

    public GetOrderTrackingNumberHandler(
        IShipmentClient shipmentClient,
        IPackageRepository packageRepository,
        ILogger<GetOrderTrackingNumberHandler> logger)
    {
        _shipmentClient = shipmentClient;
        _packageRepository = packageRepository;
        _logger = logger;
    }

    public async Task<GetOrderTrackingNumberResponse> Handle(
        GetOrderTrackingNumberRequest request,
        CancellationToken cancellationToken)
    {
        string? trackingNumber;
        try
        {
            trackingNumber = await _shipmentClient.GetLatestActiveTrackingNumberAsync(request.OrderCode, cancellationToken);
        }
        catch (Exception ex)
        {
            // Best-effort: the confirmation screen falls back to the package name. Never surface an error here.
            _logger.LogWarning(ex,
                "GetOrderTrackingNumber: failed to fetch tracking for order {OrderCode}.", request.OrderCode);
            return new GetOrderTrackingNumberResponse { TrackingNumber = null };
        }

        if (!string.IsNullOrEmpty(trackingNumber))
        {
            await _packageRepository.SetTrackingNumberByOrderCodeAsync(request.OrderCode, trackingNumber, cancellationToken);
        }

        return new GetOrderTrackingNumberResponse { TrackingNumber = trackingNumber };
    }
}
```

- [ ] **Step 6: Run the tests (GREEN)**

Run: `cd backend/test/Anela.Heblo.Tests && dotnet test --filter "GetOrderTrackingNumberHandlerTests"`
Expected: 3 passed.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/GetOrderTrackingNumber/ \
        backend/test/Anela.Heblo.Tests/Application/Packaging/GetOrderTrackingNumberHandlerTests.cs
git commit -m "feat(packaging): GetOrderTrackingNumber use case returns latest active shipment tracking"
```

---

## Task 3: Expose the endpoint on `PackagingController`

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/PackagingController.cs`

- [ ] **Step 1: Add the using directive**

At the top of `PackagingController.cs`, add this `using` alongside the existing `Anela.Heblo.Application.Features.Packaging.UseCases.*` imports (keep alphabetical grouping):

```csharp
using Anela.Heblo.Application.Features.Packaging.UseCases.GetOrderTrackingNumber;
```

- [ ] **Step 2: Add the endpoint action**

Add this method to `PackagingController` (place it right after the `GetPackageLabelPdf` action, before `GetDashboard`):

```csharp
    /// <summary>
    /// Returns the tracking number of the latest active (non-cancelled) shipment for the order,
    /// backfilling it onto the order's package rows. Used by the kiosk confirmation screen once
    /// the label has printed (tracking is guaranteed assigned by then). Returns trackingNumber: null
    /// when no active shipment has one yet — callers fall back to the package name.
    /// </summary>
    [HttpGet("orders/{orderCode}/tracking-number")]
    public async Task<ActionResult<GetOrderTrackingNumberResponse>> GetOrderTrackingNumber(
        [FromRoute] string orderCode,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(
            new GetOrderTrackingNumberRequest { OrderCode = orderCode },
            cancellationToken);
        return HandleResponse(response);
    }
```

- [ ] **Step 3: Build and run the full backend test suite for the feature**

Run: `dotnet build Anela.Heblo.sln && cd backend/test/Anela.Heblo.Tests && dotnet test --filter "FullyQualifiedName~Packaging"`
Expected: `Build succeeded.`; all Packaging tests pass (includes the new handler tests).

- [ ] **Step 4: Format and commit**

```bash
dotnet format Anela.Heblo.sln --include backend/src/Anela.Heblo.API/Controllers/PackagingController.cs
git add backend/src/Anela.Heblo.API/Controllers/PackagingController.cs
git commit -m "feat(packaging): expose GET orders/{orderCode}/tracking-number endpoint"
```

---

## Task 4: Create the `useOrderTrackingNumber` frontend hook (TDD)

**Files:**
- Create: `frontend/src/api/hooks/useOrderTrackingNumber.ts`
- Test: `frontend/src/api/hooks/__tests__/useOrderTrackingNumber.test.ts`

All frontend commands run from `frontend/`.

- [ ] **Step 1: Write the failing hook test**

`frontend/src/api/hooks/__tests__/useOrderTrackingNumber.test.ts`:

```typescript
import { renderHook, waitFor } from '@testing-library/react';
import { useOrderTrackingNumber } from '../useOrderTrackingNumber';
import { createMockApiClient, mockAuthenticatedApiClient, createQueryClientWrapper } from '../../testUtils';

jest.mock('../../client');

describe('useOrderTrackingNumber', () => {
  let mockFetch: jest.Mock;

  beforeEach(() => {
    const mock = createMockApiClient();
    mockFetch = mock.mockFetch;
    mockAuthenticatedApiClient(mock.mockClient);
  });

  afterEach(() => {
    jest.clearAllMocks();
  });

  it('returns the tracking number from a successful response', async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: () => Promise.resolve({ success: true, trackingNumber: '2421907688' }),
    });

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useOrderTrackingNumber('126000034', true), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toBe('2421907688');
  });

  it('returns null when the response has no tracking number', async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: () => Promise.resolve({ success: true, trackingNumber: null }),
    });

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useOrderTrackingNumber('ORD-1', true), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toBeNull();
  });

  it('returns null when the response is not successful', async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: () => Promise.resolve({ success: false, errorCode: 'Exception' }),
    });

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useOrderTrackingNumber('ORD-1', true), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toBeNull();
  });

  it('does not fetch when disabled', () => {
    const { wrapper } = createQueryClientWrapper();
    renderHook(() => useOrderTrackingNumber('ORD-1', false), { wrapper });
    expect(mockFetch).not.toHaveBeenCalled();
  });
});
```

- [ ] **Step 2: Run the test to confirm it fails (RED)**

Run: `CI=true npm test -- --testPathPattern="useOrderTrackingNumber"`
Expected: FAIL — `Cannot find module '../useOrderTrackingNumber'`.

- [ ] **Step 3: Implement the hook (GREEN)**

`frontend/src/api/hooks/useOrderTrackingNumber.ts`:

```typescript
import { useQuery } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';

interface ApiClientWithInternals {
  baseUrl: string;
  http: { fetch(url: RequestInfo, init?: RequestInit): Promise<Response> };
}

const fetchOrderTrackingNumber = async (orderCode: string): Promise<string | null> => {
  const apiClient = getAuthenticatedApiClient(false) as unknown as ApiClientWithInternals;
  const response = await apiClient.http.fetch(
    `${apiClient.baseUrl}/api/packaging/orders/${encodeURIComponent(orderCode)}/tracking-number`,
  );
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const data = (await response.json()) as any;
  if (!data.success) return null;
  return (data.trackingNumber as string | null) ?? null;
};

export const useOrderTrackingNumber = (orderCode: string, enabled: boolean) =>
  useQuery<string | null>({
    queryKey: ['order-tracking-number', orderCode],
    queryFn: () => fetchOrderTrackingNumber(orderCode),
    enabled,
    staleTime: 0,
  });
```

- [ ] **Step 4: Run the test (GREEN)**

Run: `CI=true npm test -- --testPathPattern="useOrderTrackingNumber"`
Expected: 4 passed.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/api/hooks/useOrderTrackingNumber.ts \
        frontend/src/api/hooks/__tests__/useOrderTrackingNumber.test.ts
git commit -m "feat(baleni): useOrderTrackingNumber hook for the confirmation screen"
```

---

## Task 5: Add `resolvedTrackingNumber` prop to `PackingShipmentDoneView` (TDD)

**Files:**
- Modify: `frontend/src/components/baleni/PackingShipmentDoneView.tsx`
- Test: `frontend/src/components/baleni/__tests__/PackingShipmentDoneView.test.tsx`

**Behavior:** when `resolvedTrackingNumber` is a non-empty string, render it as the "Číslo zásilky" value. Otherwise fall back to the existing `shipment.packages.map((p) => p.trackingNumber ?? p.name).join(', ')`.

- [ ] **Step 1: Add the failing tests**

Append these two tests inside the `describe('PackingShipmentDoneView', ...)` block in `PackingShipmentDoneView.test.tsx` (before its closing `});`):

```typescript
  it('shows resolvedTrackingNumber when provided, overriding the package summary', () => {
    render(
      <PackingShipmentDoneView
        order={makeOrder()}
        shipment={makeShipment()}
        resolvedTrackingNumber="2421907688"
        onReprint={() => {}}
      />
    );
    expect(screen.getByText('2421907688')).toBeInTheDocument();
    expect(screen.queryByText('TR-1, TR-2')).not.toBeInTheDocument();
  });

  it('falls back to the package summary when resolvedTrackingNumber is null or empty', () => {
    render(
      <PackingShipmentDoneView
        order={makeOrder()}
        shipment={makeShipment()}
        resolvedTrackingNumber={null}
        onReprint={() => {}}
      />
    );
    expect(screen.getByText('TR-1, TR-2')).toBeInTheDocument();
  });
```

- [ ] **Step 2: Run the tests to confirm the first fails (RED)**

Run: `CI=true npm test -- --testPathPattern="PackingShipmentDoneView"`
Expected: the `shows resolvedTrackingNumber...` test FAILS (component ignores the prop, still renders `TR-1, TR-2`).

- [ ] **Step 3: Implement the prop**

In `PackingShipmentDoneView.tsx`, update the props interface and the `trackingSummary` computation. Replace the interface:

```typescript
interface PackingShipmentDoneViewProps {
  order: PackingOrder;
  shipment: ScanShipment;
  onReprint: () => void;
}
```

with:

```typescript
interface PackingShipmentDoneViewProps {
  order: PackingOrder;
  shipment: ScanShipment;
  resolvedTrackingNumber?: string | null;
  onReprint: () => void;
}
```

Then replace the function signature line:

```typescript
function PackingShipmentDoneView({ order, shipment, onReprint }: PackingShipmentDoneViewProps) {
  const addressLines = buildAddressLines(order);
  const trackingSummary = shipment.packages.map((p) => p.trackingNumber ?? p.name).join(', ');
```

with:

```typescript
function PackingShipmentDoneView({
  order,
  shipment,
  resolvedTrackingNumber,
  onReprint,
}: PackingShipmentDoneViewProps) {
  const addressLines = buildAddressLines(order);
  const trackingSummary =
    resolvedTrackingNumber && resolvedTrackingNumber.length > 0
      ? resolvedTrackingNumber
      : shipment.packages.map((p) => p.trackingNumber ?? p.name).join(', ');
```

(The `{trackingSummary}` usage in the JSX is unchanged.)

- [ ] **Step 4: Run the tests (GREEN)**

Run: `CI=true npm test -- --testPathPattern="PackingShipmentDoneView"`
Expected: all PackingShipmentDoneView tests pass (the original ones still pass because the prop is optional).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/baleni/PackingShipmentDoneView.tsx \
        frontend/src/components/baleni/__tests__/PackingShipmentDoneView.test.tsx
git commit -m "feat(baleni): PackingShipmentDoneView accepts resolvedTrackingNumber override"
```

---

## Task 6: Wire the fetch into `PackingLabelPrinter`

**Files:**
- Modify: `frontend/src/components/baleni/PackingLabelPrinter.tsx`
- Modify: `frontend/src/components/baleni/__tests__/PackingLabelPrinter.test.tsx`

`PackingLabelPrinter` already computes `isDone`. Call `useOrderTrackingNumber(order.code, isDone)` and pass `data` into the done view. The hook only fetches once `isDone` is true (printing finished → tracking ready).

- [ ] **Step 1: Update the existing test file so it does not break, and assert the wiring**

The current test mocks `PackingShipmentDoneView` and does NOT provide a `QueryClientProvider`. Adding a `useQuery` call to the component would throw "No QueryClient set". So mock the new hook, and make the done-view mock surface the prop.

In `PackingLabelPrinter.test.tsx`, add this mock next to the existing `jest.mock('../printLabelPdf', ...)` mock (top of file):

```typescript
jest.mock('../../../api/hooks/useOrderTrackingNumber', () => ({
  useOrderTrackingNumber: jest.fn(() => ({ data: null })),
}));
```

Replace the existing `jest.mock('../PackingShipmentDoneView', ...)` block with one that renders the resolved tracking number so wiring can be asserted:

```typescript
jest.mock('../PackingShipmentDoneView', () => ({
  __esModule: true,
  default: ({
    resolvedTrackingNumber,
    onReprint,
  }: {
    resolvedTrackingNumber?: string | null;
    onReprint: () => void;
  }) => (
    <div data-testid="done-view">
      <span data-testid="done-tracking">{resolvedTrackingNumber ?? ''}</span>
      <button data-testid="reprint" onClick={onReprint}>R</button>
    </div>
  ),
}));
```

Add this import near the top (after the other imports):

```typescript
import { useOrderTrackingNumber } from '../../../api/hooks/useOrderTrackingNumber';
```

Add a typed handle to the mock (after `const mockPrintLabelPdf = ...`):

```typescript
const mockUseOrderTrackingNumber = useOrderTrackingNumber as jest.MockedFunction<typeof useOrderTrackingNumber>;
```

Add this test inside the `describe('PackingLabelPrinter', ...)` block (before its closing `});`):

```typescript
  it('passes the resolved tracking number into the done view once printing is finished', () => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    mockUseOrderTrackingNumber.mockReturnValue({ data: '2421907688' } as any);

    render(
      <PackingLabelPrinter order={makeOrder('250001')} shipment={makeShipment([pkg1])} />
    );
    fireAck(0);

    expect(screen.getByTestId('done-view')).toBeInTheDocument();
    expect(screen.getByTestId('done-tracking')).toHaveTextContent('2421907688');
  });
```

- [ ] **Step 2: Run the test to confirm the new one fails (RED)**

Run: `CI=true npm test -- --testPathPattern="PackingLabelPrinter"`
Expected: the new `passes the resolved tracking number...` test FAILS (component does not yet pass `resolvedTrackingNumber`). Existing tests still pass (the hook is mocked, returning `{ data: null }`).

- [ ] **Step 3: Wire the hook into the component**

In `PackingLabelPrinter.tsx`, add the import after the existing imports:

```typescript
import { useOrderTrackingNumber } from '../../api/hooks/useOrderTrackingNumber';
```

Inside the component, after the line `const isDone = labels.length > 0 && acknowledgedCount >= labels.length;`, add:

```typescript
  const trackingQuery = useOrderTrackingNumber(order.code, isDone);
```

Then update the done-view render block:

```typescript
  if (isDone) {
    return (
      <PackingShipmentDoneView
        order={order}
        shipment={shipment}
        onReprint={handleReprint}
      />
    );
  }
```

to pass the resolved value:

```typescript
  if (isDone) {
    return (
      <PackingShipmentDoneView
        order={order}
        shipment={shipment}
        resolvedTrackingNumber={trackingQuery.data ?? null}
        onReprint={handleReprint}
      />
    );
  }
```

- [ ] **Step 4: Run the test (GREEN)**

Run: `CI=true npm test -- --testPathPattern="PackingLabelPrinter"`
Expected: all PackingLabelPrinter tests pass.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/baleni/PackingLabelPrinter.tsx \
        frontend/src/components/baleni/__tests__/PackingLabelPrinter.test.tsx
git commit -m "feat(baleni): show latest active tracking number on the confirmation screen"
```

---

## Task 7: Full verification

**Files:** none (verification only).

- [ ] **Step 1: Backend build + targeted tests**

Run: `dotnet build Anela.Heblo.sln && cd backend/test/Anela.Heblo.Tests && dotnet test --filter "FullyQualifiedName~Packaging|FullyQualifiedName~ShoptetShipment"`
Expected: `Build succeeded.`; all pass.

- [ ] **Step 2: Backend format check**

Run: `dotnet format Anela.Heblo.sln --verify-no-changes` (if it reports changes, run `dotnet format Anela.Heblo.sln` and re-commit the touched files).

- [ ] **Step 3: Frontend build + lint + targeted tests**

Run (from `frontend/`):
```bash
npm run build
CI=true npm test -- --testPathPattern="useOrderTrackingNumber|PackingShipmentDoneView|PackingLabelPrinter"
```
Expected: build succeeds; all listed tests pass. (`npm run lint` over the whole repo has pre-existing failures unrelated to these files — confirm no NEW lint errors are introduced in the files you touched.)

- [ ] **Step 4: Manual staging verification (after deploy)**

1. On the Balení kiosk, scan an already-packed order whose latest active shipment has a label (e.g. `126000034`).
2. Let the label print; on "Zakázka byla vyexpedována", confirm "Číslo zásilky" shows the carrier tracking number (e.g. `2421907688`), not `Vlastní balení`.
3. Open the Zásilky page and confirm the same order now shows the tracking number in "Sledovací č." immediately (the endpoint backfilled the DB row — no wait for the 10-minute job).

---

## Self-Review

**Spec coverage:**
- Confirmation screen shows real tracking number → Tasks 4-6 (hook + prop + wiring).
- Tracking sourced from latest active shipment → Task 2 (reuses `GetLatestActiveTrackingNumberAsync`).
- Captured at the moment it's ready (post-print `isDone`) → Task 6 (`enabled: isDone`).
- Immediate Zásilky update (no 10-min lag) → Tasks 1-2 (`SetTrackingNumberByOrderCodeAsync` persisted in the handler).
- Graceful fallback when tracking not ready / Shoptet error → handler returns `null` (Task 2), done view falls back to package name (Task 5).

**Placeholder scan:** none — every code/test step contains full content and exact commands.

**Type consistency:**
- `SetTrackingNumberByOrderCodeAsync(string, string, CancellationToken)` — defined Task 1, mocked Task 2, called Task 2 handler. ✔
- `GetLatestActiveTrackingNumberAsync` — pre-existing on `IShipmentClient`. ✔
- `GetOrderTrackingNumberResponse.TrackingNumber` (`string?`) — defined Task 2, read by controller (Task 3) and hook JSON (`data.trackingNumber`, Task 4). ✔
- `useOrderTrackingNumber(orderCode: string, enabled: boolean)` → `useQuery<string | null>` — defined Task 4, called Task 6 with `(order.code, isDone)`. ✔
- `resolvedTrackingNumber?: string | null` on `PackingShipmentDoneView` — defined Task 5, passed Task 6. ✔

**Scope note:** `PackingShipmentCreator` also renders `PackingShipmentDoneView` directly for the non-eligible rescan review path (without `resolvedTrackingNumber`); that path keeps its current behavior, which already shows tracking when the scan response carries it (rescans return the latest active shipment's labels). Out of scope here; revisit only if that screen also shows the placeholder.
