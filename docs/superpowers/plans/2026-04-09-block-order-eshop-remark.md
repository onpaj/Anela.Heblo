# BlockOrder: Switch from /history to eshopRemark Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Change the BlockOrder feature so the block reason is appended as a new line to the order's `eshopRemark` via `PATCH /api/orders/{code}/notes`, instead of being written as a new system history entry via `POST /api/orders/{code}/history`.

**Architecture:** The BlockOrder handler currently performs: (1) validate source state, (2) `PATCH .../status`, (3) `POST .../history`. We replace step (3) with a read-modify-write: read the current `eshopRemark` via `GET /api/orders/{code}`, append `"\n<note>"`, then `PATCH .../notes` with `{"data":{"eshopRemark":"<combined>"}}`. The existing `SetInternalNoteAsync` method on `IEshopOrderClient` remains on the client for possible future use but is no longer called by the handler. The status update stays unchanged.

**Tech Stack:** .NET 8, MediatR, xUnit + Moq + FluentAssertions, `HttpClient`, `System.Text.Json`, Clean Architecture / Vertical Slice.

---

## Context

The BlockOrder feature is triggered by `PATCH /api/shoptet-orders/{code}/block` and blocks a Shoptet order by transitioning its status and recording why it was blocked. Today the "why" is written as a Shoptet history entry (`POST /api/orders/{code}/history`, `type=system`). That creates a history row, and does not affect the searchable/visible order-level `eshopRemark` field that staff use day-to-day.

The user wants the block reason to live on the order itself, as a new line appended to the existing `eshopRemark`, so that:
1. Staff looking at the order in the Shoptet admin see the block reason directly in the remark field, not only in the history tab.
2. Multiple blocks on the same order accumulate as lines in a single field rather than as separate history rows.
3. The write uses the public Shoptet endpoint `PATCH /api/orders/{code}/notes` ("updateRemarksForOrder"), which is designed for this purpose.

The current `eshopRemark` value MUST be preserved — the new note is appended (newline-separated) to the end; it does not replace anything. The user explicitly asked that `SetInternalNoteAsync` remain on the client interface/implementation but no longer be called by the handler. The status-update step stays unchanged.

---

## File Map

**Modify:**
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs` — add two new methods: `GetEshopRemarkAsync`, `UpdateEshopRemarkAsync`. Leave `SetInternalNoteAsync` in place (unused).
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs` — implement the two new methods.
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/OrderListResponse.cs` — add `EshopRemark` property on `OrderSummary` (deserialized from GET /api/orders/{code}).
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/BlockOrderProcessing/BlockOrderProcessingHandler.cs` — replace the `SetInternalNoteAsync` call with a read-append-write using the new client methods.
- `backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/BlockOrderProcessingHandlerTests.cs` — update unit tests to mock and verify the new client methods.
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/BlockOrderProcessingIntegrationTests.cs` — update integration test assertions to read `eshopRemark` instead of listing history entries; add a new append-semantics test.
- `docs/integrations/shoptet-api.md` — add section documenting `PATCH /api/orders/{code}/notes` request body (`eshopRemark`, `customerRemark`, `trackingNumber`, `additionalFields[]`) and document that the BlockOrder feature uses this endpoint.

**Create:**
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/UpdateEshopRemarkRequest.cs` — new DTO for the PATCH /notes body. (The existing `UpdateNotesRequest.cs` file contains `CreateOrderRemarkRequest`, which is for POST /history — keep it as-is.)

**Do NOT change:**
- `ShoptetOrderClient.SetInternalNoteAsync(...)` — left on the client for possible future use, but no longer called.
- `CreateOrderRemarkRequest` / `CreateOrderRemarkData` DTOs in `UpdateNotesRequest.cs` — still used by `SetInternalNoteAsync`.
- `BlockOrderProcessingRequest` / `BlockOrderProcessingResponse` / controller / route / API contract — unchanged. No frontend work.
- `UpdateStatusAsync` and the status-update flow — unchanged.

---

## Verification Prerequisite — Confirm field name in GET response

**Before coding**, confirm that `GET /api/orders/{code}` actually returns the internal remark under the JSON property name `eshopRemark`. The PATCH /notes endpoint uses `eshopRemark`, but Shoptet GET responses sometimes use different names — e.g. the list endpoint uses `remark` for the customer remark.

- [ ] **Step 0: Probe a real test-store order to confirm the field name**

Run this against the test store (uses the same user-secrets token as the integration tests):

