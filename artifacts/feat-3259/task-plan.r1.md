# Task Plan: IEshopOrderClient Dead-Method Cleanup

Pure dead-code removal. No behaviour changes. Two tasks: delete files first, then strip the interface/implementation/test references, then verify the build is clean.

---

### task: delete-dead-files-and-strip-interface

**Files:**
- Delete: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/EshopOrderInfo.cs`
- Delete: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/ShoptetEshopResponse.cs`
- Delete: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/UpdateNotesRequest.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/BlockOrderProcessingHandlerTests.cs`

- [ ] **Step 1: Delete `EshopOrderInfo.cs`**

  ```bash
  rm backend/src/Anela.Heblo.Application/Features/ShoptetOrders/EshopOrderInfo.cs
  ```

- [ ] **Step 2: Delete `ShoptetEshopResponse.cs`**

  This file contains `ShoptetEshopResponse`, `ShoptetEshopData`, `ShoptetEshopDetail`, and `ShoptetOrderStatus` — all used only by the dead `GetOrderStatusNamesAsync` method.

  ```bash
  rm backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/ShoptetEshopResponse.cs
  ```

- [ ] **Step 3: Delete `UpdateNotesRequest.cs`**

  This file contains `CreateOrderRemarkRequest` and `CreateOrderRemarkData` — used only by the dead `SetInternalNoteAsync` method.

  ```bash
  rm backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/UpdateNotesRequest.cs
  ```

- [ ] **Step 4: Strip the three dead declarations from `IEshopOrderClient.cs`**

  Current file at `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs`.

  Remove line 8 (the `SetInternalNoteAsync` declaration) and lines 28–37 (XML doc + `GetRecentOrdersByEmailAsync` + XML doc + `GetOrderStatusNamesAsync`).

  The file should become:

  ```csharp
  namespace Anela.Heblo.Application.Features.ShoptetOrders;

  public interface IEshopOrderClient
  {
      Task<string> CreateOrderAsync(CreateEshopOrderRequest request, CancellationToken ct = default);
      Task<int> GetOrderStatusIdAsync(string orderCode, CancellationToken ct = default);
      Task UpdateStatusAsync(string orderCode, int statusId, CancellationToken ct = default);

      /// <summary>
      /// Returns the current internal (staff-facing) remark for the given order,
      /// as returned by GET /api/orders/{code}?include=notes → data.order.notes.eshopRemark.
      /// Returns an empty string if Shoptet sends null or the notes object is absent.
      /// </summary>
      Task<string> GetEshopRemarkAsync(string orderCode, CancellationToken ct = default);

      /// <summary>
      /// Overwrites the order's internal (staff-facing) remark via
      /// PATCH /api/orders/{code}/notes with body {"data":{"eshopRemark":"..."}}.
      /// The caller is responsible for preserving any existing content (read-modify-write).
      /// </summary>
      Task UpdateEshopRemarkAsync(string orderCode, string eshopRemark, CancellationToken ct = default);

      Task DeleteOrderAsync(string orderCode, CancellationToken ct = default);
      Task<List<EshopOrderSummary>> GetRecentOrdersAsync(int count = 20, CancellationToken ct = default);
      Task<List<EshopOrderSummary>> ListByExternalCodePrefixAsync(string prefix, string? emailFilter = null, CancellationToken ct = default);

      /// <summary>
      /// Transitions the order to the configured "packed" state
      /// (Shoptet "Zabaleno", id 52 by default). Called by the Balení screen
      /// after a successful scan + shipment creation.
      /// </summary>
      Task MarkAsPackedAsync(string orderCode, CancellationToken ct = default);
  }
  ```

- [ ] **Step 5: Remove dead methods and helper from `ShoptetOrderClient.cs`**

  File: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs`

  Remove the following four blocks (exact text shown for safe find-and-replace):

  **Block A — `SetInternalNoteAsync` (lines 163–177):**
  ```csharp
      public async Task SetInternalNoteAsync(string orderCode, string note, CancellationToken ct = default)
      {
          var body = new CreateOrderRemarkRequest
          {
              Data = new CreateOrderRemarkData { Text = note, Type = "system" },
          };

          var response = await _http.PostAsJsonAsync($"/api/orders/{orderCode}/history", body, JsonOptions, ct);
          if (!response.IsSuccessStatusCode)
          {
              var errorBody = await response.Content.ReadAsStringAsync(ct);
              throw new HttpRequestException(
                  $"POST /api/orders/{orderCode}/history returned {(int)response.StatusCode}: {errorBody}");
          }
      }
  ```

  **Block B — NOTE comment + `GetRecentOrdersByEmailAsync` (lines 201–215):**
  ```csharp
      // NOTE: Fetches only page 1. For high-volume stores the matching orders may not appear on the first page,
      // which can cause the email-fallback resolution path to under-return results or miss the customer GUID.
      public async Task<List<EshopOrderInfo>> GetRecentOrdersByEmailAsync(string email, int count, CancellationToken ct = default)
      {
          var itemsPerPage = Math.Min(count, 50);
          var response = await _http.GetAsync($"/api/orders?page=1&itemsPerPage={itemsPerPage}", ct);
          response.EnsureSuccessStatusCode();

          var data = await response.Content.ReadFromJsonAsync<OrderListResponse>(JsonOptions, ct);
          return (data?.Data.Orders ?? [])
              .Where(o => string.Equals(o.Email, email, StringComparison.OrdinalIgnoreCase))
              .Take(count)
              .Select(MapToOrderInfo)
              .ToList();
      }
  ```

  **Block C — `GetOrderStatusNamesAsync` (lines 217–226):**
  ```csharp
      public async Task<Dictionary<int, string>> GetOrderStatusNamesAsync(CancellationToken ct = default)
      {
          var response = await _http.GetAsync("/api/eshop?include=orderStatuses", ct);
          response.EnsureSuccessStatusCode();

          var data = await response.Content.ReadFromJsonAsync<ShoptetEshopResponse>(JsonOptions, ct);
          return (data?.Data?.Eshop?.OrderStatuses ?? [])
              .Where(s => !string.IsNullOrWhiteSpace(s.Name))
              .ToDictionary(s => s.Id, s => s.Name!);
      }
  ```

  **Block D — `MapToOrderInfo` private helper (lines 301–310):**
  ```csharp
      private static EshopOrderInfo MapToOrderInfo(OrderSummary o) => new()
      {
          Code = o.Code,
          CustomerGuid = o.CustomerGuid,
          TotalWithVat = o.Price?.WithVat,
          CurrencyCode = o.Price?.CurrencyCode,
          StatusId = o.Status.Id,
          AdminUrl = o.AdminUrl,
          OrderDate = o.CreationTime is { } t && DateTime.TryParse(t, out var dt) ? dt : null,
      };
  ```

  After removal there must be a blank line between `DeleteOrderAsync` and `MarkAsPackedAsync`, and between `MarkAsPackedAsync` and the `// ── Expedition methods` section header — keep the file's existing spacing rhythm.

