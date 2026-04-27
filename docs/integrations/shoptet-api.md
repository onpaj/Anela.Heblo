# Shoptet API — Integration Findings

> **Living document.** Every new finding about the Shoptet API MUST be added here before it is used elsewhere.
> API reference: https://api.docs.shoptet.com/shoptet-api/openapi
> OpenAPI spec: https://api.docs.shoptet.com/_bundle/Shoptet%20API/openapi.json

---

## 1. Overview

Shoptet is an e-commerce platform that exposes a REST API at `https://api.myshoptet.com`. The project uses two separate adapters:

| Adapter | Type | Purpose |
|---|---|---|
| `Anela.Heblo.Adapters.Shoptet` | Console exe (Playwright) | Invoice scraping, stock operations via browser automation |
| `Anela.Heblo.Adapters.ShoptetApi` | Class library (HTTP) | Direct REST API calls (ShoptetPay payouts, orders) |

These two are **completely unrelated** — do not confuse them.

---

## 2. Authentication

- **Add-on (OAuth2):** For apps published in the Shoptet app store.
- **Premium private access:** Direct API token in `Authorization: Bearer {token}` header.
- The project uses **premium private access** via `ShoptetPaySettings.ApiToken`.
- No sandbox/test environment is documented. All API calls hit the live store configured by the token.
- **Header name:** `Shoptet-Private-API-Token: <token>` — NOT `Authorization: Bearer`. Using Bearer returns 401.

---

## 3. Orders API

### 3.1 Endpoints

| Method | Path | Description |
|---|---|---|
| `POST` | `/api/orders` | Create an order |
| `GET` | `/api/orders` | List orders (paginated, default 50, max 100) |
| `GET` | `/api/orders/{code}` | Get single order (optional `include` sections) |
| `GET` | `/api/orders/snapshot` | Async full export (jsonlines) |
| `PATCH` | `/api/orders/{code}/head` | Update basic info (email, addresses) |
| `PATCH` | `/api/orders/{code}/status` | Change status / mark paid / update payment method |
| `PATCH` | `/api/orders/{code}/notes` | Update remarks and 6 custom fields |
| `GET` | `/api/orders/{code}/history` | List history entries for an order |
| `POST` | `/api/orders/{code}/history` | Add a remark to order history (type: `comment` or `system`) |
| `DELETE` | `/api/orders/{code}/history/{id}` | Delete a specific history entry |
| `GET` | `/api/orders/history/snapshot` | Bulk async history export for up to 50 orders (gzip jsonlines, HTTP 202 + jobId) |
| `PATCH` | `/api/orders/status-change` | Bulk status update for multiple orders |
| `DELETE` | `/api/orders/{code}` | Delete order |
| `POST` | `/api/orders/{code}/copy` | Duplicate order |
| `GET` | `/api/orders/{code}/pdf` | Download order as PDF |

### 3.2 Order Statuses

Statuses are store-specific (configured in Shoptet admin). Retrievable via `GET /api/eshop?include=orderStatuses`.

- **System statuses:** Negative integers (e.g. `-4` = Cancelled)
- **Custom statuses:** Positive integers
- Each status has: `id` (int), `order` (sequence), `name` (string), `markAsPaid` (bool)

### 3.3 POST /api/orders — Request Body

**Required fields:**
```json
{
  "email": "string (max 100)",
  "paymentMethodGuid": "string",
  "shippingGuid": "string",
  "currency": { "code": "CZK" },
  "billingAddress": {
    "fullName": "string",
    "street": "string",
    "city": "string",
    "zip": "string"
  },
  "items": [ ... ]
}
```

**Additional required fields (discovered via integration testing):**

- `phone` — required, must be in international format (e.g. `+420725191660`). Omitting it returns 422.
- When `paymentMethodGuid` is provided, `items` MUST include at least one item with `"itemType": "billing"`.
- When `shippingGuid` is provided, `items` MUST include at least one item with `"itemType": "shipping"`.
- Product items (`"itemType": "product"`) require a real, existing variant-level `code` in the store catalog. The API validates the code against the catalog — there is no suppress flag to bypass this via REST. Use a known real variant code (e.g. `OCH001030` — "Ochráním zadečky, 30ml" in the Anela test store).

**suppress* flags do NOT work via REST.** Do not include `suppressProductChecking`, `suppressEmailSending`, `suppressStockMovements`, or `suppressDocumentGeneration` — the API returns 422 for unknown fields.

**Address fields** (both `billingAddress` and `deliveryAddress`):
`company`, `fullName`, `street`, `houseNumber`, `city`, `district`, `additional`, `zip`, `regionName`, `regionShortcut`, `companyId`, `vatId`, `taxId` — all max 255 chars.

**Item fields:**
```json
{
  "itemType": "product | billing | shipping | discount-coupon | volume-discount | gift | gift-certificate | generic-item | product-set | product-set-item | deposit",
  "code": "string (max 64)",
  "name": "string (max 250)",
  "variantName": "string (max 128)",
  "vatRate": "21",
  "itemPriceWithVat": "100.00",
  "amount": "1"
}
```

**IMPORTANT — numeric fields must be JSON strings.** `vatRate`, `itemPriceWithVat`, and `amount` must be sent as strings (e.g. `"21"`, `"1.00"`, `"1"`), not JSON numbers. Sending them as numbers returns 422. The correct field name for quantity is `amount` (not `quantity`).

**`product-set` in GET order response:** When an order contains a set product, Shoptet returns the set header as a `product-set` item in `items[]` (with its own SKU, e.g. `SA009`). The individual components are **not** in `items[]` — they are in the separate `completion[]` array as `product-set-item` entries, linked to the parent via `parentProductSetItemId` matching the parent's `itemId`. To build an expedition/picking list for sets, ignore `product-set` in `items[]` and instead expand it using the `product-set-item` entries from `completion[]`, multiplying component quantities by the set quantity.