```bash
TOKEN=$(cat ~/.microsoft/usersecrets/anela-heblo-adapters-shoptet-tests/secrets.json \
  | python3 -c "import json,sys;print(json.load(sys.stdin)['Shoptet']['ApiToken'])")
BASE=$(cat ~/.microsoft/usersecrets/anela-heblo-adapters-shoptet-tests/secrets.json \
  | python3 -c "import json,sys;print(json.load(sys.stdin)['Shoptet']['BaseUrl'])")

# Pick the first order code
CODE=$(curl -s -H "Shoptet-Private-API-Token: $TOKEN" \
  "$BASE/api/orders?page=1&itemsPerPage=1" \
  | python3 -c "import json,sys;print(json.load(sys.stdin)['data']['orders'][0]['code'])")

# Dump top-level keys of the returned order
curl -s -H "Shoptet-Private-API-Token: $TOKEN" "$BASE/api/orders/$CODE" \
  | python3 -c "import json,sys;o=json.load(sys.stdin)['data']['order'];print(sorted(o.keys()))"
```

Expected: output contains `'eshopRemark'` (and `'remark'`, which is the customer remark). If instead only `'remark'` appears or the key is e.g. `'internalRemark'`, **STOP and update the plan**: the property name on `OrderSummary.EshopRemark` and the JSON attribute in subsequent steps must use whatever the real name is.

- [ ] **Step 1: Verify PATCH /notes accepts the body we intend to send**

Using the same `$TOKEN`, `$BASE`, and `$CODE` from Step 0 — writes an innocuous value and restores the original:

```bash
# Read current value
ORIG=$(curl -s -H "Shoptet-Private-API-Token: $TOKEN" "$BASE/api/orders/$CODE" \
  | python3 -c "import json,sys;print(json.load(sys.stdin)['data']['order'].get('eshopRemark') or '')")

# Write a test value
curl -s -X PATCH \
  -H "Shoptet-Private-API-Token: $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"data":{"eshopRemark":"plan-probe"}}' \
  "$BASE/api/orders/$CODE/notes" -w "\nHTTP %{http_code}\n"

# Confirm it was saved
curl -s -H "Shoptet-Private-API-Token: $TOKEN" "$BASE/api/orders/$CODE" \
  | python3 -c "import json,sys;print('stored:',json.load(sys.stdin)['data']['order'].get('eshopRemark'))"

# Restore original
python3 -c "import json,sys,os;print(json.dumps({'data':{'eshopRemark':os.environ['ORIG']}}))" \
  | ORIG="$ORIG" curl -s -X PATCH \
      -H "Shoptet-Private-API-Token: $TOKEN" \
      -H "Content-Type: application/json" \
      --data-binary @- \
      "$BASE/api/orders/$CODE/notes" -w "\nHTTP %{http_code}\n"
```

Expected: both PATCH calls return HTTP 200 and the "stored:" line prints `plan-probe`. If either fails, stop and investigate.

---

## Task 1: Extend `OrderSummary` with `eshopRemark`

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/OrderListResponse.cs`

- [ ] **Step 1: Add the `EshopRemark` property to `OrderSummary`**

Open `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/OrderListResponse.cs`. Inside class `OrderSummary` (currently ending with `PaymentMethod` at lines 68–70), add the new property just before the closing brace:

```csharp
    [JsonPropertyName("paymentMethod")]
    public OrderPaymentMethodSummary? PaymentMethod { get; set; }

    /// <summary>
    /// Internal (staff-facing) order remark.
    /// Returned only by GET /api/orders/{code} — NOT in the list endpoint.
    /// Updated via PATCH /api/orders/{code}/notes with body {"data":{"eshopRemark":"..."}}.
    /// Nullable because a new order has no internal remark.
    /// </summary>
    [JsonPropertyName("eshopRemark")]
    public string? EshopRemark { get; set; }
}
```

Also update the XML doc comment at lines 20–27 of the same file. Change:

```csharp
/// <summary>
/// Order summary as returned by GET /api/orders (list endpoint).
/// Available fields: code, guid, creationTime, changeTime, company, fullName, email,
/// phone, remark, cashDeskOrder, customerGuid, paid, status, source, price,
/// paymentMethod, shipping, adminUrl, salesChannelGuid.
/// Note: externalCode and billing/delivery addresses are NOT in the list response —
/// use GET /api/orders/{code} to retrieve them.
/// </summary>
```

to:

```csharp
/// <summary>
/// Order summary as returned by GET /api/orders (list endpoint) and GET /api/orders/{code}.
/// List-endpoint fields: code, guid, creationTime, changeTime, company, fullName, email,
/// phone, remark, cashDeskOrder, customerGuid, paid, status, source, price,
/// paymentMethod, shipping, adminUrl, salesChannelGuid.
/// Note: externalCode, eshopRemark, and billing/delivery addresses are NOT in the list
/// response — use GET /api/orders/{code} to retrieve them.
/// </summary>
```

- [ ] **Step 2: Build the adapter project**

Run:
```bash
dotnet build backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj
```
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/OrderListResponse.cs
git commit -m "feat(shoptet): add eshopRemark to OrderSummary DTO"
```

