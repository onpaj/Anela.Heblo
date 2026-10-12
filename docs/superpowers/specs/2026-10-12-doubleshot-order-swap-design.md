# Double-shot V → S order swap — design

Issue: #4480 (parent #4477, sibling #4479). Deadline 19 Oct 2026 (set relaunch with the price increase).

## Goal

Customers order a double-shot as a **`V` variant** (plain product, e.g. `SA015030V`). Its mirror
**`S` product set** (`SA015030`) carries the composition. Every `V` order line must be replaced
by its `S` set inside Shoptet, so stock, picking, packing and invoicing all behave as for a normal
set — with no change to Heblo's downstream consumers.

## Decisions (Ondra, 2026-10-12)

| Topic | Decision |
|---|---|
| Where to swap | **In Shoptet**, via the order-item API. |
| Atomicity | No single-call swap exists (see below). Accept **two calls — POST S, then DELETE V** — with idempotent self-healing. |
| V ↔ S mapping | **Naming convention only**: a code starting `SA` and ending `V` is a V variant; its S set is the same code without the trailing `V`. No config. Shared with #4479 as `DoubleShotCodeRule`. |
| Eligible statuses | Pre-picking states only (configurable, defaults below). |
| Cadence | Every 5 min + one sweep right before every picking-list print. |
| Rollout | Dry-run until feature flag `is-double-shot-order-swap-enabled` is on. |

## API facts this relies on

Verified on test store 780175 on 2026-10-12 and documented in
`docs/integrations/shoptet-api.md` §3.8 and §3.17:

- `POST /api/orders/{code}/item` with `itemType: product-set` expands the set into `completion[]`
  (`product-set-item` rows), deducts component stock, keeps the sent unit price and `priceRatio`,
  stores `remark` (returned as `items[].remark`). Works in `-1` and `-2`, and after an invoice.
- `DELETE /api/orders/{code}/item/{id}` removes the line and returns its stock.
- `PATCH /api/orders/{code}/item/{id}` changing `code`/`itemType` **only relabels** the line: no
  components, no stock movement, and a later order delete returns stock for the new code. It is
  never used.
- `GET /api/orders?productCode=` matches a line code exactly (variant or `product-set` line).
- Invoices follow later order-item edits; item edits are not written to order history.

## Components

### 1. `DoubleShotCodeRule` — `backend/src/Anela.Heblo.Domain/Features/Catalog/DoubleShotCodeRule.cs`

Static, dependency-free, styled after `BundleProductRule`. Shared with #4479 (announced on #4477).

```csharp
public static class DoubleShotCodeRule
{
    public static bool IsVariantCode(string? code);                            // "SA015030V" -> true
    public static bool TryGetSetCode(string? variantCode, out string setCode); // "SA015030V" -> "SA015030"
    public static string ToVariantCode(string setCode);                        // "SA015030" -> "SA015030V"
}
```

Rule: ordinal, case-sensitive; `code.Length > 3`, starts with `"SA"`, ends with `'V'`.
`ToVariantCode` throws `ArgumentException` for a null/empty code or one that is already a
variant code.

### 2. Order-item operations on `IEshopOrderClient`

`backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs`, implemented in
the ShoptetApi adapter (`Orders/`):

| Method | Call |
|---|---|
| `Task<IReadOnlyList<string>> ListOrderCodesContainingProductAsync(string productCode, DateTimeOffset createdFrom, CancellationToken ct)` | `GET /api/orders?productCode={code}&creationTimeFrom={iso, + as %2B}&page=n&itemsPerPage=50`, all pages |
| `Task<EshopOrderDetail?> GetOrderDetailAsync(string orderCode, CancellationToken ct)` | `GET /api/orders/{code}`; 404 → `null` |
| `Task AddOrderItemAsync(string orderCode, NewEshopOrderItem item, CancellationToken ct)` | `POST /api/orders/{code}/item` |
| `Task DeleteOrderItemAsync(string orderCode, long itemId, CancellationToken ct)` | `DELETE /api/orders/{code}/item/{itemId}` |

New Application-layer types (classes, not records, if they ever cross the API boundary; they do not
today):