### 3.4 Shipping Methods

- Identified by `shippingGuid` (store-specific GUID, not portable between stores).
- Retrieve available methods: `GET /api/eshop?include=shippingMethods`
- Order response includes `shippingDetails` section (via `?include=shippingDetails`):
  - `branchId` — carrier branch identifier
  - `carrierId` — numerical carrier ID (primarily for Zásilkovna)

#### Known Shipping IDs (production store)

These are the numeric `shippingId` values used by `ShoptetPlaywrightExpeditionListSource` for the picking list scenario. The same IDs must be used when seeding test orders so the picking list can find them via `?f[shippingId]={id}`.

Source of truth: `backend/src/Adapters/Anela.Heblo.Adapters.Shoptet/Playwright/ShoptetPlaywrightExpeditionListSource.cs`

| ID | Constant Name | Carrier | PageSize |
|---|---|---|---|
| 21 | `ZASILKOVNA_DO_RUKY` | Zásilkovna | 8 |
| 15 | `ZASILKOVNA_ZPOINT` | Zásilkovna | 8 |
| 385 | `ZASILKOVNA_DO_RUKY_SK` | Zásilkovna | 8 |
| 370 | `ZASILKOVNA_DO_RUKY_CHLAZENY` | Zásilkovna | 8 |
| 373 | `ZASILKOVNA_ZPOINT_CHLAZENY` | Zásilkovna | 8 |
| 388 | `ZASILKOVNA_DO_RUKY_SK_CHLAZENY` | Zásilkovna | 8 |
| 487 | `ZASILKOVNA_ZPOINT_ZDARMA` | Zásilkovna | 8 |
| 481 | `ZASILKOVNA_ZPOINT_CHLAZENY_ZDARMA` | Zásilkovna | 8 |
| 6 | `PPL_DO_RUKY` | PPL | 8 |
| 80 | `PPL_PARCELSHOP` | PPL | 8 |
| 86 | `PPL_EXPORT` | PPL | 8 |
| 358 | `PPL_DO_RUKY_CHLAZENY` | PPL | 8 |
| 361 | `PPL_PARCELSHOP_CHLAZENY` | PPL | 8 |
| 379 | `PPL_EXPORT_CHLAZENY` | PPL | 8 |
| 97 | `GLS_DO_RUKY` | GLS | 8 |
| 109 | `GLS_EXPORT` | GLS | 8 |
| 489 | `GLS_PARCELSHOP` | GLS | 8 |
| 4 | `OSOBAK` | Osobak | 1 |

**Important:** These are Shoptet admin-internal numeric IDs used in URL filter params (`?f[shippingId]=21`), not the `shippingGuid` used in the REST API order creation body. When seeding test orders via `POST /api/orders`, use the corresponding `shippingGuid` from `GET /api/eshop?include=shippingMethods` that maps to the desired shipping ID.

#### Order Statuses (known values from code)

Defined in `PrintPickingListOptions` and `ShoptetPlaywrightExpeditionListSource`:

| ID | Name |
|---|---|
| -2 | Vyřizuje se (Processing) — source state for picking list |
| 26 | Balí se (Packing) — PACK seed state; `PrintPickingListOptions.DesiredStateId` |
| 70 | Předáno přepravci (Handed to carrier) — EXP seed state |
| 73 | Oprava-robot — Fix source state (`FixSourceStateId`) |

> **Note:** Status 55 ("K Expedici") referenced in `ShoptetPlaywrightExpeditionListSource.DesiredStateId` does **not exist** in the store (confirmed via `GET /api/eshop?include=orderStatuses`). Seeding EXP-category orders uses status 70 instead.
> Custom status IDs for the hydration test are configurable: `Shoptet:StatusId:EXP` and `Shoptet:StatusId:PACK` in user secrets.

### 3.5 GET /api/orders — Filtering Parameters

- `statusId` — filter by status id; **use `statusId=` (not `status=`)** — the correct parameter name supports both positive custom IDs and negative system IDs (e.g. `?statusId=-2` for "Vyřizuje se" works correctly; `?status=-2` returns 400)
- `transport` — filter by shipping method
- `payment` — filter by payment method
- Date range filters
- `code` — order code
- `page`, `itemsPerPage` — pagination; **max `itemsPerPage` is 50** (not 100; passing 100 is rejected)

Optional `include` sections: `notes`, `images`, `shippingDetails`, `stockLocation`, `surchargeParameters`, `productFlags`

**`?include=stockLocation`** — required to get the warehouse position on order items. The field is named `stockLocation` (a nullable string, e.g. `"H6/M11/R13"`) — there is no `warehousePosition` field. Without this include, `stockLocation` is absent from all items.

**`amount` field in GET response** — returned as a JSON decimal number (e.g. `1.000`), NOT a string and NOT an integer. Map to `decimal?` with `[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]`, then cast to `int` when needed.

**Fields in list response:** `code`, `guid`, `creationTime`, `changeTime`, `company`, `fullName`, `email`, `phone`, `remark`, `cashDeskOrder`, `customerGuid`, `paid`, `status`, `source`, `price`, `paymentMethod`, `shipping`, `adminUrl`, `salesChannelGuid`. **`externalCode` is NOT included.** Use `GET /api/orders/{code}` (single detail) to get `externalCode`.

### 3.6 PATCH /api/orders/{code}/notes — Update Remarks (operationId: updateRemarksForOrder)