---

## Task 2: Create the `UpdateEshopRemarkRequest` DTO

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/UpdateEshopRemarkRequest.cs`

- [ ] **Step 1: Create the new DTO file**

Create `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/UpdateEshopRemarkRequest.cs` with the following contents:

```csharp
using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Orders.Model;

/// <summary>
/// Body for PATCH /api/orders/{code}/notes (Shoptet operationId: updateRemarksForOrder).
/// The endpoint accepts customerRemark, eshopRemark, trackingNumber, and additionalFields[] —
/// this project only updates eshopRemark, so only that property is modelled. Omitted fields
/// are left unchanged by the Shoptet API.
/// </summary>
public class UpdateEshopRemarkRequest
{
    [JsonPropertyName("data")]
    public UpdateEshopRemarkData Data { get; set; } = new();
}

public class UpdateEshopRemarkData
{
    [JsonPropertyName("eshopRemark")]
    public string EshopRemark { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Build the adapter project**

Run:
```bash
dotnet build backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj
```
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/UpdateEshopRemarkRequest.cs
git commit -m "feat(shoptet): add UpdateEshopRemarkRequest DTO for PATCH /notes"
```

---

## Task 3: Add `GetEshopRemarkAsync` + `UpdateEshopRemarkAsync` to the client interface

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs`

- [ ] **Step 1: Add the two new method signatures**

Open the file and add the two methods below. Keep `SetInternalNoteAsync` in place — it stays on the interface but will no longer be called by the handler.

Add inside the `IEshopOrderClient` interface body (location: after `SetInternalNoteAsync`, before `DeleteOrderAsync`):

```csharp
    /// <summary>
    /// Returns the current internal (staff-facing) remark for the given order,
    /// as returned by GET /api/orders/{code}.data.order.eshopRemark.
    /// Returns an empty string if Shoptet sends null or the property is missing.
    /// </summary>
    Task<string> GetEshopRemarkAsync(string orderCode, CancellationToken ct = default);

    /// <summary>
    /// Overwrites the order's internal (staff-facing) remark via
    /// PATCH /api/orders/{code}/notes with body {"data":{"eshopRemark":"..."}}.
    /// The caller is responsible for preserving any existing content (read-modify-write).
    /// </summary>
    Task UpdateEshopRemarkAsync(string orderCode, string eshopRemark, CancellationToken ct = default);
```

- [ ] **Step 2: Build the Application project**

Run:
```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```
Expected: **FAIL** — `ShoptetOrderClient` does not implement the two new interface members. This is intentional; we'll implement them in Task 4.

- [ ] **Step 3: Do NOT commit yet** — the interface change is not shippable alone. Move on to Task 4 and commit them together at the end of Task 4.

---

## Task 4: Implement `GetEshopRemarkAsync` + `UpdateEshopRemarkAsync` on `ShoptetOrderClient`

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs`

- [ ] **Step 1: Add `GetEshopRemarkAsync`**

In `ShoptetOrderClient.cs`, immediately after `GetOrderStatusIdAsync` (currently ending at line 84), add:

```csharp
    public async Task<string> GetEshopRemarkAsync(string orderCode, CancellationToken ct = default)
    {
        var detail = await GetOrderDetailInternalAsync(orderCode, ct);
        return detail.EshopRemark ?? string.Empty;
    }
```

This reuses the existing `GetOrderDetailInternalAsync` helper (lines 182–189) which deserializes `GET /api/orders/{code}` into `OrderSummary`. With the `EshopRemark` property added in Task 1, the value will be populated automatically.

- [ ] **Step 2: Add `UpdateEshopRemarkAsync`**

In the same file, immediately after `SetInternalNoteAsync` (currently ending at line 152), add:

```csharp
    public async Task UpdateEshopRemarkAsync(string orderCode, string eshopRemark, CancellationToken ct = default)
    {
        var body = new UpdateEshopRemarkRequest
        {
            Data = new UpdateEshopRemarkData { EshopRemark = eshopRemark },
        };

        var response = await _http.PatchAsJsonAsync($"/api/orders/{orderCode}/notes", body, JsonOptions, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"PATCH /api/orders/{orderCode}/notes returned {(int)response.StatusCode}: {errorBody}");
        }
    }
```

This mirrors the existing `UpdateStatusAsync` error-handling pattern (lines 122–136). `PatchAsJsonAsync` is the same extension already used there.

- [ ] **Step 3: Build the adapter and application projects**

Run:
```bash
dotnet build backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```
Expected: both succeed with `Build succeeded. 0 Warning(s). 0 Error(s).` — the interface from Task 3 is now satisfied.

- [ ] **Step 4: Commit (Tasks 3 + 4 together)**

```bash
git add backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs \
        backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs
git commit -m "feat(shoptet): add Get/Update EshopRemarkAsync client methods"
```

---

## Task 5: Update `BlockOrderProcessingHandler` to use eshopRemark

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/BlockOrderProcessing/BlockOrderProcessingHandler.cs`
- Modify (tests): `backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/BlockOrderProcessingHandlerTests.cs`

- [ ] **Step 1: Add a failing unit test for the new behavior (append semantics)**

Open `backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/BlockOrderProcessingHandlerTests.cs` and add the following test methods at the end of the test class (before the closing brace):

```csharp
    [Fact]
    public async Task Handle_OrderInAllowedState_AppendsNoteToExistingEshopRemark()
    {
        // Arrange
        var clientMock = new Mock<IEshopOrderClient>();
        clientMock.Setup(c => c.GetOrderStatusIdAsync("ORDER-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(26);
        clientMock.Setup(c => c.GetEshopRemarkAsync("ORDER-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("previous staff note");

        var handler = new BlockOrderProcessingHandler(
            clientMock.Object,
            Options.Create(new ShoptetOrdersSettings
            {
                AllowedBlockSourceStateIds = new[] { 26, -2 },
                BlockedStatusId = 99,
            }),
            NullLogger<BlockOrderProcessingHandler>.Instance);

        // Act
        var result = await handler.Handle(
            new BlockOrderProcessingRequest { OrderCode = "ORDER-1", Note = "blocked by accounting" },
            CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        clientMock.Verify(c => c.UpdateStatusAsync("ORDER-1", 99, It.IsAny<CancellationToken>()), Times.Once);
        clientMock.Verify(
            c => c.UpdateEshopRemarkAsync(
                "ORDER-1",
                "previous staff note\nblocked by accounting",
                It.IsAny<CancellationToken>()),
            Times.Once);
        clientMock.Verify(
            c => c.SetInternalNoteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_OrderWithEmptyEshopRemark_SetsNoteAsFirstLine()
    {
        // Arrange
        var clientMock = new Mock<IEshopOrderClient>();
        clientMock.Setup(c => c.GetOrderStatusIdAsync("ORDER-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(-2);
        clientMock.Setup(c => c.GetEshopRemarkAsync("ORDER-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);

        var handler = new BlockOrderProcessingHandler(
            clientMock.Object,
            Options.Create(new ShoptetOrdersSettings
            {
                AllowedBlockSourceStateIds = new[] { 26, -2 },
                BlockedStatusId = 99,
            }),
            NullLogger<BlockOrderProcessingHandler>.Instance);

        // Act
        var result = await handler.Handle(
            new BlockOrderProcessingRequest { OrderCode = "ORDER-2", Note = "fraud suspicion" },
            CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        clientMock.Verify(
            c => c.UpdateEshopRemarkAsync("ORDER-2", "fraud suspicion", It.IsAny<CancellationToken>()),
            Times.Once);
    }
```

- [ ] **Step 2: Run the new tests to confirm they fail**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~BlockOrderProcessingHandlerTests.Handle_OrderInAllowedState_AppendsNoteToExistingEshopRemark|FullyQualifiedName~BlockOrderProcessingHandlerTests.Handle_OrderWithEmptyEshopRemark_SetsNoteAsFirstLine"
```
Expected: both tests **FAIL** — because the handler still calls `SetInternalNoteAsync`, not `UpdateEshopRemarkAsync`.

- [ ] **Step 3: Update the handler**

Open `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/BlockOrderProcessing/BlockOrderProcessingHandler.cs`. Locate the block inside `Handle` that currently calls `SetInternalNoteAsync` (after `UpdateStatusAsync`). Replace the note-writing call with the read-append-write sequence.

Change this section of the handler body:

```csharp
            await _client.UpdateStatusAsync(request.OrderCode, _settings.BlockedStatusId, cancellationToken);
            await _client.SetInternalNoteAsync(request.OrderCode, request.Note, cancellationToken);
```

to:

```csharp
            await _client.UpdateStatusAsync(request.OrderCode, _settings.BlockedStatusId, cancellationToken);