- [ ] **Step 6: Remove the `Times.Never` verification from `BlockOrderProcessingHandlerTests.cs`**

  File: `backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/BlockOrderProcessingHandlerTests.cs`

  Remove lines 182–184 (the `clientMock.Verify` block that asserts `SetInternalNoteAsync` is never called). The closing brace of the test method on line 185 must remain.

  Lines to delete:
  ```csharp
          clientMock.Verify(
              c => c.SetInternalNoteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
              Times.Never);
  ```

- [ ] **Final Step: Build, format, and test**

  ```bash
  dotnet build backend/
  ```
  Expected: zero errors, zero warnings about missing types.

  ```bash
  dotnet format backend/ --verify-no-changes
  ```
  Expected: exits 0 (no diff produced).

  ```bash
  dotnet test backend/
  ```
  Expected: all tests pass, no regressions.

- [ ] **Commit**
  ```bash
  git add -A
  git commit -m "refactor: remove dead IEshopOrderClient methods and associated types

  Removes SetInternalNoteAsync, GetRecentOrdersByEmailAsync, and
  GetOrderStatusNamesAsync from IEshopOrderClient and ShoptetOrderClient —
  none have callers in production source. Deletes EshopOrderInfo,
  ShoptetEshopResponse, and UpdateNotesRequest (CreateOrderRemarkRequest)
  which existed solely to support these methods. Removes the Times.Never
  assertion for SetInternalNoteAsync from BlockOrderProcessingHandlerTests."
  ```
