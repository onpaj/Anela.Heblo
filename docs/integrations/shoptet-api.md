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

- `status` — filter by status id
- `transport` — filter by shipping method
- `payment` — filter by payment method
- Date range filters
- `code` — order code
- `page`, `itemsPerPage` — pagination; **max `itemsPerPage` is 50** (not 100; passing 100 is rejected)

Optional `include` sections: `notes`, `images`, `shippingDetails`, `stockLocation`, `surchargeParameters`, `productFlags`

**Fields in list response:** `code`, `guid`, `creationTime`, `changeTime`, `company`, `fullName`, `email`, `phone`, `remark`, `cashDeskOrder`, `customerGuid`, `paid`, `status`, `source`, `price`, `paymentMethod`, `shipping`, `adminUrl`, `salesChannelGuid`. **`externalCode` is NOT included.** Use `GET /api/orders/{code}` (single detail) to get `externalCode`.

---

## 4. ShoptetPay API

Base URL: `https://api.shoptetpay.com`

### 4.1 Payout Reports

| Method | Path | Description |
|---|---|---|
| `GET` | `/v1/reports/payout` | List payout reports (filter: `dateFrom`, `dateTo`, `types`, `limit`) |
| `GET` | `/v1/reports/payout/{id}/abo` | Download payout report as ABO file |

**Date format:** `yyyy-MM-dd` (not ISO 8601 round-trip).

**PayoutReportDto fields:** `id`, `currency`, `type`, `serialNumber`, `dateFrom`, `dateTo`, `createdAt`

---

## 5. Existing Project Integration Points

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

## 6. Test Environment Notes

- **No official Shoptet sandbox.** Test env is a real Shoptet store configured with test credentials.
- **`suppress*` flags are NOT supported by the REST API.** `suppressEmailSending`, `suppressStockMovements`, `suppressDocumentGeneration`, `suppressProductChecking` only exist in the Shoptet admin UI import flow — the REST `POST /api/orders` endpoint rejects them with 422. Do not include them in the request body.
- `shippingGuid` and `paymentMethodGuid` values are **store-specific** — must be discovered at runtime via `GET /api/eshop?include=shippingMethods,paymentMethods` or configured per environment.
- Orders created for testing should use a recognizable `externalCode` prefix (e.g. `TEST-`) to allow cleanup.

---

### Test Environment Hydration (issue #444)

- **Seeding endpoint:** `POST /api/orders` — creates orders with minimal valid payload.
- **Status update endpoint:** `PATCH /api/orders/{code}/status` — body shape `{"data":{"statusId":<int>}}`. Note: the property is `statusId` (flat integer), NOT `{"status":{"id":x}}` — verified against Shoptet OpenAPI spec (`additionalProperties: false` schema).
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

**Known mappings — production store (780175.myshoptet.com / Anela.cz):**

| Numeric ID | Constant | Method name | GUID |
|---|---|---|---|
| 21 | `ZASILKOVNA_DO_RUKY` | Zásilkovna (do ruky) | `f6610d4d-578d-11e9-beb1-002590dad85e` |
| 6 | `PPL_DO_RUKY` | PPL (do ruky) | `2ec88ea7-3fb0-11e2-a723-705ab6a2ba75` |

**Payment method used for seeding:**

| Method name | GUID |
|---|---|
| Platba převodem | `6f2c8e36-3faf-11e2-a723-705ab6a2ba75` |

These values are stored in `~/.microsoft/usersecrets/anela-heblo-adapters-shoptet-tests/secrets.json` under `Shoptet:ShippingGuidMap:21`, `Shoptet:ShippingGuidMap:6`, and `Shoptet:PaymentMethodGuid`.

---

## 7. Design Documents

- **ShoptetApi Adapter F1** (ShoptetPay payout downloads): `docs/superpowers/specs/2026-03-24-shoptet-api-adapter-f1-design.md`
- **ShoptetApi Adapter F1 Implementation Plan**: `docs/superpowers/plans/2026-03-24-shoptet-api-f1.md`
- **Test Environment Hydration Design** (issue #444): `docs/superpowers/specs/2026-03-27-shoptet-test-env-hydration-design.md`
- **Test Environment Hydration Implementation Plan**: `docs/superpowers/plans/2026-03-27-shoptet-test-env-hydration.md`
