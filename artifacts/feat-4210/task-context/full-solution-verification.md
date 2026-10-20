### task: full-solution-verification

**Files:** none (verification only).

- [ ] **Step 1: Full backend build**

Run:
```bash
cd backend && dotnet build
```
Expected: `Build succeeded.` with 0 errors across every project in the backend (Application, the ShoptetApi adapter, the API host, and every test project).

- [ ] **Step 2: Format check**

Run:
```bash
cd backend && dotnet format --verify-no-changes
```
Expected: no formatting violations reported. If it reports violations in files this plan touched, run `dotnet format` (no flag) and re-stage/re-commit just those files with message `style(shoptet-orders): apply dotnet format`.

- [ ] **Step 3: Run the full Application unit test suite (FR-5 regression check)**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```
Expected: all tests pass, including `BlockOrderProcessingHandlerTests`, `ScanPackingOrderHandlerTests`, `ScanPackingOrderHandlerPackagePersistenceTests`, `CompletePackingOrderHandlerTests`, `CompleteDeliveredOrdersJobTests`, `PrintExpeditionOrderHandlerTests`, and `ModuleBoundariesTests` — none of these mock/stub `CreateOrderAsync`, `DeleteOrderAsync`, `GetRecentOrdersAsync`, or `ListByExternalCodePrefixAsync` today (confirmed via repo-wide grep during planning), so the narrowed `IEshopOrderClient` requires no test changes here. `ModuleBoundariesTests` specifically re-verifies its `IEshopOrderClient` allowlist entries for `ScanPackingOrderHandler` and `CompletePackingOrderHandler` still resolve correctly against the now-7-method interface (the allowlist is per-type, not per-member, so no allowlist edit is needed — this step just proves that holds).

- [ ] **Step 4: Grep the whole backend for any stray reference that still resolves the 4 relocated methods through `IEshopOrderClient`**

Run:
```bash
cd backend && grep -rn "IEshopOrderClient" --include="*.cs" . | grep -v "IShoptetOrderTestClient"
```
Read through the output manually: every remaining `IEshopOrderClient` reference should be either its own declaration/implementation, a DI registration, or a consumer that only needs the 7 surviving methods. There should be no line where `IEshopOrderClient` is injected solely to call one of the 4 relocated methods (FR-4's acceptance criterion).

- [ ] **Step 5: Run the full adapter integration test project one more time as a final compile/DI-resolution gate**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj
```
Expected: all tests pass or self-skip exactly as they did before this refactor (guarded by the same `SHOPTET_BLOCK_ORDER`/`SHOPTET_HYDRATE` env vars and `ShoptetTestGuard.Assert` as before) — no new failures attributable to this change.

- [ ] **Step 6: No commit needed for this task** — it is verification-only. If any step above required a fix, that fix was already committed under its own task; do not create an empty commit here.

---

## Self-Review

**Spec coverage:**
- FR-1 (new `IShoptetOrderTestClient` in the adapter project, 4 methods verbatim) → `create-shoptet-order-test-client-interface`.
- FR-2 (shrink `IEshopOrderClient` to 7 methods, regression grep) → `shrink-ieshoporderclient-interface` (Steps 1-3).
- FR-3 (`ShoptetOrderClient` implements both interfaces, DI registers both) → `implement-test-client-on-shoptet-order-client` + `register-test-client-in-di`.
- FR-4 (update the 3 integration test files to the new interface) → `update-block-order-processing-integration-tests`, `update-shoptet-test-environment-hydration-tests`, `update-picking-list-integration-tests`.
- FR-5 (no Application-layer mocking burden for removed methods) → confirmed by planning-time grep (no existing setups reference the 4 methods) and re-verified in `full-solution-verification` Step 3.
- NFR-1/NFR-2 (no perf/security impact) → satisfied by construction (no method bodies change).
- NFR-3 (zero behavior change) → every task explicitly preserves method bodies; only interface membership and call-site types change.
- "Out of Scope" items (no HTTP behavior change, no change to the 7 surviving methods, no DTO relocation, no further ISP cleanup) → respected; no task touches HTTP logic, the 7 surviving method bodies, or `CreateEshopOrderRequest`/`EshopOrderSummary`.

**Placeholder scan:** No "TBD"/"add appropriate handling"/"similar to Task N" phrasing anywhere above; every step shows the literal before/after code or the literal command and expected output.

**Type consistency:** `IShoptetOrderTestClient`'s 4 signatures (Step 1 of task 1) are reused character-for-character in `ShoptetOrderClient`'s existing method bodies (task 3, no change needed there) and in every call site rewritten in tasks 5-7 (`_testClient.CreateOrderAsync(...)`, `_testClient.DeleteOrderAsync(...)`, `_testClient.GetRecentOrdersAsync(...)`, `_testClient.ListByExternalCodePrefixAsync(...)` / `_testOrderClient.GetRecentOrdersAsync(...)` in the picking-list file, which names its test-client field `_testOrderClient` to mirror its existing `_orderClient` naming convention — this is intentionally the only file that doesn't use the literal name `_testClient`, called out here so it isn't mistaken for an inconsistency).

**Correction to the arch-review during planning:** The arch-review's file-by-file table claimed `ShoptetTestEnvironmentHydrationTests.cs` and `PickingListIntegrationTests.cs` are pure single-interface consumers that can retype their existing field entirely to `IShoptetOrderTestClient`. Re-reading the actual source during this planning pass found both files also call `UpdateStatusAsync` (which stays on `IEshopOrderClient`) — `ShoptetTestEnvironmentHydrationTests.HydrateTestEnvironment`/`PurgeTestOrders` at lines 245/252/291, and `PickingListIntegrationTests.PrintPickingList_ProducesPdfs_ForRecentOrders` at lines 80/117. Both are therefore **mixed-usage** consumers like `BlockOrderProcessingIntegrationTests`, and this plan's tasks 6 and 7 add a second field rather than retyping the existing one, to avoid breaking those `UpdateStatusAsync` call sites.
