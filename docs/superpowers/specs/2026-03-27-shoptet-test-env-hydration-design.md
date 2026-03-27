# Design: Shoptet Test Environment Hydration

**Date:** 2026-03-27
**Scope:** Integration test that seeds a Shoptet test store with known orders in defined states, enabling deterministic picking-list and expedition-list tests.

---

## 1. Goal

Populate the Shoptet test store with a fixed set of orders in specific states so that integration and E2E tests that depend on picking-list behavior have reliable, reproducible data. The seeder must be idempotent (safe to re-run) and able to reset orders back to their target state when tests mutate them.

---

## 2. Constraints

- Must **never** run against the production store.
- Lives in the existing integration test project `Anela.Heblo.Adapters.Shoptet.Tests`.
- Uses the Shoptet REST API (`Anela.Heblo.Adapters.ShoptetApi`) — not the Playwright adapter.
- All created orders must be identifiable for cleanup via a stable `externalCode` prefix.

---

## 3. Guard Mechanism

Two guards run before any API call. Either failure aborts immediately with a descriptive message.

### Guard 1 — IsTestEnvironment flag
Config key `Shoptet:IsTestEnvironment` must be `true`.

Failure message:
> `"Hydration must not run against live environment. Set Shoptet:IsTestEnvironment=true in test appsettings.json"`

### Guard 2 — URL does not contain "anela"
The configured Shoptet API base URL must not contain the string `"anela"` (case-insensitive).

Failure message:
> `"Hydration refused: base URL contains 'anela' — this looks like the production store."`

Both checks are extracted into a shared `AssertTestEnvironment(config)` helper called at the top of each test method.

---

## 4. Seed Catalog

### 4.1 Shipping methods (representative subset)

| Numeric ID | Name | Carrier |
|---|---|---|
| 21 | `ZASILKOVNA_DO_RUKY` | Zásilkovna |
| 6 | `PPL_DO_RUKY` | PPL |

These IDs match the constants in `ShoptetPlaywrightExpeditionListSource` and are the same IDs used by the picking-list URL filter (`?f[shippingId]={id}`).

### 4.2 Order distribution per shipping

| State ID | State Name | Count | Purpose |
|---|---|---|---|
| `-2` | Vyřizuje se | 9 | Source state for picking list (fills one full page) |
| `55` | K Expedici | 2 | Post-picking state coverage |
| `26` | Balí se | 2 | Mid-state coverage |

**Total: 26 orders** (13 per shipping × 2 shippings).

### 4.3 ExternalCode naming

Pattern: `TEST-{SHIPPING_TAG}-{STATE_TAG}-{INDEX:D2}`

| Shipping | State | Examples |
|---|---|---|
| ZAK-21 | INIT (−2) | `TEST-ZAK-21-INIT-01` … `TEST-ZAK-21-INIT-09` |
| ZAK-21 | EXP (55) | `TEST-ZAK-21-EXP-01`, `TEST-ZAK-21-EXP-02` |
| ZAK-21 | PACK (26) | `TEST-ZAK-21-PACK-01`, `TEST-ZAK-21-PACK-02` |
| PPL-6 | INIT (−2) | `TEST-PPL-6-INIT-01` … `TEST-PPL-6-INIT-09` |
| PPL-6 | EXP (55) | `TEST-PPL-6-EXP-01`, `TEST-PPL-6-EXP-02` |
| PPL-6 | PACK (26) | `TEST-PPL-6-PACK-01`, `TEST-PPL-6-PACK-02` |

The `externalCode` is the stable identity used to find an existing order and determine whether it needs to be created or reset.

---

## 5. ShippingGuid Resolution

`POST /api/orders` requires a `shippingGuid` (UUID), while the picking-list filter uses a numeric `shippingId`. These are correlated via a config-stored mapping in `appsettings.json` of the test project:

```json
"Shoptet": {
  "IsTestEnvironment": true,
  "BaseUrl": "https://api.myshoptet.com",
  "ApiToken": "...",
  "ShippingGuidMap": {
    "21": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
    "6":  "yyyyyyyy-yyyy-yyyy-yyyy-yyyyyyyyyyyy"
  }
}
```

GUIDs are discovered once by calling `GET /api/eshop?include=shippingMethods` on the test store, then recorded in config. This avoids fragile name-matching and makes stale mappings obvious (order creation fails with a clear API error).

