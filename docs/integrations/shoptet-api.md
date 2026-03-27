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

**Optional suppress flags (critical for test seeding):**
```json
{
  "suppressProductChecking": true,
  "suppressStockMovements": true,
  "suppressDocumentGeneration": true,
  "suppressEmailSending": true
}
```

**Address fields** (both `billingAddress` and `deliveryAddress`):
`company`, `fullName`, `street`, `houseNumber`, `city`, `district`, `additional`, `zip`, `regionName`, `regionShortcut`, `companyId`, `vatId`, `taxId` — all max 255 chars.

**Item fields:**
```json
{
  "itemType": "product | billing | shipping | discount-coupon | volume-discount | gift | gift-certificate | generic-item | product-set | product-set-item | deposit",
  "code": "string (max 64)",
  "name": "string (max 250)",
  "variantName": "string (max 128)",
  "vatRate": 21,
  "itemPriceWithVat": 100.0,
  "quantity": 1
}
```

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
| 26 | Balí se (Packing) |
| 55 | K Expedici (Ready to ship) — desired state after picking |
| 73 | Fix source state (`FixSourceStateId`) |

### 3.5 GET /api/orders — Filtering Parameters

- `status` — filter by status id
- `transport` — filter by shipping method
- `payment` — filter by payment method
- Date range filters
- `code` — order code

Optional `include` sections: `notes`, `images`, `shippingDetails`, `stockLocation`, `surchargeParameters`, `productFlags`

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
- `suppressEmailSending=true`, `suppressStockMovements=true`, `suppressDocumentGeneration=true` MUST be used when creating test orders to avoid side effects.
- `shippingGuid` and `paymentMethodGuid` values are **store-specific** — must be discovered at runtime via `GET /api/eshop?include=shippingMethods,paymentMethods` or configured per environment.
- Orders created for testing should use a recognizable `externalCode` prefix (e.g. `TEST-`) to allow cleanup.

---

## 7. Design Documents

- **ShoptetApi Adapter F1** (ShoptetPay payout downloads): `docs/superpowers/specs/2026-03-24-shoptet-api-adapter-f1-design.md`
- **ShoptetApi Adapter F1 Implementation Plan**: `docs/superpowers/plans/2026-03-24-shoptet-api-f1.md`