Updates the order's remark/note slots. Any property omitted from the body is left unchanged on the server — the endpoint is a partial update.

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

**Success response (200):** `{"data":null,"errors":null}` — no meaningful body content.

**Field-to-GET-response mapping (names differ):**
- `customerRemark` (PATCH) ↔ `remark` (GET /api/orders list, GET /api/orders/{code}).
- `eshopRemark` (PATCH) ↔ `notes.eshopRemark` (GET /api/orders/{code}?include=notes only; NOT in GET /api/orders list response).

**NOT a history endpoint.** `PATCH /notes` overwrites `eshopRemark`. It does NOT create a history entry. To append rather than replace, the caller must first `GET /api/orders/{code}?include=notes`, read `data.order.notes.eshopRemark`, concatenate, then PATCH the combined value.

**Used by:** `BlockOrderProcessingHandler` — reads current `eshopRemark` via `GetEshopRemarkAsync`, appends the block reason on a new line, writes back via `UpdateEshopRemarkAsync`. Both methods are on `ShoptetOrderClient`.

---

## 4. Products API

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/products` | List products (paginated, default 20, max 50) |
| `GET` | `/api/products/{guid}` | Get single product detail |

**`include` sections:** Only `images` is supported on the list endpoint. **`variants` and `productVariants` are NOT valid include values and return 400.**

### 4.1 GET /api/products (list)

Returns paginated product list — **does not include variants**. Max `itemsPerPage` is 50.

### 4.2 GET /api/products/{guid} (detail)

Returns the full product including a `variants[]` array with `code` fields — **no `include` needed**:

```json
{
  "data": {
    "guid": "...",
    "name": "Ochráním zadečky",
    "isVariant": true,
    "variants": [
      { "code": "OCH001030", "stock": "120.000", "price": "190.00", ... }
    ]
  }
}
```

### 4.3 Getting all variant codes efficiently

**Do NOT use N+1 product detail calls.** Use the stock CSV export instead — it lists all variant codes in a single request and is already parsed by `IEshopStockClient.ListAsync()`. The `EshopStock.Code` field is the variant-level SKU accepted by `POST /api/orders`.

The snapshot endpoint (`GET /api/products/snapshot`) exists but requires a registered webhook for `job:finished` — not usable without webhook infrastructure.

---

## 5. ShoptetPay API

Base URL: `https://api.shoptetpay.com`

### 4.1 Payout Reports

| Method | Path | Description |
|---|---|---|
| `GET` | `/v1/reports/payout` | List payout reports (filter: `dateFrom`, `dateTo`, `types`, `limit`) |
| `GET` | `/v1/reports/payout/{id}/abo` | Download payout report as ABO file |

**Date format:** `yyyy-MM-dd` (not ISO 8601 round-trip).

**PayoutReportDto fields:** `id`, `currency`, `type`, `serialNumber`, `dateFrom`, `dateTo`, `createdAt`

---

## 6. Existing Project Integration Points

### 5.1 Adapter: `Anela.Heblo.Adapters.ShoptetApi`
Location: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/`

- `ShoptetPayBankClient` — implements `IBankClient` for ShoptetPay payout downloads
- `ShoptetPaySettings` — config key: `"ShoptetPay"` (`ApiToken`, `BaseUrl`)
- Registered via `AddShoptetApiAdapter(configuration)` in `Program.cs`

### 5.2 Adapter: `Anela.Heblo.Adapters.Shoptet` (Playwright)
Location: `backend/src/Adapters/Anela.Heblo.Adapters.Shoptet/`

- Browser automation for invoice scraping and stock sync
- Separate test project: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/`
- Test fixture: `ShoptetIntegrationTestFixture` (user secrets + env vars for credentials)

### 5.3 Existing Integration Tests
- `ShoptetStockClientIntegrationTests` — validates CSV stock export parsing
- `ShoptetPriceClientIntegrationTests` — validates price client
- `ShoptetPlaywrightInvoiceSourceIntegrationTests` — validates Playwright invoice source
- Tests use `[Trait("Category", "Integration")]` and `[Collection("ShoptetIntegration")]`

---

## 7. Test Environment Notes

- **No official Shoptet sandbox.** Test env is a real Shoptet store configured with test credentials.
- **`suppress*` flags are NOT supported by the REST API.** `suppressEmailSending`, `suppressStockMovements`, `suppressDocumentGeneration`, `suppressProductChecking` only exist in the Shoptet admin UI import flow — the REST `POST /api/orders` endpoint rejects them with 422. Do not include them in the request body.
- `shippingGuid` and `paymentMethodGuid` values are **store-specific** — must be discovered at runtime via `GET /api/eshop?include=shippingMethods,paymentMethods` or configured per environment.
- Orders created for testing should use a recognizable `externalCode` prefix (e.g. `TEST-`) to allow cleanup.

---

### Test Environment Hydration (issue #444)

