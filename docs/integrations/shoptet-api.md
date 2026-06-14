# Shoptet API — Integration Findings

> **Living document.** Every new finding about the Shoptet API MUST be added here before it is used elsewhere.
> API reference: https://api.docs.shoptet.com/shoptet-api/openapi
> OpenAPI spec: https://api.docs.shoptet.com/_bundle/Shoptet%20API/openapi.json

---

## 1. Overview

Shoptet is an e-commerce platform that exposes a REST API at `https://api.myshoptet.com`. The project uses the REST adapter `Anela.Heblo.Adapters.ShoptetApi` for all Shoptet operations (invoices, stock, expedition, orders, ShoptetPay payouts).

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

These are the numeric `shippingId` values used by `ShoptetApiExpeditionListSource` for the picking list scenario. The same IDs must be used when seeding test orders so the picking list can find them via `?f[shippingId]={id}`.

Source of truth: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiExpeditionListSource.cs`

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

**2025+ carrier scheme** (added when PPL/Zásilkovna/GLS introduced new "box & výdejní místa / do ruky" methods on anela.cz; GUIDs `*-11f1-9239-bc241122355e`). These coexist with the legacy methods above — both still receive orders during the transition. GUIDs discovered via `GET /api/eshop?include=shippingMethods` (production anela.cz, `retail` group); numeric IDs supplied from Shoptet admin (not returned by the REST API).

| ID | Constant Name | Carrier | DisplayName | GUID |
|---|---|---|---|---|
| 490 | `PPL_BOX` | PPL | PPL přímo do PPL boxu | `6fc70492-6341-11f1-9239-bc241122355e` |
| 496 | `PPL_VYDEJNI_MISTA` | PPL | PPL výdejní místa a Alzaboxy | `8e6313c7-6342-11f1-9239-bc241122355e` |
| 493 | `PPL_DO_RUKY_NEW` | PPL | PPL do ruky | `53f8a8a5-6342-11f1-9239-bc241122355e` |
| 502 | `ZASILKOVNA_BOXY_VYDEJNI` | Zásilkovna | Zásilkovna boxy a výdejní místa | `68201aa2-6343-11f1-9239-bc241122355e` |
| 505 | `ZASILKOVNA_DO_RUKY_NEW` | Zásilkovna | Zásilkovna do ruky | `b1915a68-6343-11f1-9239-bc241122355e` |
| 511 | `GLS_BOXY_VYDEJNI` | GLS | GLS boxy a výdejní místa | `8448f4b6-6344-11f1-9239-bc241122355e` |
| 508 | `GLS_DO_RUKY_NEW` | GLS | GLS do ruky | `53db451b-6344-11f1-9239-bc241122355e` |

> `ResolveDeliveryHandling` classifies these by `Name`: `*_DO_RUKY*` → `NaRuky`; names containing `BOX` or `VYDEJNI` → `Box`. The legacy `7878c138-…` method (`ZASILKOVNA_ZPOINT`, id 15) was renamed in Shoptet to "Zásilkovna – Výdejní místa a Z-boxy" — `DisplayName` updated to match.
> ⚠️ The ID↔method pairing within each carrier was assigned from the order the methods appear in the `retail` response; confirm against Shoptet admin before relying on the picking-list filter.

**Important:** These are Shoptet admin-internal numeric IDs used in URL filter params (`?f[shippingId]=21`), not the `shippingGuid` used in the REST API order creation body. When seeding test orders via `POST /api/orders`, use the corresponding `shippingGuid` from `GET /api/eshop?include=shippingMethods` that maps to the desired shipping ID.

#### Order Statuses (known values from code)

Defined in `PrintPickingListOptions` and `ShoptetApiExpeditionListSource`:

| ID | Name |
|---|---|
| -2 | Vyřizuje se (Processing) — source state for picking list |
| 26 | Balí se (Packing) — PACK seed state; `PrintPickingListOptions.DesiredStateId` |
| 70 | Předáno přepravci (Handed to carrier) — EXP seed state |
| 73 | Oprava-robot — Fix source state (`FixSourceStateId`) |

> **Note:** Status 55 ("K Expedici") referenced in `ShoptetApiExpeditionListSource.DesiredStateId` does **not exist** in the store (confirmed via `GET /api/eshop?include=orderStatuses`). Seeding EXP-category orders uses status 70 instead.
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

**`shipping` object on `GET /api/orders/{code}`** — the single-order detail base response (no `include` needed) returns a `shipping` object with the same shape as the list endpoint, exposing `shipping.guid` and `shipping.name`. The Balení packing flow (`GET /api/orders/{code}?include=stockLocation,notes`) relies on these two fields to resolve the order's shipping method and derive carrier cooling.

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

**✅ Verified 2026-05-25 against test store (Shoptet token prefix 780175):**
- PATCH `additionalFields: [{ "index": 1, "text": "CHLAZENE-TEST" }]` → 200 `{"data":null,"errors":null}` _(verified with index 1; index 6 uses the same API path — round-trip confirmed)_
- GET `/api/orders/{code}?include=notes` → field round-trips correctly
- Production uses **index 6** (index 1 is reserved by an external system):
  ```json
  {
    "data": {
      "additionalFields": [{ "index": 6, "text": "CHLAZENE" }]
    }
  }
  ```

### 3.7 Heblo reservations for the 6 per-order additional fields

| Index | Reserved by | Value contract | Written when | Cleared when | Reader(s) | Limits |
|---|---|---|---|---|---|---|
| 1 | **RESERVED — external system** | Do not use; already claimed by a system outside Heblo | — | — | External system (unknown) | ≤ 255 chars |
| 2 | — unassigned — | | | | | ≤ 255 chars |
| 3 | — unassigned — | | | | | ≤ 255 chars |
| 4 | — unassigned — | | | | | length undocumented |
| 5 | — unassigned — | | | | | length undocumented |
| 6 | Expedition cooling marker | Literal string `"CHLAZENE"` for cooled orders; no other value ever written | Original expedition list print, if `ExpeditionOrder.IsCooled == true` | Never (write-only) | External / Shoptet operators (informational; nothing in Heblo reads it back) | length undocumented (we use 8 chars) |

**Before using an additional field in a new feature, claim it by updating this table in the same PR.** The fields are a finite shared resource (6 total) and the Shoptet API gives no per-field semantic protection — two callers writing to the same index will silently overwrite each other. The Heblo expectation is: one logical owner per index, documented here.

The Heblo client (`ShoptetOrderClient.SetAdditionalFieldAsync`) accepts any 1..6 index; there is no runtime guard tying an index to a feature. The guard is this table and code review.

Length limits: indices 1–3 are capped at 255 chars by the Shoptet API. Indices 4–6 are believed to support longer text but the exact cap has not been verified — measure before assuming.

---

## 4. Customers API

> **TODO: Verify against live API.** Run `curl -H "Shoptet-Private-API-Token: <token>" https://api.myshoptet.com/api/customers/<guid>` with a real customer GUID and document the actual response shape here. Update `ShoptetCustomerResponse.cs` field names if they differ from the speculative model.

