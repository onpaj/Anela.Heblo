# Review: PR #587 — StockUp Playwright → Shoptet REST API (Issue #535)

## Context

PR #587 migrates stock-up operations from Playwright browser automation to direct Shoptet REST API calls (`PATCH /api/stocks/{stockId}/movements`). The goal is eliminating headless-Chromium reliability problems. Issue #535 provides a detailed spec with 7 tasks and a 13-file map.

---

## Spec Coverage: Issue #535 vs PR #587

| Issue Requirement | PR Status | Notes |
|---|---|---|
| Task 1: `IShoptetStockClient` interface | ✅ Done | Matches spec exactly |
| Task 2: DTO models (Request/Response) | ✅ Done | Matches spec exactly |
| Task 3: `ShoptetStockClient` (TDD) | ✅ Done | 6 tests, all spec'd scenarios covered |
| Task 4 (inferred): `StockId` setting + DI wiring | ✅ Done | `ShoptetApiSettings.StockId` added, HTTP client registered |
| Task 5 (inferred): Rewire Playwright service | ✅ Done | `StockUpAsync` delegates to REST, `VerifyStockUpExistsAsync` → `false` |
| Task 6 (inferred): Simplify `StockUpProcessingService` | ✅ Done | Post-verify removed, 3 new tests |
| Task 7 (inferred): Update `shoptet-api.md` docs | ✅ Done | New section 8 added |
| `ShoptetAdapterServiceCollectionExtensions.cs` modification | ❌ **Missing** | See finding #1 below |

**Verdict: 12 of 13 files from the spec are addressed. 1 file was listed as "Modify" but not changed.**

---

## Findings

### 1. MEDIUM — Dead DI registrations for removed Playwright scenarios

`ShoptetAdapterServiceCollectionExtensions.cs` still registers:
```csharp
services.AddSingleton<StockUpScenario>();
services.AddSingleton<VerifyStockUpScenario>();
```

These classes are **no longer injected anywhere** — `ShoptetPlaywrightStockDomainService` no longer takes them as constructor params. They're dead code that:
- Unnecessarily instantiates Playwright infrastructure on startup
- Could confuse future developers into thinking they're still used
- The issue's File Map listed this file as "Modify" — this was likely the intended cleanup

**Recommendation:** Remove both registrations from `ShoptetAdapterServiceCollectionExtensions.cs`.

### 2. ~~HIGH~~ NOT AN ISSUE — Multi-product partial failure

**Verified safe.** The queue architecture ensures 1 product per operation:
- `CreateOperationAsync` takes a single `productCode` → creates one `StockUpOperation`
- `ProcessOperationAsync` constructs `new StockUpRequest(productCode, amount)` → always 1 item in `Products` list
- All callers (`ChangeTransportBoxStateHandler`, `GiftPackageManufactureService`) loop and call `CreateOperationAsync` once per product

The `StockUpRequest.Products` being a `List<>` is legacy from Playwright batch-submit days. The foreach loop in `StockUpAsync` always iterates exactly once. No partial failure risk.

### 3. LOW — Pre-verify is now dead code path

`StockUpProcessingService` still calls:
```csharp
var existsInShoptet = await _eshopService.VerifyStockUpExistsAsync(operation.DocumentNumber);
```

This **always returns `false`** (hardcoded). The entire pre-verify block (try/catch, early return on `true`, warning log) is unreachable dead code. It's harmless but adds cognitive noise.

**Recommendation:** Consider removing the pre-verify block entirely from `StockUpProcessingService`, since it can never return `true` now. The issue spec acknowledged this ("Pre-verify remains harmless") so this is intentional — but it could be a follow-up cleanup.

### 4. LOW — Unrelated file bundled in PR