- **Seeding endpoint:** `POST /api/orders` — creates orders with minimal valid payload.
- **Status update endpoint:** `PATCH /api/orders/{code}/status` — body shape `{"data":{"statusId":<int>}}`. Note: the property is `statusId` (flat integer), NOT `{"status":{"id":x}}` — verified against Shoptet OpenAPI spec (`additionalProperties: false` schema).
- **Internal note / history remark:** `POST /api/orders/{code}/history` — body `{"data":{"text":"...","type":"system"}}`. The `type` field is either `"comment"` (visible to customer) or `"system"` (internal). `PATCH /api/orders/{code}/notes` is for updating `customerRemark` (= `remark` in GET), `eshopRemark`, `trackingNumber`, and 6 custom fields — it is NOT for writing history entries. See section 3.6 for the full contract.
- **Read history for one order:** `GET /api/orders/{code}/history` — returns `data.orderHistory[]`. Each entry: `id` (int), `creationTime` (datetime string), `text` (string), `system` (bool — true = Shoptet-generated), `type` ("comment"|"system"), `user` object with `id` (email or system process ID) and `name` (human-readable name). **Author is `user.name`, NOT `author`/`createdBy`/`userName`.** Timestamp is `creationTime`, NOT `createdAt`. Optional query param `system` (bool) to filter system-only or user-only entries. Max 100 history items per order (POST returns 403 if exceeded).
- **Bulk history fetch:** `GET /api/orders/history/snapshot?orderCodes=A,B,C` — async, max 50 codes per call. Returns HTTP 202 with `data.jobId`. Poll `GET /api/orders/history/snapshot/{jobId}` until it returns a download URL. Download is gzip-compressed jsonlines (one entry per line), same schema as single-order history but extended with `orderCode` field identifying the parent order.
- **Delete endpoint:** `DELETE /api/orders/{code}`.
- **ExternalCode filter does NOT exist.** `GET /api/orders?externalCode={code}` returns 400 "Unsupported query parameters". There is no server-side filter by externalCode.
- **ExternalCode is NOT in the list response.** `GET /api/orders` (paginated list) does NOT include the `externalCode` field for each order. The field is only available via `GET /api/orders/{code}` (single order detail). The list response DOES include `email`, which can be used as a pre-filter to narrow down candidates before fetching details.
- **Guard 1:** Config key `Shoptet:IsTestEnvironment` must be `true` before any hydration call.
- **Guard 2:** `Shoptet:BaseUrl` must not contain `"anela"` (case-insensitive) to prevent running against production.
- **ShippingGuidMap:** Numeric shipping IDs (21, 6) map to UUIDs stored in user secrets under `Shoptet:ShippingGuidMap:{id}`. See mapping procedure and known GUIDs below.
- **StatusId:** Custom status IDs for EXP and PACK seed categories are store-specific and stored in user secrets under `Shoptet:StatusId:EXP` and `Shoptet:StatusId:PACK`. Discover available IDs via `GET /api/eshop?include=orderStatuses`.
- **Default order status on create:** New orders do not land in the target state — always call `PATCH /status` immediately after `POST /api/orders`.
- **ExternalCode uniqueness:** The API enforces uniqueness on `externalCode`. If you try to create an order with a duplicate externalCode you get 422 "already imported". Note: after `DELETE /api/orders/{code}`, the externalCode appears to remain reserved — the order may persist in a deleted/hidden state.

### Shipping ID → GUID mapping procedure

The picking-list URL filter uses a **numeric shipping ID** (`?f[shippingId]={id}`), while `POST /api/orders` requires a **GUID**. These must be mapped manually per store.

**Discovery command:**
```bash
curl -s -H "Shoptet-Private-API-Token: <token>" \
  "https://api.myshoptet.com/api/eshop?include=shippingMethods" \
  | python3 -c "
import json, sys
data = json.load(sys.stdin)
for m in data['data']['shippingMethods']['retail']['methods']:
    print(f\"guid={m['guid']}  name={m['name']}\")
"
```

Note: the response **does not include numeric IDs** — match by method name against the constants in `ShoptetPlaywrightExpeditionListSource.cs`.

**How to get a shipping GUID for a new/unknown method:**

1. Run the discovery command above with the production API token (from user secrets: `Shoptet:ApiToken`).
2. Find the method by name in the output.
3. Add the GUID to `ShippingList` in `ShoptetApiExpeditionListSource.cs` and to the table below.
4. To verify: place a real order with that shipping method in the store, then call `GET /api/orders/{code}` — the `shipping.guid` field must match.

**Known mappings — production store (269953 / anela.cz):**

Discovered via `GET /api/eshop?include=shippingMethods` with production API token. The response contains two channels: `retail` (B2C) and `wholesale` (VO/B2B) — each with separate GUIDs for the same logical carrier. Both are mapped so wholesale orders appear in the expedition list alongside retail orders.

**Retail (B2C) GUIDs:**