### 4.1 Endpoints

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/customers/{guid}` | Get customer by GUID |

### 4.2 Known fields (speculative — verify before production)

Response shape assumed to follow Shoptet conventions:
- `data.customer.guid` — customer GUID
- `data.customer.email` — customer email
- `data.customer.fullName` — full name
- `data.customer.customerGroup.name` — customer group name
- `data.customer.priceList.name` — price list name
- `data.customer.billingAddress` — billing address with `street`, `city`, `zip`, `countryCode`

---

## 5. Products API

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

### 4.4 Stock CSV export — resilience characteristics

The stock CSV export URL is configured via `StockClient:Url` and **is not** on `api.myshoptet.com` — it is the per-store CSV export host (e.g. `https://<store>.myshoptet.com/action/...`). Two consequences:

- The dependency tracker (which targets `api.myshoptet.com`) does **not** record these calls. Failures must be queried by exception name (`System.Net.Http.HttpRequestException` with `outerMethod contains "ShoptetStockClient.ListAsync"`).
- The URL contains an access token as a query parameter (e.g. `?token=...` / `?hash=...`). Logging code redacts `token`, `hash`, `key`, `apiToken`, `access_token` keys to `***`.

**Encoding:** `windows-1250`. **Delimiter:** `;`. Parsed via `CsvHelper` with `StockDataMap`.