`paymentMethodGuid` follows the same pattern — a single test payment method GUID stored in config.

---

## 6. Upsert + Reset Algorithm

```
AssertTestEnvironment(config)   // Guards — abort if either fails

shippingGuidMap = config["Shoptet:ShippingGuidMap"]

for each orderDefinition in SeedCatalog:
    existing = GET /api/orders?externalCode={orderDefinition.ExternalCode}

    if existing is null:
        POST /api/orders  {
            externalCode: orderDefinition.ExternalCode,
            shippingGuid: shippingGuidMap[orderDefinition.ShippingId],
            // minimal valid order body (test customer, single product item)
            suppressEmailSending: true,
            suppressStockMovements: true,
            suppressDocumentGeneration: true,
            suppressProductChecking: true
        }
        // New orders land in default status; change to target state if needed
        if defaultStatus ≠ orderDefinition.TargetState:
            PATCH /api/orders/{code}/status { statusId: orderDefinition.TargetState }

    else if existing.StatusId ≠ orderDefinition.TargetState:
        PATCH /api/orders/{code}/status { statusId: orderDefinition.TargetState }
        log "Reset {externalCode} from {current} → {target}"

    else:
        log "OK {externalCode} already in state {target}"
```

---

## 7. Minimal Order Body

Each seeded order uses a minimal but valid payload:

```json
{
  "email": "test-seed@heblo.test",
  "externalCode": "TEST-...",
  "shippingGuid": "...",
  "paymentMethodGuid": "...",
  "currency": { "code": "CZK" },
  "billingAddress": {
    "fullName": "Test Heblo",
    "street": "Testovací 1",
    "city": "Praha",
    "zip": "10000"
  },
  "items": [
    {
      "itemType": "product",
      "code": "TEST-ITEM",
      "name": "Test product",
      "vatRate": 21,
      "itemPriceWithVat": 1.00,
      "quantity": 1
    }
  ],
  "suppressEmailSending": true,
  "suppressStockMovements": true,
  "suppressDocumentGeneration": true,
  "suppressProductChecking": true
}
```

---

## 8. Test Class Structure

```
Anela.Heblo.Adapters.Shoptet.Tests/
└── Integration/
    └── ShoptetTestEnvironmentHydrationTests.cs
```

```csharp
[Collection("ShoptetIntegration")]
[Trait("Category", "Integration")]
public class ShoptetTestEnvironmentHydrationTests
{
    // [Fact] HydrateTestEnvironment()
    //   - Runs AssertTestEnvironment()
    //   - Executes upsert+reset for all 26 order definitions
    //   - Logs outcome per order (created / reset / ok)

    // [Fact] PurgeTestOrders()
    //   - Runs AssertTestEnvironment()
    //   - GET /api/orders filtered by externalCode prefix "TEST-"
    //   - DELETE /api/orders/{code} for each result
    //   - Logs count of deleted orders
}
```

Uses the existing `ShoptetIntegrationTestFixture`. The fixture needs a new `IShoptetOrderClient` (or a configured `HttpClient`) from `Anela.Heblo.Adapters.ShoptetApi` registered in `ShoptetIntegrationTestFixture.cs`.

---

## 9. Configuration

Test `appsettings.json` (in test project, committed with placeholder values):

```json
{
  "Shoptet": {
    "IsTestEnvironment": false,
    "BaseUrl": "https://api.myshoptet.com",
    "ShippingGuidMap": {
      "21": "FILL_IN_FROM_TEST_STORE",
      "6":  "FILL_IN_FROM_TEST_STORE"
    },
    "PaymentMethodGuid": "FILL_IN_FROM_TEST_STORE"
  }
}
```

Real values go in **user secrets** (never committed). `IsTestEnvironment` defaults to `false` — must be explicitly overridden to run hydration.

---

## 10. Out of Scope

- Seeding all 18 shipping methods — representative subset (2) is sufficient for now
- Automatic GUID discovery at runtime — config mapping is explicit and auditable
- Teardown after each test run — `PurgeTestOrders` is a separate, manually-invoked fact
- Order contents beyond minimal valid payload (addresses, real products)
- Additional order states beyond `-2`, `55`, `26`