| Numeric ID | Constant | Method name | GUID |
|---|---|---|---|
| 21 | `ZASILKOVNA_DO_RUKY` | Zásilkovna (do ruky) | `f6610d4d-578d-11e9-beb1-002590dad85e` |
| 15 | `ZASILKOVNA_ZPOINT` | Zásilkovna Z-Point | `7878c138-578d-11e9-beb1-002590dad85e` |
| 385 | `ZASILKOVNA_DO_RUKY_SK` | Zásilkovna (do ruky) SK | `a6d9a6ce-0ede-11ee-b534-2a01067a25a9` |
| 370 | `ZASILKOVNA_DO_RUKY_CHLAZENY` | Zásilkovna chlazený balík (do ruky) | `34d3f7d4-166f-11ee-b534-2a01067a25a9` |
| 373 | `ZASILKOVNA_ZPOINT_CHLAZENY` | Zásilkovna Z-Point chlazený balík - ZDARMA od 1500,- | `bac58d34-166f-11ee-b534-2a01067a25a9` |
| 388 | `ZASILKOVNA_DO_RUKY_SK_CHLAZENY` | Zásilkovna SK chlazený balík (do ruky) | `75123baa-1671-11ee-b534-2a01067a25a9` |
| 487 | `ZASILKOVNA_ZPOINT_ZDARMA` | Zásilkovna Z-Point - DOPRAVA ZDARMA | `79b9ef95-5e46-11f0-ae6d-9237d29d7242` |
| 481 | `ZASILKOVNA_ZPOINT_CHLAZENY_ZDARMA` | Zásilkovna Z-Point - PLATÍTE POUZE CHLADÍTKO | `db9bf927-5e44-11f0-ae6d-9237d29d7242` |
| 6 | `PPL_DO_RUKY` | PPL (do ruky) | `2ec88ea7-3fb0-11e2-a723-705ab6a2ba75` |
| 80 | `PPL_PARCELSHOP` | PPL ParcelShop | `c4e6c287-9a85-11ea-beb1-002590dad85e` |
| 86 | `PPL_EXPORT` | PPL Export (doručení do zahraničí) | `f17a0a12-0ebe-11eb-933a-002590dad85e` |
| 358 | `PPL_DO_RUKY_CHLAZENY` | PPL chlazený balík (do ruky) - ZDARMA od 3000,- | `05ea842d-166a-11ee-b534-2a01067a25a9` |
| 361 | `PPL_PARCELSHOP_CHLAZENY` | PPL ParcelShop chlazený balík - ZDARMA od 1500,- | `0d10802f-166c-11ee-b534-2a01067a25a9` |
| 379 | `PPL_EXPORT_CHLAZENY` | PPL Export chlazený balík (zahraničí) | `de70f0e4-1670-11ee-b534-2a01067a25a9` |
| 97 | `GLS_DO_RUKY` | GLS (do ruky) | `138ec07f-0119-11ec-a39f-002590dc5efc` |
| 109 | `GLS_EXPORT` | GLS Export (doručení do zahraničí) | `c06835e6-165e-11ec-a39f-002590dc5efc` |
| 489 | `GLS_PARCELSHOP` | GLS ParcelShop | `49b79aec-0118-11ec-a39f-002590dc5efc` |
| 4 | `OSOBAK` | Osobní odběr v Dobrušce | `8fdb2c89-3fae-11e2-a723-705ab6a2ba75` |

**Wholesale / VO (B2B) GUIDs:**

These are separate GUID variants for the same carriers used in wholesale orders. Mapped to the same constants as retail.

| Constant | Method name (VO) | GUID |
|---|---|---|
| `ZASILKOVNA_ZPOINT` | Zásilkovna Z-Point | `389cea0b-40f1-11ea-beb1-002590dad85e` |
| `PPL_DO_RUKY` | PPL (do ruky) | `389ce5b4-40f1-11ea-beb1-002590dad85e` |
| `PPL_PARCELSHOP` | PPL ParcelShop (vyzvednutí na pobočce) | `83372e07-9a86-11ea-beb1-002590dad85e` |
| `PPL_EXPORT` | PPL Export (doručení do zahraničí) | `2fd96b91-1508-11eb-933a-002590dad85e` |
| `GLS_DO_RUKY` | GLS (do ruky) | `b7e787c5-011d-11ec-a39f-002590dc5efc` |
| `GLS_EXPORT` | GLS Export (doručení do zahraničí) | `bbbe7223-4ea8-11ec-a39f-002590dc5efc` |
| `OSOBAK` | Osobní odběr v Dobrušce - po dohodě | `389ce19e-40f1-11ea-beb1-002590dad85e` |

> **Note:** Wholesale has no chlazený or SK variants — those are retail-only.

**Payment method used for seeding:**

| Method name | GUID |
|---|---|
| Platba převodem | `6f2c8e36-3faf-11e2-a723-705ab6a2ba75` |

These values are stored in `~/.microsoft/usersecrets/anela-heblo-adapters-shoptet-tests/secrets.json` under `Shoptet:ShippingGuidMap:21`, `Shoptet:ShippingGuidMap:6`, and `Shoptet:PaymentMethodGuid`.

---

## 8. Stock Endpoints

Stock movements are used to update product quantities in Shoptet (stock-up and stock-down).

### 8.1 Authentication
Same `Shoptet-Private-API-Token` header as all other endpoints. No separate token.

### 8.2 List Stocks
```
GET /api/stocks
```
Returns all warehouses. Response includes `defaultStockId` (integer). Most single-warehouse
Shoptet stores have exactly one stock. Use this endpoint once to discover the `StockId` value
to configure in `Shoptet:StockId`.

### 8.3 Update Stock Quantity
```
PATCH /api/stocks/{stockId}/movements
Content-Type: application/json

{
  "data": [
    { "productCode": "AKL001", "amountChange": 5 }
  ]
}
```

- `stockId` — warehouse ID (configure via `Shoptet:StockId`, discover via `GET /api/stocks`)
- `productCode` — variant-level SKU (same as stored in `StockUpOperation.ProductCode`)
- `amountChange` — relative delta; positive = stock-up, negative = stock-down (ingredient consumption)
- Up to 300 products per call (this project sends one product per call)

**Partial failure semantics:** Shoptet returns `200 OK` even when one or more products fail;
the `errors[]` array will be non-empty. If all products fail, Shoptet returns `400 Bad Request`.
Always check `errors[]` even on 200. `ShoptetStockClient` throws `HttpRequestException` for
either case.

**No document number field.** `additionalProperties: false` on the request body — no `documentNumber`,
`note`, or `reference` fields are accepted. Movements appear in Shoptet admin with
`changedBy = "api.service-{id}@{domain}"`. Traceability is maintained in Heblo's
`StockUpOperation` table via `DocumentNumber` (BOX-/GPM-/GPD- prefix).

### 8.4 Configuration Keys

| Key | Type | Where to set |
|-----|------|-------------|
| `Shoptet:StockId` | `int` | User secrets / Azure App Service env var |

Default value is `1`. To find the correct value per environment:
```
GET /api/stocks
Authorization: see section 2
```
The response `data.defaultStockId` is the value to configure.