            var currentRemark = await _client.GetEshopRemarkAsync(request.OrderCode, cancellationToken);
            var updatedRemark = string.IsNullOrEmpty(currentRemark)
                ? request.Note
                : $"{currentRemark}\n{request.Note}";
            await _client.UpdateEshopRemarkAsync(request.OrderCode, updatedRemark, cancellationToken);
```

Do NOT remove `SetInternalNoteAsync` from the interface or implementation — the user asked to keep it.

- [ ] **Step 4: Run the new tests to confirm they now pass**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~BlockOrderProcessingHandlerTests.Handle_OrderInAllowedState_AppendsNoteToExistingEshopRemark|FullyQualifiedName~BlockOrderProcessingHandlerTests.Handle_OrderWithEmptyEshopRemark_SetsNoteAsFirstLine"
```
Expected: both PASS.

- [ ] **Step 5: Do NOT commit yet** — the pre-existing tests still reference `SetInternalNoteAsync` verifications and will fail. Fix them in Task 6.

---

## Task 6: Fix the pre-existing unit tests

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/BlockOrderProcessingHandlerTests.cs`

- [ ] **Step 1: Update `Handle_OrderInAllowedState_ChangesStatusAndSetsNote`**

Locate the test at line 34 (approximate — `Handle_OrderInAllowedState_ChangesStatusAndSetsNote`). It currently mocks only `GetOrderStatusIdAsync`, `UpdateStatusAsync`, and `SetInternalNoteAsync`. Rewrite it to use the new client methods:

```csharp
    [Fact]
    public async Task Handle_OrderInAllowedState_ChangesStatusAndUpdatesEshopRemark()
    {
        // Arrange
        var clientMock = new Mock<IEshopOrderClient>();
        clientMock.Setup(c => c.GetOrderStatusIdAsync("ORDER-A", It.IsAny<CancellationToken>()))
            .ReturnsAsync(26);
        clientMock.Setup(c => c.GetEshopRemarkAsync("ORDER-A", It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);

        var handler = new BlockOrderProcessingHandler(
            clientMock.Object,
            Options.Create(new ShoptetOrdersSettings
            {
                AllowedBlockSourceStateIds = new[] { 26, -2 },
                BlockedStatusId = 99,
            }),
            NullLogger<BlockOrderProcessingHandler>.Instance);

        // Act
        var response = await handler.Handle(
            new BlockOrderProcessingRequest { OrderCode = "ORDER-A", Note = "test note" },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        clientMock.Verify(c => c.UpdateStatusAsync("ORDER-A", 99, It.IsAny<CancellationToken>()), Times.Once);
        clientMock.Verify(
            c => c.UpdateEshopRemarkAsync("ORDER-A", "test note", It.IsAny<CancellationToken>()),
            Times.Once);
    }
```

- [ ] **Step 2: Update `Handle_OrderInSecondAllowedState_Succeeds`**

Rewrite it so that `GetEshopRemarkAsync` is mocked and `UpdateEshopRemarkAsync` is the method verified. Keep the distinguishing detail (the `-2` source state):

```csharp
    [Fact]
    public async Task Handle_OrderInSecondAllowedState_Succeeds()
    {
        var clientMock = new Mock<IEshopOrderClient>();
        clientMock.Setup(c => c.GetOrderStatusIdAsync("ORDER-B", It.IsAny<CancellationToken>()))
            .ReturnsAsync(-2);
        clientMock.Setup(c => c.GetEshopRemarkAsync("ORDER-B", It.IsAny<CancellationToken>()))
            .ReturnsAsync("existing");

        var handler = new BlockOrderProcessingHandler(
            clientMock.Object,
            Options.Create(new ShoptetOrdersSettings
            {
                AllowedBlockSourceStateIds = new[] { 26, -2 },
                BlockedStatusId = 99,
            }),
            NullLogger<BlockOrderProcessingHandler>.Instance);

        var response = await handler.Handle(
            new BlockOrderProcessingRequest { OrderCode = "ORDER-B", Note = "note" },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        clientMock.Verify(c => c.UpdateStatusAsync("ORDER-B", 99, It.IsAny<CancellationToken>()), Times.Once);
        clientMock.Verify(
            c => c.UpdateEshopRemarkAsync("ORDER-B", "existing\nnote", It.IsAny<CancellationToken>()),
            Times.Once);
    }
```

- [ ] **Step 3: Update `Handle_OrderInDisallowedState_ReturnsInvalidSourceStateError_WithoutCallingShoptet`**

Replace the `SetInternalNoteAsync` negative verification with negative verifications for both `GetEshopRemarkAsync` and `UpdateEshopRemarkAsync`:

```csharp
    [Fact]
    public async Task Handle_OrderInDisallowedState_ReturnsInvalidSourceStateError_WithoutCallingShoptet()
    {
        var clientMock = new Mock<IEshopOrderClient>();
        clientMock.Setup(c => c.GetOrderStatusIdAsync("ORDER-C", It.IsAny<CancellationToken>()))
            .ReturnsAsync(100); // not in AllowedBlockSourceStateIds

        var handler = new BlockOrderProcessingHandler(
            clientMock.Object,
            Options.Create(new ShoptetOrdersSettings
            {
                AllowedBlockSourceStateIds = new[] { 26, -2 },
                BlockedStatusId = 99,
            }),
            NullLogger<BlockOrderProcessingHandler>.Instance);

        var response = await handler.Handle(
            new BlockOrderProcessingRequest { OrderCode = "ORDER-C", Note = "note" },
            CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ShoptetOrderInvalidSourceState);
        response.Params.Should().ContainKey("orderCode").WhoseValue.Should().Be("ORDER-C");
        response.Params.Should().ContainKey("currentStatusId").WhoseValue.Should().Be("100");

        clientMock.Verify(
            c => c.UpdateStatusAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        clientMock.Verify(
            c => c.GetEshopRemarkAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        clientMock.Verify(
            c => c.UpdateEshopRemarkAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
```

If the existing test's `response.ErrorCode` / `response.Params` property names differ from those above (e.g. `response.ErrorCodes` plural), adapt to the actual `BaseResponse` shape in this codebase — the intent is to keep the same error-path assertions the original test had, plus the new negative verifications.

- [ ] **Step 4: Review `Handle_ShoptetApiThrowsOnStatusFetch_ReturnsInternalServerError`**

No body changes needed — it throws from `GetOrderStatusIdAsync` before any write. After Step 6 of this task, confirm it still passes as-is.

- [ ] **Step 5: Update `Handle_ShoptetApiThrowsOnStatusUpdate_ReturnsInternalServerError`**

The existing test verifies that `SetInternalNoteAsync` is NOT called after a status-update throw. Replace that verification with:

```csharp
        clientMock.Verify(
            c => c.GetEshopRemarkAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        clientMock.Verify(
            c => c.UpdateEshopRemarkAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
```

Keep the existing setup that throws from `UpdateStatusAsync`.

- [ ] **Step 6: Run all `BlockOrderProcessingHandlerTests`**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~BlockOrderProcessingHandlerTests"
```
Expected: all tests (5 updated + 2 new = 7) PASS.

- [ ] **Step 7: Run the full Application test project**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```
Expected: all tests PASS — no regressions.

- [ ] **Step 8: Commit Tasks 5 + 6 together**

```bash
git add backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/BlockOrderProcessing/BlockOrderProcessingHandler.cs \
        backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/BlockOrderProcessingHandlerTests.cs
git commit -m "feat(shoptet): block-order writes to eshopRemark via PATCH /notes (append)"
```

---

## Task 7: Update the integration test

**Files:**
- Modify: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/BlockOrderProcessingIntegrationTests.cs`

- [ ] **Step 1: Read the existing file and identify the assertion block**

Open the file. The existing tests (`BlockOrder_Nova_Succeeds`, `BlockOrder_Poznamka_Succeeds`, `BlockOrder_VyrizujeSe_Succeeds`) each:
1. Create a real order.
2. Set it to the source state.
3. Call the handler with `Note = "Integration test block"`.
4. Assert the status changed AND the note was written.

Step (4) today verifies the history entry. Replace that with a direct read of `eshopRemark` via the new client method.

- [ ] **Step 2: Change the "note was written" assertion in the 3 existing success tests**

Wherever the existing tests currently verify the history entry (look for uses of a history-read call and/or FluentAssertions against a fetched history collection), replace with:

```csharp
        var remarkAfter = await client.GetEshopRemarkAsync(createdCode);
        remarkAfter.Should().EndWith("Integration test block");
```

Rationale: the order is freshly created with an empty `eshopRemark`, so after one block the value should equal `"Integration test block"`. Using `EndWith` rather than `Be` keeps the test robust if Shoptet ever returns trailing whitespace.

Apply this change to all three success tests.

- [ ] **Step 3: Add a new test covering the append-to-existing-remark case**

Add this test method to the same class, after the existing success tests and before the rejection test:

```csharp
    [Fact]
    public async Task BlockOrder_PreservesExistingEshopRemark_AndAppendsOnNewLine()
    {
        if (Environment.GetEnvironmentVariable("SHOPTET_BLOCK_ORDER") != "1")
            return;

        AssertTestEnvironment();

        var client = _fixture.Services.GetRequiredService<IEshopOrderClient>();
        var createdCode = await CreateTestOrderAsync(externalCodeSuffix: "append-remark");

        try
        {
            // Seed a pre-existing eshopRemark so we can verify append semantics
            await client.UpdateEshopRemarkAsync(createdCode, "pre-existing note from setup");
            await client.UpdateStatusAsync(createdCode, StatusNova);

            var handler = BuildHandler();
            var response = await handler.Handle(
                new BlockOrderProcessingRequest { OrderCode = createdCode, Note = "block reason from test" },
                CancellationToken.None);

            response.Success.Should().BeTrue();

            var remarkAfter = await client.GetEshopRemarkAsync(createdCode);
            remarkAfter.Should().Be("pre-existing note from setup\nblock reason from test");
        }
        finally
        {
            await client.DeleteOrderAsync(createdCode);
        }
    }
```

Notes:
- `CreateTestOrderAsync` and `BuildHandler` are placeholder names — use whatever helper names the existing tests in this file use. Match their patterns for `externalCode` prefix (`BLOCK-ORDER-TEST-*`).
- If `externalCodeSuffix` isn't the existing helper's parameter name, use the existing helper as-is and omit the extra parameter.
- `StatusNova` is already defined as `-1` (line 18) — leave as is.

- [ ] **Step 4: Build the integration test project**

Run:
```bash
dotnet build backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj
```
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 5: Run the integration tests locally (manual, gated)**

This test only runs when `SHOPTET_BLOCK_ORDER=1` is set AND `Shoptet:IsTestEnvironment=true` AND `Shoptet:BaseUrl` does not contain `"anela"`. If your local user secrets are set up per `docs/integrations/shoptet-api.md` section 6:

```bash
SHOPTET_BLOCK_ORDER=1 dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj \
  --filter "FullyQualifiedName~BlockOrderProcessingIntegrationTests"
```

Expected: all 4 existing tests + 1 new test PASS. If you don't have the test-store credentials set up, skip this step — the tests no-op silently per the `SHOPTET_BLOCK_ORDER` gate, and CI does not run them.

- [ ] **Step 6: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/BlockOrderProcessingIntegrationTests.cs
git commit -m "test(shoptet): block-order integration test verifies eshopRemark append"
```

---

## Task 8: Update Shoptet integration documentation

**Files:**
- Modify: `docs/integrations/shoptet-api.md`

- [ ] **Step 1: Add a new subsection `### 3.6 PATCH /api/orders/{code}/notes — Update Remarks`**

In section 3, after subsection 3.5 and before section 4, add:

````markdown
### 3.6 PATCH /api/orders/{code}/notes — Update Remarks (operationId: updateRemarksForOrder)

Updates the order's four remark/note slots. Any property omitted from the body is left unchanged on the server — the endpoint is a partial update.

**Request body:**
```json
{
  "data": {
    "customerRemark": "string or null",
    "eshopRemark":    "string or null",
    "trackingNumber": "string or null",
    "additionalFields": [
      { "index": 1, "text": "..." }
    ]
  }
}
```

- `customerRemark` — customer-facing note (what customer typed at checkout).
- `eshopRemark` — internal staff-facing note.
- `trackingNumber` — max 32 chars.
- `additionalFields` — optional, each `{ index: 1..6, text: string|null }`. Fields 1–3 max 255 chars.

**Success response (200):** `{"data":{},"errors":null}` — no meaningful body content.

**Field-to-GET-response mapping (names differ):**
- `customerRemark` (PATCH) ↔ `remark` (GET /api/orders list, GET /api/orders/{code}).
- `eshopRemark` (PATCH) ↔ `eshopRemark` (GET /api/orders/{code} only; NOT in GET /api/orders list response).

**NOT a history endpoint.** `PATCH /notes` overwrites `eshopRemark`. It does NOT create a history entry. To append rather than replace, the caller must first `GET /api/orders/{code}`, read the current `eshopRemark`, concatenate, then PATCH the combined value.

**Used by:** `BlockOrderProcessingHandler` — reads current `eshopRemark`, appends the block reason on a new line, writes back. Implementation: `ShoptetOrderClient.UpdateEshopRemarkAsync` and `ShoptetOrderClient.GetEshopRemarkAsync`.
````

- [ ] **Step 2: Update the `/history` bullet in section 6 to cross-reference section 3.6**

In section 6 (Test Environment Hydration), find the bullet about `POST /api/orders/{code}/history` (approximately line 236). It currently ends with: *"`PATCH /api/orders/{code}/notes` is for updating the order's `remark` field and custom fields — it is NOT for writing history entries."*

Change it to:

```markdown
`PATCH /api/orders/{code}/notes` is for updating `customerRemark` (= `remark` in GET), `eshopRemark`, `trackingNumber`, and 6 custom fields — it is NOT for writing history entries. See section 3.6 for the full contract.
```

- [ ] **Step 3: Commit**

```bash
git add docs/integrations/shoptet-api.md
git commit -m "docs(shoptet): document PATCH /orders/{code}/notes body and eshopRemark"
```

---

## Task 9: End-to-end verification

- [ ] **Step 1: Build the entire backend solution**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 2: Run the full backend test suite (excluding integration tests)**

Run:
```bash
dotnet test backend/Anela.Heblo.sln --filter "Category!=Integration"
```
Expected: all tests PASS — no regressions in any feature.

- [ ] **Step 3: Format check**

Run:
```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```
Expected: exit code 0. If it fails, run `dotnet format backend/Anela.Heblo.sln` and commit the formatting fix separately.

- [ ] **Step 4: Manual end-to-end smoke (optional, requires running backend)**

If you have the backend running locally with a valid Shoptet token against the test store:

1. Identify a real order in the test store that is in one of the `AllowedBlockSourceStateIds` states (e.g. `-1` Nová).
2. Note its current `eshopRemark` value.
3. Call the API:
   ```bash
   curl -X PATCH \
     -H "Authorization: Bearer $YOUR_ENTRA_ID_TOKEN" \
     -H "Content-Type: application/json" \
     -d '{"note":"smoke-test block reason"}' \
     https://localhost:5001/api/shoptet-orders/<ORDER_CODE>/block
   ```
4. Expect HTTP 204.
5. Fetch the order: `curl -H "Shoptet-Private-API-Token: $TOKEN" "$BASE/api/orders/<ORDER_CODE>"` and confirm:
   - `data.order.status.id` equals `ShoptetOrders:BlockedStatusId`.
   - `data.order.eshopRemark` equals `"<previous value>\nsmoke-test block reason"` (or just `"smoke-test block reason"` if the previous value was empty).
   - Open the Shoptet admin UI for this order — the internal remark field should show the same content.

- [ ] **Step 5: No changes to commit from Task 9 unless `dotnet format` flagged something.**

---

## Self-Review Checklist (already applied)

- [x] **Spec coverage** — every part of the request is covered:
  - "Use updateremarksfororder instead" → Tasks 2, 3, 4, 5 (new DTO, new client methods, handler swap).
  - "eshopRemark should be updated" → Tasks 1 (DTO field), 4 (PATCH body), 5 (handler writes eshopRemark).
  - "attach new line" → Task 5 step 3 and Tasks 5–6 tests verifying `"existing\nnote"` append semantics.
  - "instead of current remarks" → Task 5 replaces the `SetInternalNoteAsync` call; Task 6 removes stale assertions. `SetInternalNoteAsync` itself is left in place per user instruction.
- [x] **No placeholders** — every code block is concrete; no TBD/TODO/"add error handling".
- [x] **Type consistency** — `GetEshopRemarkAsync` returns `Task<string>` in the interface (Task 3), implementation returns `detail.EshopRemark ?? string.Empty` (Task 4), and handler uses `string.IsNullOrEmpty(currentRemark)` (Task 5) — all consistent.
- [x] **Method names consistent** — `GetEshopRemarkAsync` and `UpdateEshopRemarkAsync` used identically across Task 3 (interface), Task 4 (implementation), Task 5 (handler), Task 6 (unit tests), Task 7 (integration test), Task 8 (docs).
- [x] **JSON property name consistency** — `eshopRemark` (camelCase, `[JsonPropertyName("eshopRemark")]`) used identically in `OrderSummary` (Task 1), `UpdateEshopRemarkData` (Task 2), and cURL probe (prerequisite Steps 0/1).
- [x] **Prerequisite verification covered** — Steps 0 and 1 probe a real order to confirm field name before any code changes land.