**Observed transient failure rate (baseline):** ~1.1 `HttpRequestException` / day across all callers (telemetry window 2026-06-05 → 2026-06-12).

**Resilience policy (HTTP layer, registered against the named HttpClient `"ShoptetStockCsv"`):**

| Property | Value | Configurable via |
|---|---|---|
| Per-attempt timeout | 8s (default) | `StockClient:TimeoutSeconds` |
| Max retry attempts | 3 (default) | `StockClient:MaxRetryAttempts` |
| Retry base delay | 1s exponential + jitter | `StockClient:RetryBaseDelaySeconds` |
| Retry triggers | `HttpRequestException`, 5xx, 408, 429, `TimeoutRejectedException`, `OperationCanceledException` (only when caller's token has **not** requested cancellation) | — |
| Outer `HttpClient.Timeout` | `TimeoutSeconds × MaxRetryAttempts + 5` | derived |

Worst-case wall clock with defaults: ≈ 8 + 1 + 8 + 2 + 8 + 4 + 8 ≈ 39 s — but `CatalogDataRefreshService` invocations are wrapped by `CatalogResilienceService` whose 30 s pipeline timeout will surface first. Tune `TimeoutSeconds` down if the outer pipeline still aborts retries; raise it for ad-hoc callers that do not use the outer pipeline.

**Caller-side wrapping:** Both `CatalogDataRefreshService.RefreshEshopStockData` and `ProductPairingDqtComparer.CompareAsync` wrap `ListAsync` with `ICatalogResilienceService` for circuit-breaker + outer-timeout semantics. New callers must follow the same pattern.

---

## 6. ShoptetPay API

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

### 5.2 Existing Integration Tests
- `ShoptetStockClientIntegrationTests` — validates CSV stock export parsing
- `ShoptetPriceClientIntegrationTests` — validates price client
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

Note: the response **does not include numeric IDs** — match by method name against the constants in `ShoptetApiExpeditionListSource.cs`.

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

> **`withVat` vs `toPay` for DQT:** Czech invoices can include a "Zaokrouhlení" (rounding) line
> that rounds the total to the nearest full crown. When this occurs, `withVat` holds the
> pre-rounding sum (e.g. `"14321.50"`) and `toPay` holds the post-rounding "Částka k úhradě"
> (e.g. `"14322.00"`). Flexi's `sumCelkem` equals `toPay`. For DQT `TotalWithVat` comparison,
> use `toPay` — the mapper applies it automatically when `|toPay − withVat| < 1 Kč`.
> When they match (no rounding), `withVat == toPay`.

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

#### 10.12.2 Discount handling in Heblo invoice import

Heblo's `ShoptetInvoiceMapper` folds every discount into each product line's
post-discount `PricePerUnit` so the Flexi/Abra mapping (`PricePerUnit = ItemPrice.WithoutVat`)
carries the discount through unchanged. Concretely:

1. Per-line `priceRatio < 1` (including `0.0` = 100% free) → multiply
   `unitPrice.{withVat, withoutVat, vat}` by `priceRatio`.
   Note: Shoptet sends `priceRatio` as a **quoted string** (e.g. `"0.0000"`), not a number.
2. Aggregate rows with `itemType ∈ { discount-coupon, volume-discount, gift }` →
   distribute their `itemPrice.withoutVat` / `itemPrice.withVat` across product lines
   proportionally to each product line's pre-discount `TotalWithoutVat`, then drop the
   aggregate row.

There is no separate discount line in the resulting Flexi invoice. Visibility of the
discount source is preserved only in the original Shoptet invoice and the Heblo import logs.

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

---

## Product Export Download

**Endpoint:** Shoptet CSV export host (full URL stored in `ProductExportOptions.Url` / `appsettings` — do NOT paste URLs with embedded tokens here).

**Method:** `GET` (with a best-effort `HEAD` probe before the GET to estimate content size).

**Frequency:** Once per day at 02:00 UTC, scheduled by `ProductExportDownloadJob` (Hangfire recurring job).

**Observed behaviour (captured from staging — run the commands below if values are stale):**

To refresh these values from staging, run:
```bash
curl -sS -I --max-time 30 "$PRODUCT_EXPORT_URL"
curl -sS -o /dev/null -w "time_total=%{time_total}s\nhttp_code=%{http_code}\nsize_download=%{size_download}\n" --max-time 600 "$PRODUCT_EXPORT_URL"
echo | openssl s_client -servername "$EXPORT_HOST" -connect "$EXPORT_HOST:443" 2>/dev/null | openssl x509 -noout -dates -subject
```

- HTTP status: Not yet observed from staging — update after first successful run post-deploy.
- `Content-Type`: Not yet observed — expected `text/csv` or `application/octet-stream`.
- `Content-Length`: Not yet observed — HEAD probe result will populate `FileSizeBytes` in telemetry.
- Wall-clock latency for full GET: Not yet observed — check `ElapsedMs` in Application Insights `ProductExportDownload` business event.
- TLS certificate: Not yet observed — run the openssl command above from staging.

**Quirks / gotchas:**
- HEAD support: Unknown until tested. If HEAD returns 405 or times out, the HEAD probe swallows the error and continues with `FileSizeBytes = 0`; this is expected and logged at `Debug` level.
- Token in URL: The export URL likely contains a query-string token (`?token=…` or `?sig=…`). All failure telemetry redacts the query string (replaced with nothing — only the path is logged). Do NOT paste the full URL with token anywhere in this doc.
- Shoptet has no sandbox — the export URL hits the live store. Avoid testing against production outside of normal scheduled windows.

**Resilience configuration:** Per-attempt timeout 120 s, 3 Polly retries with exponential + jitter backoff (base 2 s). Hangfire auto-retry is disabled on this job (`[AutomaticRetry(Attempts = 0)]`); all retry logic lives in Polly only. See `ProductExportOptions` for tunables (`HeadTimeout`, `DownloadTimeout`, `MaxRetryAttempts`, `RetryBaseDelay`).

**Related code:** `ProductExportDownloadJob`, `DownloadFromUrlHandler`, `DownloadResilienceService`, `AzureBlobStorageService.DownloadFromUrlAsync`.

---

## 11. Delivery API

### 11.1 Shipments Endpoint

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/shipments` | List shipments (filter by `orderCode`) |
| `POST` | `/api/shipments` | Create a shipment for an order |
| `GET` | `/api/shipments/order/{code}/shipping-options` | Get available carrier options for an order |

### 11.2 Filtering

Pass `orderCode` as a query parameter to retrieve shipments for a specific order:

```
GET /api/shipments?orderCode={orderCode}
```

Optional `status` filter also available (not used in this integration).

### 11.3 Response Envelope

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

### 11.4 Error Envelope

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

### 11.5 Labels

- `labelUrl` — PDF download URL, may be `null` if the label has not been generated yet.
- `labelZpl` — Raw Zebra ZPL string for direct USB printing, may be `null`.
- An order may have multiple shipments, each with multiple packages. All are returned; the kiosk prints each.
- If both `labelUrl` and `labelZpl` are `null` for all packages, labels have not been generated yet.

### 11.5.1 Package `name` is NOT a stable match key — and shipments accumulate per order

> **Verified 2026-06-10 against the live test store (780175), order `126000035`.** A GET returned **12 shipments** for the single order.

Hard-won findings from the FillTrackingNumbers backfill job — read before matching packages to DB rows:

- **`packages[].name` is non-unique within an order.** Every package across all 12 shipments was named `"Vlastní balení"` ("custom packaging" — the packaging type, not an identifier). Building a `Dictionary` keyed by `name` will throw a duplicate-key exception the moment two non-dead packages share a name.
- **`packages[].name` is NOT stable across label generation.** Immediately after `POST /api/shipments` (status `requested`, label not yet ready) the package name is a placeholder sequence (`"1"`, `"2"`, …). Once the carrier label generates, Shoptet **renames** the package to the packaging type (`"Vlastní balení"`). So a name persisted at scan time will not match the name read back later. **Never match a persisted package to a live label by name.**
- **Shipments accumulate; `ResetOrderShipmentHandler` orphans the DB `ShipmentGuid`.** Each pack/reset cancels the old shipment (status → `canceled`/`deleted`) and creates a brand-new one. The Heblo `Package` row is **not** updated on reset, so its `ShipmentGuid` points at a now-cancelled shipment that still carries an (invalid) tracking number. The live, deliverable tracking number lives on a different, newer shipment.
- **Latest active shipment = the last non-dead shipment in the response.** Shoptet returns shipments **oldest-first** (the `guid` is UUIDv7, time-ordered). "Active" = status not in `{canceled, cancel_requested, deleted, request_failed}`. To resolve the current tracking number for an order, take the **last** active shipment and read its package tracking number — ignore the persisted `ShipmentGuid` and all package names. Implemented as `IShipmentClient.GetLatestActiveTrackingNumberAsync` and used by `FillTrackingNumbersJob`.
- Each Heblo-created shipment has **exactly one package** (`ScanPackingOrderHandler`/`ResetOrderShipmentHandler` always send a single `packages[]` entry), so an order's single tracking number maps to its single `Package` row.

### 11.6 Authentication

Same host (`https://api.myshoptet.com`) and `Shoptet-Private-API-Token` header as all other Shoptet endpoints. `ShoptetApiSettings.BaseUrl` and `ShoptetApiSettings.ApiToken` are reused — no new configuration keys.

### 11.7 Implementation status

- **Backend** (`POST /api/shipment-labels`) — complete. Fetches labels by order code, returns PDF URL + ZPL string per package, maps 29XX error codes for not-found and not-generated cases.
- **UI / Balení module** — not yet implemented. The Balení kiosk PWA needs a new screen that calls this endpoint and sends the ZPL payload to the USB-connected Zebra printer. The cloud backend is data-only — USB hardware access happens entirely on the kiosk device.

---

### 11.8 GET /api/shipments/order/{code}/shipping-options

> **Probed 2026-05-19** against the production API token (store 780175 / anela.cz staging, orders 126000032–126000035).

Returns the available carrier options for a specific order. Must be called before `POST /api/shipments` to obtain the `shippingId` required by the create endpoint.

#### Path parameter

| Parameter | Type | Description |
|---|---|---|
| `code` | string | Shoptet order code (e.g. `126000035`) |

#### Response shape (200)

```json
{
  "data": {
    "shippingOptions": [
      {
        "shippingId": 236806,
        "methodName": "PPL (do ruky)",
        "carrierCode": "ppl-cz",
        "serviceCode": "ppl-cz-private-address",
        "maxShipment": 1,
        "carrierAddresses": [],
        "bankAccounts": [],
        "branch": "balikobot"
      }
    ]
  },
  "errors": null,
  "metadata": {
    "requestId": "..."
  }
}
```

#### Field semantics

| Field | Type | Description |
|---|---|---|
| `shippingId` | integer | Carrier-specific shipment identifier — pass as `shippingId` in `POST /api/shipments`. **Per-order value**, different for each order even for the same shipping method. |
| `methodName` | string | Human-readable shipping method name |
| `carrierCode` | string | Carrier code (e.g. `ppl-cz`, `zasilkovna`) |
| `serviceCode` | string | Carrier service code (e.g. `ppl-cz-private-address`) |
| `maxShipment` | integer | Maximum number of shipments allowed for this order |
| `carrierAddresses` | array | Pickup point / branch addresses (empty for home delivery) |
| `bankAccounts` | array | Carrier bank accounts (required for COD — empty when not applicable) |
| `branch` | string | Integration branch identifier (e.g. `balikobot`) |

#### Observed behaviour

- Returns an empty `shippingOptions: []` when the order's shipping method has no Balikobot carrier integration configured (e.g. GLS, Zásilkovna on this store), or when Balikobot is not set up in the store at all.
- The `shippingId` value is **unique per order** (observed: different integer per test-seed order 126000032–126000035, all PPL do ruky). Do not cache or reuse across orders.
- The `shippingId` is **not** the same as the numeric shipping method ID used in the expedition list URL filter (`?f[shippingId]=6`).

---

### 11.9 POST /api/shipments — Create Shipment

> **Probed 2026-05-19** against the production API token. Schema fully confirmed from OpenAPI spec + 422-error iteration. Successful creation was blocked by the test store having no Balikobot carrier configured (`GET /api/shipments/carriers` returns `[]`) — the request body shape was confirmed up to the carrier-integration boundary (`errorCode: invalid-request-data`, `instance: integration-call`).

Creates a shipment for an order. Triggers Balikobot carrier API call synchronously.

#### Request body

```json
{
  "data": {
    "orderCode": "126000035",
    "shippingId": 236806,
    "note": null,
    "cod": null,
    "addressId": null,
    "bankAccountId": null,
    "packages": [
      {
        "width": 300,
        "height": 200,
        "depth": 150,
        "weight": "0.500"
      }
    ]
  }
}
```

**Required fields:**

| Field | Type | Required | Description |
|---|---|---|---|
| `data` | object | yes | Envelope wrapping all fields (Shoptet standard pattern) |
| `data.orderCode` | string | yes | Shoptet order code |
| `data.shippingId` | integer\|null | yes (if Balikobot) | From `GET /api/shipments/order/{code}/shipping-options` — the `shippingId` field |
| `data.packages` | array | yes (min 1 item) | Package dimensions and weight |

**Optional fields:**

| Field | Type | Description |
|---|---|---|
| `data.note` | string\|null | Shipment note |
| `data.cod` | string\|null | COD override — `null` = use order COD; `"0.00"` = no COD; any other value overrides amount |
| `data.addressId` | integer\|null | From `carrierAddresses[].id` in shipping-options (for pickup points) |
| `data.bankAccountId` | integer\|null | From `bankAccounts[].id` in shipping-options — required when `cod` is sent |

**Package object — three mutually exclusive variants (oneOf):**

*Variant A: by dimensions*
```json
{ "width": 300, "height": 200, "depth": 150, "weight": "0.500" }
```

*Variant B: by packaging type + weight*
```json
{ "packaging": 6, "weight": "0.500" }
```

*Variant C: by orderPackagingId (pre-configured packaging)*
```json
{ "orderPackagingId": 3 }
```

**Weight unit: kilograms (kg).** The `weight` field accepts `string | null | integer`. String format is decimal kg (e.g. `"0.500"`, `"1.00"`). The `typeWeight` schema in the OpenAPI spec confirms: *"weight in kg, unpacked. 3 decimal places."* The GET response `shipmentPackage.weight` examples show `1` (numeric, kg).

**Dimensions** (`width`, `height`, `depth`) are integers in **millimetres** — the OpenAPI spec examples show `10`, `20`, `30`.

#### Response shape (201 Created)

```json
{
  "data": {
    "guid": "1e3f3f32-3f3f-6766-3f3f-02423f1f0004",
    "checkUrls": null
  },
  "errors": null
}
```

| Field | Type | Description |
|---|---|---|
| `data.guid` | string (UUID) | Shipment GUID — use with `GET /api/shipments?orderCode=...` to poll for label readiness |
| `data.checkUrls` | array\|null | URL(s) to poll for async completion — `null` by default; only available if explicitly enabled by Shoptet |

#### Error codes (422)

| errorCode | instance | Meaning |
|---|---|---|
| `invalid-request-data` | `data` | Outer `data` wrapper missing |
| `invalid-request-data` | `data.packages[0]` | Package object doesn't match any variant (oneOf mismatch — check required fields) |
| `invalid-request-data` | `data.packages[0].weight` | Double (float) value sent; weight must be string, null, or integer |
| `invalid-request-data` | `data.packages[0].orderPackagingId` | Required when using variant C |
| `invalid-request-data` | `data.packages[0].packaging` | Must be integer (not a nested object) |
| `invalid-request-data` | `integration-call` | **Carrier (Balikobot) rejected the request.** Shoptet wraps the carrier error with this generic message. Causes include: carrier not configured, invalid address, address validation failure, or missing carrier account. |
| `shipment-validation-failed` | `data.orderCode` | Invalid recipient address (missing phone/email/fullName/street/city/zip), invalid currency, invalid country |
| `shipment-validation-failed` | `data.bankAccountId` | Required when COD is sent |
| `shipment-validation-failed` | `data.cod` | COD exceeds carrier maximum (100 000.00) |

#### Label readiness latency

After a successful `201` response, the shipment is in `requested` status. The label is not immediately available.

**Confirmed workflow:**
1. `POST /api/shipments` → `201` with `guid`
2. Poll `GET /api/shipments?orderCode={code}` — check `items[].packages[].labelUrl != null`
3. When `labelUrl` is non-null, the label is ready for download

**Status lifecycle** (from OpenAPI spec code list):

| Status | Meaning |
|---|---|
| `requested` | Shipment submitted to carrier, label not yet ready |
| `request_failed` | Carrier rejected the request |
| `created` | Label ready (tracking number assigned) |
| `in_transit` | Parcel picked up by carrier |
| `ready_for_pickup` | Available at pickup point |
| `delivered` | Delivered |
| `failed` | Delivery failed |
| `back_transit` | Return in transit |
| `returned` | Returned to sender |
| `cancel_requested` | Cancellation requested |
| `canceled` | Canceled |
| `closed` | Closed |
| `deleted` | Deleted |

**Webhook alternative:** `shipment:create` webhook fires when carrier confirms the shipment (tracking number assigned, label ready). Register via Shoptet webhooks if polling is undesirable.

**Observed latency:** The test store has no Balikobot configured so label-ready latency was not directly measured. For the PPL carrier via Balikobot the expected flow is synchronous within the carrier call — label should be available within a few seconds of the `POST` completing. The `checkUrls` field (when non-null) provides a polling URL for async carriers.

#### Test store limitation

The staging store (780175 / `api.myshoptet.com` with token `Shoptet:ApiToken`) has **no Balikobot carriers configured** (`GET /api/shipments/carriers` returns `[]`). All `POST /api/shipments` calls in the test store will return `errorCode: invalid-request-data, instance: integration-call`. Integration tests for shipment creation must run against the production store or a store with Balikobot configured.

---

### 11.10 POST /api/shipments/{guid}/cancel-request — Cancel Shipment

> **Probed 2026-05-21** against the official OpenAPI spec (`https://api.docs.shoptet.com/_bundle/Shoptet%20API/openapi.json`).

Requests cancellation of an existing shipment. Forwarded to the carrier asynchronously — the API accepts the request immediately and the carrier processes the actual cancellation in the background.

> **⚠️ Common mistake — there is NO `DELETE /api/shipments/{guid}` endpoint.** Calling `DELETE` returns `404` / `405`. The only documented cancellation mechanism is this `POST .../cancel-request`. Anela hit this bug prior to 2026-05-21 in `ShoptetShipmentClient.DeleteShipmentAsync` — `ResetOrderShipmentHandler` consistently failed with `ShipmentDeleteFailed` because the underlying DELETE never succeeded.

#### Path parameter

| Parameter | Type | Description |
|---|---|---|
| `guid` | string (UUID) | Shipment GUID returned by `POST /api/shipments` |

#### Request body

**None.** No body, no query params.

#### Response (202 Accepted)

Empty body. Cancellation has been accepted for forwarding to the carrier — it is NOT yet final.

#### Error codes

| HTTP | errorCode | instance | Meaning |
|---|---|---|---|
| 404 | `shipment-not-found` | `payload` | Shipment GUID does not exist (already removed / never created). Treat as idempotent success. |
| 422 | `invalid-request-data` | `integration-call` | Carrier rejected the cancellation request (e.g. shipment already in transit). |

#### Final-state semantics

`202` does NOT mean the shipment is cancelled — only that the request was accepted. Poll `GET /api/shipments/{guid}` to observe the actual lifecycle:

```
requested → cancel_requested → canceled → deleted
created   → cancel_requested → canceled
in_transit → (cancellation may be rejected by carrier with 422)
```

#### Anela usage pattern (Balení reset flow)

`ResetOrderShipmentHandler` fires `POST .../cancel-request` and **immediately** calls `POST /api/shipments` to create a replacement — it does **not** poll for the cancel to complete. Trade-off:

- Operator experience is fast (single confirm-then-print cycle on the kiosk).
- The old shipment may briefly co-exist with the new one in `requested` state at the carrier.
- A `404` from cancel-request is treated as success — the shipment is already gone, no further action needed.

`ShoptetShipmentClient.CancelShipmentAsync` returns silently on `404` and throws `HttpRequestException` on any other non-2xx status.