### 8.5 Known Constraints
- Cannot update quantities for product sets (dynamically calculated). Returns `stock-change-not-allowed` error.
- No idempotency key — duplicate PATCHes create duplicate movements. Guard in application layer via
  `StockUpOperation` state machine (unique `DocumentNumber` per operation, Submitted → Completed transition).
- `VerifyStockUpExistsAsync` is not implementable via REST (no document-number filter on `GET /api/stocks/{id}/movements`). The pre-check in `StockUpProcessingService` always returns false and is effectively a no-op with the REST adapter.

### 8.6 GET /api/stocks/{stockId}/supplies

Reads the current stock level for one or more variants. Used by StockTaking to read state before setting an absolute quantity.

```
GET /api/stocks/{stockId}/supplies
```

**Query parameters:**

| Parameter | Type | Description |
|---|---|---|
| `code` | string (optional) | Filter by variant code (SKU). Omit to return all. |
| `onlyWithClaim` | bool | When `true`, return only items with a non-zero `claim` (reserved by pending orders). |
| `changedFrom` | ISO 8601 datetime | Return only items changed since this timestamp. |
| `itemsPerPage` | int | Default and maximum: 1000. |
| `page` | int | Default: 1. |

**Response shape:**

```json
{
  "data": {
    "supplies": [
      {
        "productGuid": "...",
        "code": "AKL001",
        "amount": "42.000",
        "claim": "5.000",
        "location": "H6/M11/R13",
        "changeTime": "2026-04-14T10:00:00+02:00"
      }
    ]
  }
}
```

**Field semantics:**
- `amount` — quantity available for ordering (free stock). Returned as a string (decimal), may be `null`.
- `claim` — quantity reserved by pending/open orders. Returned as a string (decimal), may be `null`.
- `location` — warehouse location label (same as `stockLocation` in the orders API).
- `changeTime` — ISO 8601 timestamp of the last stock movement for this variant.

**Use case:** Call `GET /supplies?code={code}` before a StockTaking PATCH to confirm current state and log it for audit purposes.

### 8.7 PATCH /api/stocks/{stockId}/movements — `realStock` field (StockTaking)

The existing PATCH endpoint (section 8.3) also accepts `realStock` as an alternative to `amountChange`:

```json
{
  "data": [
    { "productCode": "AKL001", "realStock": 37 }
  ]
}
```

**`realStock` vs `amountChange`:**

| Field | Semantics | Use case |
|---|---|---|
| `amountChange` | Relative delta (positive = add, negative = subtract) | StockUp — add received goods |
| `realStock` | Absolute physical warehouse count | StockTaking — set exact physical quantity |
| `quantity` | (deprecated alias, avoid) | — |

- When `realStock` is used, Shoptet internally calculates `amount for ordering = realStock - claim`.
- Only one of `amountChange`, `quantity`, or `realStock` should be present per item in the request.
- Use `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]` on the unused fields in the C# DTO so they are omitted from the serialized request body.

### 8.8 StockTaking Migration Note (Playwright → REST API)

StockTaking was migrated from Playwright browser automation to the REST API. The two approaches are semantically equivalent:

**Old approach (Playwright):**
1. Navigate to the Shoptet admin stock page for the variant.
2. Read `freeAmount` and `reservedAmount` from the HTML table.
3. Calculate `setAmount = targetAmount - reservedAmount`.
4. Fill the "free amount" input field with `setAmount` and submit.

**New approach (REST API):**
1. `GET /api/stocks/{stockId}/supplies?code={code}` — read current state (`amount` = free, `claim` = reserved).
2. `PATCH /api/stocks/{stockId}/movements` with `{ "productCode": "{code}", "realStock": targetAmount }`.

**Equivalence:**
- `reservedAmount` (Playwright HTML) == `claim` (REST API `/supplies` response).
- Setting `realStock = targetAmount` via REST produces the same final state as the old UI calculation (`setAmount = targetAmount - reservedAmount` filled into the free-amount field), because Shoptet derives free stock as `realStock - claim` in both cases.

---

## 9. Design Documents

