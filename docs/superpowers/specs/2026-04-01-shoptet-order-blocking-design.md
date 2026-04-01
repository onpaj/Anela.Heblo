# Shoptet Order Blocking — Design Spec

**Date:** 2026-04-01
**Branch:** feature/sharepoint_knowledgebase_ingestion (to be merged or branched separately)

---

## Overview

Add the ability to block a Shoptet order from processing via a REST API endpoint. Blocking means: validate the order is in an allowed source state, change its status to a configured "blocked" state, and write an internal note.

---

## FIT / GAP Analysis

| Capability | Status | Notes |
|---|---|---|
| `ShoptetOrderClient.UpdateStatusAsync` | FIT ✅ | Already implemented |
| `ShoptetOrderClient.GetOrderDetailAsync` | FIT ✅ | Already implemented, returns current status |
| `ShoptetOrderClient.SetInternalNoteAsync` | GAP ❌ | New method needed |
| Application handler for order blocking | GAP ❌ | New vertical slice needed |
| API endpoint for order blocking | GAP ❌ | New controller needed |
| Config for blockable states and target state | GAP ❌ | New settings class needed |

---

## Adapter Layer

### `ShoptetOrderClient` — new method

```csharp
public async Task SetInternalNoteAsync(string orderCode, string note, CancellationToken ct = default)
```

- Calls `PATCH /api/orders/{orderCode}/notes`
- Request body: `{"data": {"internalNote": "<note>"}}`
- Throws `HttpRequestException` on non-success response
- **Implementation note:** Verify the exact field name (`internalNote`) against the Shoptet OpenAPI spec at https://api.docs.shoptet.com/shoptet-api/openapi before coding.

### New model: `UpdateNotesRequest`

```csharp
public class UpdateNotesRequest
{
    [JsonPropertyName("data")]
    public UpdateNotesData Data { get; set; } = new();
}

public class UpdateNotesData
{
    [JsonPropertyName("internalNote")]
    public string InternalNote { get; set; } = string.Empty;
}
```

---

## Application Layer

### Location

`backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/`

### Settings

**`ShoptetOrdersSettings`** — bound from `"ShoptetOrders"` configuration section:

```csharp
public class ShoptetOrdersSettings
{
    public const string ConfigurationKey = "ShoptetOrders";

    public int[] AllowedBlockSourceStateIds { get; set; } = [];
    public int BlockedStatusId { get; set; }
}
```

**appsettings.json example:**
```json
"ShoptetOrders": {
  "AllowedBlockSourceStateIds": [26, -2],
  "BlockedStatusId": 99
}
```

Actual production values go in user secrets / Azure config.

### Handler: `BlockOrderProcessingHandler`

**Request:**
```csharp
public class BlockOrderProcessingRequest : IRequest
{
    public string OrderCode { get; set; } = null!;
    public string Note { get; set; } = null!;
}
```

**Handler steps:**
1. Fetch current order via `ShoptetOrderClient.GetOrderDetailAsync(orderCode)`
2. Check `order.Status.Id` is in `ShoptetOrdersSettings.AllowedBlockSourceStateIds`
   - If not → throw domain exception (message includes current status ID)
3. Call `ShoptetOrderClient.UpdateStatusAsync(orderCode, settings.BlockedStatusId)`
4. Call `ShoptetOrderClient.SetInternalNoteAsync(orderCode, note)`

---

## API Layer

### Controller: `ShoptetOrdersController`

**Endpoint:**
```
PATCH /api/shoptet-orders/{code}/block
```

**Request body:**
```json
{ "note": "string" }
```

**Responses:**
- `204 No Content` — order successfully blocked
- `422 Unprocessable Entity` — order is not in a blockable state (body includes current status ID)
- `404 Not Found` — order code not found in Shoptet (propagated from `HttpRequestException`)
- `500 Internal Server Error` — Shoptet API failure

---

## Error Handling

- **Invalid source state:** Domain exception thrown in handler, mapped to 422 at the controller level. Message: `"Order {code} is in state {currentId} which is not in the allowed block source states."`
- **Partial failure:** Status update and note write are two sequential Shoptet API calls with no transaction support. If note write fails after a successful status update, the error propagates as 500. This is acceptable for the current scope.

---

## Configuration Registration

`ShoptetOrdersSettings` is registered in `Program.cs` (or a new `ShoptetOrdersModule.cs`) alongside the existing `AddShoptetApiAdapter` call:

```csharp
services.AddOptions<ShoptetOrdersSettings>()
    .Bind(configuration.GetSection(ShoptetOrdersSettings.ConfigurationKey));
```

---

## Out of Scope

- Unblocking orders
- Bulk blocking
- Frontend UI
- MCP tools
- Audit logging