- `EshopOrderDetail { Code, StatusId, IReadOnlyList<EshopOrderLine> Lines }`
- `EshopOrderLine { ItemId, ItemType, Code, Amount, UnitPriceWithVat, VatRate, PriceRatio, Remark }`
  (decimals parsed from Shoptet's string fields with invariant culture)
- `NewEshopOrderItem { ItemType, Code, Amount, UnitPriceWithVat, VatRate, PriceRatio, Remark }`
  serialised as strings: `amount` `"2.000"`, prices `"1770.00"`, `vatRate` `"21.00"`, `priceRatio` `"0.9000"`.

Errors: same exception type as the rest of `ShoptetOrderClient` (`HttpRequestException` with
`StatusCode` set), but built explicitly instead of `EnsureSuccessStatusCode()` so the message
carries Shoptet's `errors[].errorCode` and `message` from the body
(`"Shoptet POST /api/orders/{code}/item returned 422: invalid-request-data – …"`).
The service tells **422** (permanent: skip the line, log error) and **409** (transient: retry
next sweep) apart via `ex.StatusCode`.

### 3. `DoubleShotSwapPlanner` — pure logic

`backend/src/Anela.Heblo.Application/Features/ShoptetOrders/DoubleShotSwap/DoubleShotSwapPlanner.cs`

`Plan(EshopOrderDetail order, IReadOnlySet<int> eligibleStatusIds) -> DoubleShotSwapPlan`

- Not eligible status → plan with `SkipReason`.
- For each line with `ItemType == "product"` and `DoubleShotCodeRule.IsVariantCode(Code)`:
  - tag = `SwapTag.For(vCode, vItemId)` = `"auto-swap {vCode} #{vItemId}"`;
  - if a `product-set` line with `Remark == tag` already exists → `DeleteVariantOnly(vItemId)` (heal);
  - else → `AddSetThenDeleteVariant(newItem, vItemId)` where `newItem` copies amount, unit
    price with VAT, VAT rate and price ratio, with `ItemType = "product-set"`, `Code = S`,
    `Remark = tag`.
- No V lines → empty plan.

### 4. `DoubleShotOrderSwapService`

`.../ShoptetOrders/DoubleShotSwap/DoubleShotOrderSwapService.cs`, interface
`IDoubleShotOrderSwapService { Task<DoubleShotSwapSummary> SweepAsync(bool applyChanges, CancellationToken ct); }`

1. Acquire a static `SemaphoreSlim(1,1)`; wait max 2 min, else log and return an empty summary.
2. V codes: `IEshopStockClient.ListAsync` → distinct codes where `DoubleShotCodeRule.IsVariantCode`.
3. For each V code: `ListOrderCodesContainingProductAsync(code, now − LookbackDays)`; union the order codes.
4. For each order (try/catch per order; `OperationCanceledException` rethrown):
   1. `GetOrderDetailAsync` (null → skip), plan.
   2. Dry run → log every planned action, no writes.
   3. Apply → for each action: POST (if any), then DELETE. 422 on POST → log **error**
      (order, V code, S code, Shoptet message), keep V, continue with the next action.
      409 → log warning, abandon this order until the next sweep.
   4. If at least one swap completed → `AppendEshopRemarkAsync(code,
      "Automaticky prohozeno: SA015030V → SA015030 (2 ks); …")`; failure is a warning only.
5. Log a summary: orders scanned, lines swapped, healed, failed, dry-run flag.

### 5. `SwapDoubleShotOrdersJob : IRecurringJob`

`.../ShoptetOrders/Infrastructure/Jobs/SwapDoubleShotOrdersJob.cs`, modelled on
`CompleteDeliveredOrdersJob`: `JobName = "swap-double-shot-orders"`, category Warehouse,
cron `*/5 * * * *`, `DefaultIsEnabled = true`. Checks `IRecurringJobStatusChecker`, reads
`FeatureFlagKeys.DoubleShotOrderSwap` (`is-double-shot-order-swap-enabled`, default `false`) to
decide `applyChanges`, calls `SweepAsync`.

### 6. Picking-list hook

`ExpeditionListService.PrintPickingListAsync` calls `SweepAsync(applyChanges: flag)` before
`_pickingSource.CreatePickingListAsync`. Any exception is logged as a warning and printing
continues. Applies to all four entry points (they share this method).

### Settings

`ShoptetOrdersSettings` (existing):
- `DoubleShotSwapEligibleStatusIds` — default `[-1, 3, 32, -2, 35, 73]`
  (Nová, Čeká se na platbu, Zaplaceno, Vyřizuje se, Poznámka, Oprava-robot).
- `DoubleShotSwapLookbackDays` — default `14`.

Mind the config-binder array-merge gotcha: if an environment overrides the status list with fewer
elements, `.Distinct()` at the point of use and prefer an empty-array default.

## Out of scope (YAGNI)

- Heblo-side V→S mapping in picking list, packing, Flexi invoice export, analytics: unnecessary
  once Shoptet data is correct; invoices follow item edits.
- Config overrides for the mapping.
- Using `suppressStockMovements`.

## Testing

TDD, xUnit + Moq/FluentAssertions as in the existing test projects.

- `DoubleShotCodeRuleTests`: `SA015030V` valid; `SA015030`, `SER001030V`, `V`, `SAV`, `""`, `null`,
  lowercase `sa015030v` invalid; round-trip `ToVariantCode`/`TryGetSetCode`; `ToVariantCode` guards.
- `DoubleShotSwapPlannerTests`: mixed V/normal/set lines; two V lines with the same code; heal
  case; ineligible status; amount/price/VAT/priceRatio copied; no V lines → empty plan.
- `DoubleShotOrderSwapServiceTests` (mocked client): dry run makes no writes; POST before DELETE
  (`MockSequence`); 422 keeps V and logs error; 409 abandons the order; one failing order does not
  stop the next; note appended once per order with all swaps; healed order deletes only V.
- `SwapDoubleShotOrdersJobTests`: disabled job does nothing; flag off → `applyChanges: false`.
- Adapter tests (`HttpMessageHandler` stub): request bodies **parsed as JSON** and asserted field
  by field; `creationTimeFrom` encoded with `%2B`; pagination; 201/200 parsing; 404 → null;
  422/409 surfaced distinguishably.
- `ExpeditionListService` tests: sweep runs before `CreatePickingListAsync`; sweep exception does
  not prevent printing.

## Docs

- New `docs/processes/job-double-shot-order-swap.md` (template `_TEMPLATE.md`), then
  `python3 scripts/process-docs/check.py index`.
- `docs/processes/flow-picking-list.md`: pre-print sweep step.
- Feature flag registered per `docs/development/feature-flags.md` (registry, appsettings, FE mirror).
- `docs/integrations/shoptet-api.md` — already updated on this branch.

## Rollout

1. Merge with the flag off: the job logs planned swaps in production (dry run).
2. After Andy creates the `V` variants, verify the dry-run log on a real `V` order.
3. Enable `is-double-shot-order-swap-enabled`.

Residual risk: the test store has no real `V` products; production differs only by the code, and
the dry-run log shows exact V → S pairs before any write.