- **ShoptetApi Adapter F1** (ShoptetPay payout downloads): `docs/superpowers/specs/2026-03-24-shoptet-api-adapter-f1-design.md`
- **ShoptetApi Adapter F1 Implementation Plan**: `docs/superpowers/plans/2026-03-24-shoptet-api-f1.md`
- **Test Environment Hydration Design** (issue #444): `docs/superpowers/specs/2026-03-27-shoptet-test-env-hydration-design.md`
- **Test Environment Hydration Implementation Plan**: `docs/superpowers/plans/2026-03-27-shoptet-test-env-hydration.md`

---

## 10. Invoices API

> **Source:** Shoptet OpenAPI spec at `https://api.docs.shoptet.com/_bundle/Shoptet%20API/openapi.json` — probed 2026-04-15 (issue #548).
> Live API not probed (token not available in this environment); all findings are from the official spec.

### 10.1 Endpoints

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/invoices` | List invoices (paginated, max 1000/page) |
| `GET` | `/api/invoices/{code}` | Get single invoice detail |

### 10.2 Authentication

Same as all other Shoptet API endpoints: `Shoptet-Private-API-Token: <token>` header (see §2).

### 10.3 List Invoices — `GET /api/invoices`

#### Request Parameters

| Parameter | Type | Description |
|---|---|---|
| `isValid` | boolean | Filter by validity flag |
| `proformaInvoiceCode` | string | Filter by linked proforma invoice code |
| `creationTimeFrom` | string (ISO 8601) | Filter by creation date (from) |
| `creationTimeTo` | string (ISO 8601) | Filter by creation date (to) |
| `taxDateFrom` | string (date) | Filter by tax date (from) |
| `orderCode` | string | Filter by linked order code |
| `codeFrom` | string | Filter by invoice code range (from) |
| `codeTo` | string | Filter by invoice code range (to) |
| `varSymbol` | number | Filter by variable symbol |
| `itemsPerPage` | integer | Page size. **Max: 1000** (vs. orders which caps at 50) |
| `page` | integer | Page number (1-based) |

#### Response Envelope

```json
{
  "data": {
    "invoices": [ /* array of invoiceListItem */ ],
    "paginator": { /* see §10.5 */ }
  },
  "errors": [ /* see Errors schema */ ],
  "metadata": { "requestId": "..." }
}
```

> **Key:** The array key is `data.invoices` — NOT `data.items` (unlike some other endpoints).

#### Invoice List Item Fields

| Field | Type | Required | Description |
|---|---|---|---|
| `code` | string | ✓ | Invoice code (e.g. `"2018000004"`) |
| `varSymbol` | `number \| null` | ✓ | Variable symbol — **number type, not string!** |
| `isValid` | boolean | ✓ | Whether invoice is valid |
| `proformaInvoiceCodes` | array | ✓ | Linked proforma invoice codes |
| `orderCode` | `string \| null` | ✓ | Linked order code (can contain letters/dashes) |
| `creationTime` | `datetime \| null` | ✓ | Date of issue (ISO 8601) |
| `billCompany` | `string \| null` | ✓ | Billing company name |
| `billFullName` | `string \| null` | ✓ | Billing full name |
| `price` | object | ✓ | Price summary (see §10.4 for full structure) |
| `changeTime` | `datetime \| null` | — | Date last modified (ISO 8601) |
| `dueDate` | `date \| null` | — | Due date (ISO 8601 date, no time) |
| `taxDate` | `date \| null` | — | Tax date (ISO 8601 date, no time) |

### 10.4 Invoice Detail — `GET /api/invoices/{code}`

#### Path Parameter

| Parameter | Type | Description |
|---|---|---|
| `code` | string | Invoice code (e.g. `2018000004`) |

#### Include Sections (on-demand)

| `include` value | Section |
|---|---|
| `surchargeParameters` | Item surcharge parameters |

#### Response Envelope

```json
{
  "data": {
    "invoice": { /* invoice object */ }
  },
  "errors": [ /* ... */ ],
  "metadata": { "requestId": "..." }
}
```

#### Invoice Object — Top-level Fields

| Field | Type | Required | Description |
|---|---|---|---|
| `code` | string | ✓ | Invoice code |
| `isValid` | boolean | ✓ | Invoice is valid |
| `proformaInvoiceCodes` | array | ✓ | Linked proforma invoice codes |
| `orderCode` | `string \| null` | ✓ | Linked order code |
| `creationTime` | `datetime \| null` | ✓ | Date of issue (ISO 8601) |
| `changeTime` | `datetime \| null` | ✓ | Date last modified (ISO 8601) |
| `dueDate` | `date \| null` | ✓ | Due date |
| `taxDate` | `date \| null` | ✓ | Tax date |
| `varSymbol` | number | ✓ | Variable symbol (numeric) |
| `constSymbol` | `string \| null` | ✓ | Constant symbol |
| `specSymbol` | `number \| null` | ✓ | Specific symbol |
| `weight` | number | ✓ | Unpacked weight in kg (3 decimal places) |
| `completePackageWeight` | number | ✓ | Total package weight in kg (3 decimal places) |
| `billingMethod` | `object \| null` | ✓ | Payment method info (see §10.6) |
| `billingAddress` | object | ✓ | Billing address (see §10.7) |
| `deliveryAddress` | `object \| null` | ✓ | Delivery address (see §10.8) |
| `addressesEqual` | boolean | ✓ | Billing and delivery addresses are the same |
| `price` | object | ✓ | Price summary (see §10.9) |
| `customer` | object | ✓ | Customer info (see §10.10) |
| `eshop` | object | ✓ | E-shop bank details (see §10.11) |
| `items` | array | ✓ | Line items (see §10.12) |
| `documentRemark` | `string \| null` | ✓ | Document remark |
| `vatPayer` | boolean | ✓ | E-shop is a VAT payer |
| `vatMode` | `string \| null` | — | VAT mode (see §10.13) |
| `proofPayments` | array | — | Linked proof payments |

### 10.5 Paginator

```json
{
  "totalCount": 55,
  "page": 1,
  "pageCount": 3,
  "itemsOnPage": 20,
  "itemsPerPage": 20
}
```

### 10.6 Billing Method Object

```json
{ "id": 3, "name": "Cash" }
```

#### Billing Method Code List

| id | Description (EN) | CZ name |
|---|---|---|
| 1 | COD | Dobírka |
| 2 | Wire transfer | Převodem |
| 3 | Cash | Hotově |
| 4 | Card | Kartou |

### 10.7 Billing Address Fields

| Field | Type | Description |
|---|---|---|
| `company` | `string \| null` | Company name |
| `fullName` | `string \| null` | Full name |
| `street` | `string \| null` | Street name |
| `houseNumber` | `string \| null` | House/street number |
| `city` | `string \| null` | City |
| `district` | `string \| null` | County/district |
| `additional` | `string \| null` | Additional address info |
| `zip` | `string \| null` | ZIP/postal code |
| `countryCode` | `string \| null` | 3-character ISO country code (ISO 4217 in spec, but likely ISO 3166-1 alpha-2 in practice, e.g. `"CZ"`) |
| `regionName` | `string \| null` | Region name |
| `regionShortcut` | `string \| null` | Region abbreviation |
| `companyId` | `string \| null` | Company registration number (IČO) |
| `taxId` | `string \| null` | Tax ID (for CZ, same as vatId) |
| `vatId` | `string \| null` | VAT number (e.g. `"CZ289324675"`) |
| `vatIdValidationStatus` | `"unverified" \| "verified" \| "waiting" \| null` | VAT ID verification status |

### 10.8 Delivery Address Fields

Same fields as Billing Address (§10.7) **except** no `companyId`, `taxId`, `vatId`, or `vatIdValidationStatus`.
The delivery address object itself can be `null`.

### 10.9 Invoice Price Object

| Field | Type | Required | Description |
|---|---|---|---|
| `vat` | `string \| null` | ✓ | VAT amount (2 decimal places, e.g. `"42.00"`) |
| `toPay` | `string \| null` | ✓ | Total amount to pay |
| `currencyCode` | string | ✓ | ISO 4217 currency code (e.g. `"CZK"`) |
| `withoutVat` | `string \| null` | ✓ | Price excl. VAT |
| `withVat` | `string \| null` | ✓ | Price incl. VAT |
| `exchangeRate` | number | ✓ | Exchange rate |
| `invoicingExchangeRate` | number | — | Invoicing-specific exchange rate |
| `partialPaymentAmount` | `string \| null` | — | Partial payment value (% or money) |
| `partialPaymentType` | string | — | `"percents"` or `"absolute"` |

> Price fields are returned as **strings with 2 decimal places** (e.g. `"242.00"`), not numbers.

### 10.10 Customer Object

| Field | Type | Description |
|---|---|---|
| `guid` | `string \| null` | Customer GUID |
| `phone` | `string \| null` | Phone number |
| `email` | `string \| null` | Email address |
| `remark` | `string \| null` | Customer remark |

### 10.11 E-shop Object

| Field | Type | Description |
|---|---|---|
| `bankAccount` | `string \| null` | Bank account number (e.g. `"123456789/1234"`) |
| `iban` | `string \| null` | IBAN |
| `bic` | `string \| null` | SWIFT/BIC code |
| `vatPayer` | boolean | E-shop is a VAT payer |

### 10.12 Invoice Item Fields

| Field | Type | Required | Description |
|---|---|---|---|
| `productGuid` | `string \| null` | ✓ | Product GUID |
| `itemId` | integer | ✓ | Item identifier |
| `code` | `string \| null` | ✓ | Variant/product code |
| `itemType` | string | ✓ | Item type (e.g. `"product"`) |
| `name` | `string \| null` | ✓ | Product name |
| `variantName` | `string \| null` | ✓ | Variant name |
| `brand` | `string \| null` | ✓ | Brand/manufacturer |
| `amount` | `string \| null` | ✓ | Quantity (decimal string) |
| `amountUnit` | `string \| null` | ✓ | Unit (e.g. `"ks"`) |
| `remark` | `string \| null` | ✓ | Line remark |
| `priceRatio` | number | ✓ | Discount ratio (e.g. `0.78` = 22% discount; `1.0` = no discount) |
| `weight` | `number \| null` | ✓ | Weight in kg |
| `additionalField` | `string \| null` | ✓ | Additional info |
| `itemPrice` | object | ✓ | Line total price (see §10.12.1) |
| `unitPrice` | object | ✓ | Unit price (see §10.12.1) |
| `purchasePrice` | `object \| null` | ✓ | Purchase price (see §10.12.1) |
| `displayPrices` | array | — | Presentation-form prices (for admin/PDF display) |
| `recyclingFee` | object | — | Recycling fee info |
| `surchargeParameters` | object | — | Surcharge parameters (on-demand via `include`) |
| `consumptionTax` | object | ✓ | Consumption tax details |
| `itemPriceVatBreakdown` | `array \| null` | — | VAT breakdown for non-product items |

#### 10.12.1 Item Price Sub-Object

| Field | Type | Required | Description |
|---|---|---|---|
| `withVat` | `string \| null` | ✓ | Price incl. VAT |
| `withoutVat` | `string \| null` | ✓ | Price excl. VAT |
| `vat` | `string \| null` | ✓ | VAT amount |
| `vatRate` | number | ✓ | VAT rate (%) |

> **Display vs. API items:** For invoices with coupon/volume discounts across multiple VAT rates, the API returns a single discount row while the admin/PDF shows multiple rows (one per VAT rate). Use `displayPrices` array on each item to get the printout representation.

> **Observed on invoice 126000039 (per-line priceRatio discount):** The discount on this invoice is encoded as `priceRatio=0.0000` on the product line (`itemType=product`, `code=TON002030`). `itemPrice.withVat` reflects the discounted total (`0.00`), while `unitPrice.withVat` retains the original unit price (`180.00`). There is NO separate `discount-coupon` or `volume-discount` aggregate row — the discount is applied entirely per-line via `priceRatio`. The shipping (`itemType=shipping`) and billing (`itemType=billing`) rows have `priceRatio=1.0000` and are not discounted. Invoice totals: `price.withVat=158.00`, `price.toPay=158.00`.

### 10.13 VAT Modes

| Value |
|---|
| `Normal` |
| `One Stop Shop` |
| `Mini One Stop Shop` |
| `Reverse charge` |
| `Outside the EU` |

(Can also be `null`.)

### 10.14 Notable Differences vs. Orders API

| Aspect | Orders API | Invoices API |
|---|---|---|
| Max `itemsPerPage` | 100 | **1000** |
| List array key | `data.orders` | `data.invoices` |
| `varSymbol` type | string | **number** |
| Price fields type | string (decimal) | string (decimal) |
| Shipping field | Present on order | Not present on invoice |