`tasks/plan-issue-577-manufacture-pipeline-regressions.md` (1,149 lines) is an implementation plan for a completely different issue (#577). This shouldn't be in this PR — it bloats the diff and conflates separate concerns.

**Recommendation:** Remove from this PR, commit it separately or on the #577 branch.

### 5. INFO — Two classes named `ShoptetStockClient` in different namespaces

- `Anela.Heblo.Adapters.Shoptet.Stock.ShoptetStockClient` (existing, implements `IEshopStockClient`)
- `Anela.Heblo.Adapters.ShoptetApi.Stock.ShoptetStockClient` (new, implements `IShoptetStockClient`)

Different interfaces, different adapters, but same class name. Not a bug but could cause confusion when navigating code.

---

## Test Coverage Analysis

### What's covered (9 new tests):

**ShoptetStockClientTests (6 tests):**
- ✅ Success with null errors
- ✅ Success with empty errors array
- ✅ Error response throws `InvalidOperationException`
- ✅ HTTP error (400) throws `HttpRequestException`
- ✅ Correct `stockId` in URL
- ✅ Correct request body serialization

**StockUpProcessingServiceTests (3 tests):**
- ✅ No pending operations → no-op
- ✅ Successful submit → marks Completed
- ✅ Exception → marks Failed

### What's NOT covered:

| Gap | Severity | Notes |
|---|---|---|
| ~~Multi-product partial failure~~ | ~~Medium~~ | **Not an issue** — queue ensures 1 product per operation always |
| `ShoptetPlaywrightStockDomainService.StockUpAsync` delegation | Low | The foreach loop delegating to `_stockClient` has no direct test |
| `VerifyStockUpExistsAsync` always returns `false` | Low | Not directly asserted (indirectly tested via processing service) |
| CancellationToken propagation in `ShoptetStockClient` | Low | Token passed to `PatchAsJsonAsync` and `ReadFromJsonAsync` but not tested |
| Network timeout / transient failure behavior | Low | No retry policy configured on the HTTP client |
| DI wiring integration test | Low | No test verifying `IShoptetStockClient` resolves correctly at runtime |

---

## Workflow Path Analysis

### Happy path: Single product stock-up
```
ProcessPendingOperationsAsync
  → GetByStateAsync(Pending) → [operation]
  → ProcessOperationAsync(operation)
    → VerifyStockUpExistsAsync("DOC-001") → false (always)
    → MarkAsSubmitted, SaveChanges
    → StockUpAsync(request)
      → foreach product: UpdateStockAsync(code, amount)
        → PATCH /api/stocks/1/movements → 200, errors=[]
    → MarkAsCompleted, SaveChanges ✅
```

### Error path: Shoptet returns partial error
```
  → UpdateStockAsync(code, amount)
    → PATCH /api/stocks/1/movements → 200, errors=[{...}]
    → throws InvalidOperationException
  → catch: MarkAsFailed("Processing failed: Shoptet stock update failed...") ✅
```

### Error path: HTTP failure (network, 5xx)
```
  → UpdateStockAsync(code, amount)
    → PATCH /api/stocks/1/movements → 500
    → EnsureSuccessStatusCode() → throws HttpRequestException
  → catch: MarkAsFailed("Processing failed: ...") ✅
```

### Edge case: Pre-verify exception (e.g., if implementation changes later)
```
  → VerifyStockUpExistsAsync throws
  → catch (inner): LogWarning, continue with submit
  → (proceeds normally) ✅
```

### Edge case: Document already exists (currently impossible)
```
  → VerifyStockUpExistsAsync → false (always, dead path)
  → The `if (existsInShoptet)` branch is unreachable ⚠️
```

### ~~Risk path: Multi-product partial failure~~ — NOT POSSIBLE
Queue architecture guarantees 1 product per `StockUpRequest`. The `Products` list is always length 1. ✅

---

## Architecture Note

The new `IShoptetStockClient` interface is a **temporary bridge** while the Playwright adapter still handles `SubmitStockTakingAsync`. Once Playwright is fully removed, this interface can be collapsed — the REST stock logic can move directly into a clean `IEshopStockDomainService` implementation. Accepted as-is for now.

---

## Summary

The PR **faithfully implements the issue spec**. The architecture is clean — new `IShoptetStockClient` in Application layer, REST implementation in ShoptetApi adapter, Playwright service delegates to it. The test coverage for the new `ShoptetStockClient` is solid.

**Action items (to fix before merge):**

1. **Remove dead `StockUpScenario` / `VerifyStockUpScenario` DI registrations** from `ShoptetAdapterServiceCollectionExtensions.cs` (the spec intended this, file was listed as "Modify")

2. **Remove unrelated `tasks/plan-issue-577-*` file** from this PR

3. **Simplify `IEshopStockDomainService.StockUpAsync` to single-product signature**

   The queue guarantees 1 product per operation. Make the interface honest:

   **Change `IEshopStockDomainService`** (Domain):
   ```csharp
   // Before:
   Task StockUpAsync(StockUpRequest stockUpOrder);
   // After:
   Task StockUpAsync(string productCode, double amount, string? documentNumber = null);
   ```

   **Files to modify:**
   - `backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/IEshopStockDomainService.cs` — change signature
   - `backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/StockUpRequest.cs` — can be deleted or kept for `StockUpScenario` only (Playwright-internal)
   - `backend/src/Adapters/Anela.Heblo.Adapters.Shoptet/Playwright/ShoptetPlaywrightStockDomainService.cs` — simplify to direct `_stockClient.UpdateStockAsync(productCode, amount)` call (remove foreach)
   - `backend/src/Anela.Heblo.Application/Features/Catalog/Services/StockUpProcessingService.cs` — call `StockUpAsync(productCode, amount, docNumber)` directly instead of constructing `StockUpRequest`
   - `backend/test/Anela.Heblo.Tests/Features/Catalog/Stock/StockUpProcessingServiceTests.cs` — update mock setup for new signature

   **Dependency to handle:**
   - `StockUpScenario.RunAsync(StockUpRequest)` still uses the multi-product `StockUpRequest` — but it's only used by manual integration tests (all `[Skip]`). `StockUpRequest` can stay as an internal Playwright-only model, or `StockUpScenario` can be updated too (it's dead code now anyway).
   - `ShoptetPlaywrightStockUpScenarioIntegrationTests` uses multi-product requests — update or leave since they're all skipped.

**Verified safe:**
4. ~~Multi-product partial failure~~ — Queue ensures 1 product per operation. This refactor makes that guarantee explicit in the type system.

**Optional follow-ups:**
5. Consider adding Polly retry policy on the stock HTTP client for transient failures (like Comgate in PR #589)
6. Clean up dead pre-verify code path in `StockUpProcessingService` once comfortable
