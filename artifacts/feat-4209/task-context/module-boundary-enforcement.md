### task: module-boundary-enforcement

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`

- [ ] **Step 1: Remove the now-stale IEshopOrderClient allowlist entries for Packaging**

In `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`, inside `PackagingShoptetOrdersAllowlist`
(around line 297), delete these two lines:

```csharp
        "Anela.Heblo.Application.Features.Packaging.UseCases.ScanPackingOrder.ScanPackingOrderHandler -> Anela.Heblo.Application.Features.ShoptetOrders.IEshopOrderClient",
```

and

```csharp
        "Anela.Heblo.Application.Features.Packaging.UseCases.CompletePackingOrder.CompletePackingOrderHandler -> Anela.Heblo.Application.Features.ShoptetOrders.IEshopOrderClient",
```

Leave every `IPackingOrderClient`, `PackingOrder`, and `PackingOrderItem` entry in that allowlist untouched —
that coupling is explicitly out of scope for this issue.

- [ ] **Step 2: Update the allowlist's doc comment**

Replace the comment block directly above `PackagingShoptetOrdersAllowlist` (currently):

```csharp
    // Allowlist for Packaging -> ShoptetOrders. The Packaging module legitimately consumes
    // the IPackingOrderClient / IEshopOrderClient contracts (and their DTOs) defined in
    // Anela.Heblo.Application.Features.ShoptetOrders. Everything else — particularly
    // ShoptetOrdersSettings, PackingStateId, and PackedStateId — must not be referenced
    // from Packaging. This rule pins the 2026-06-05 decoupling in place.
```

with:

```csharp
    // Allowlist for Packaging -> ShoptetOrders. The Packaging module legitimately consumes
    // the IPackingOrderClient contract (and its PackingOrder/PackingOrderItem DTOs) defined in
    // Anela.Heblo.Application.Features.ShoptetOrders. Everything else — particularly
    // ShoptetOrdersSettings, PackingStateId, PackedStateId, and (as of the IEshopOrderClient
    // decoupling) IEshopOrderClient itself — must not be referenced from Packaging. Packaging now
    // consumes MarkAsPackedAsync via its own IPackedOrderStatusUpdater contract, implemented by
    // ShoptetOrders' ShoptetOrdersPackedOrderStatusUpdaterAdapter.
```

- [ ] **Step 3: Add the new ExpeditionList -> ShoptetOrders rule**

Add a new private field, placed near `ExpeditionListLogisticsAllowlist` (around line 228-231) for locality:

```csharp
    // Allowlist for ExpeditionList -> ShoptetOrders. Empty — PrintExpeditionOrderHandler now consumes
    // the ExpeditionList-owned IOrderStatusReader contract; the ShoptetOrders adapter
    // (ShoptetOrdersOrderStatusReaderAdapter) lives in ShoptetOrders.Infrastructure and implements it
    // there, so no ExpeditionList type needs to reference ShoptetOrders directly.
    private static readonly HashSet<string> ExpeditionListShoptetOrdersAllowlist = new(StringComparer.Ordinal);
```

Add a new entry to the `Rules()` `TheoryData<ModuleBoundaryRule>` (in the `public static TheoryData<ModuleBoundaryRule> Rules()` method, alongside the existing `"ExpeditionList -> Logistics"` and `"Packaging -> ShoptetOrders"` entries — insert it directly after the `"ExpeditionList -> Logistics"` entry for locality):

```csharp
        new ModuleBoundaryRule(
            Name: "ExpeditionList -> ShoptetOrders",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.ExpeditionList",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Application.Features.ShoptetOrders",
            },
            Allowlist: ExpeditionListShoptetOrdersAllowlist),
```

- [ ] **Step 4: Run the module boundary tests to verify all rules pass, including the new one**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ModuleBoundariesTests"`
Expected: PASS — every `[Theory]` case, including the new `"ExpeditionList -> ShoptetOrders"` rule (proving
`PrintExpeditionOrderHandler` no longer references `Anela.Heblo.Application.Features.ShoptetOrders` at all)
and the edited `"Packaging -> ShoptetOrders"` rule (proving `ScanPackingOrderHandler` and
`CompletePackingOrderHandler` no longer reference `IEshopOrderClient`, since that entry is now gone from
the allowlist and any lingering reference would fail the test).

- [ ] **Step 5: Confirm no stray IEshopOrderClient references remain outside ShoptetOrders**

Run: `cd backend && grep -rn "IEshopOrderClient" src/ --include=*.cs | grep -v "^src/Anela.Heblo.Application/Features/ShoptetOrders/" | grep -v "^src/Adapters/Anela.Heblo.Adapters.ShoptetApi/"`
Expected: no output (empty) — confirms FR-7's "no remaining direct ShoptetOrders coupling" acceptance
criterion at the source level, independent of the reflection-based test.

- [ ] **Step 6: Full solution build and full test suite**

Run: `cd backend && dotnet build && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: Build succeeded; all tests pass (no regressions anywhere else in the suite).

- [ ] **Step 7: Format check**

Run: `cd backend && dotnet format --verify-no-changes`
Expected: no formatting violations. If it reports changes, run `dotnet format` and re-run step 6.

- [ ] **Step 8: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
git commit -m "test(architecture): enforce ExpeditionList/Packaging decoupling from IEshopOrderClient"
```

---

## Self-Review

**Spec coverage:**
- FR-1 (`IPackedOrderStatusUpdater` contract) → `packaging-contract` task.
- FR-2 (ShoptetOrders adapter for it) → `packaging-adapter` task.
- FR-3 (Packaging handlers switched over) → `packaging-handlers` task.
- FR-4 (`IOrderStatusReader` contract) → `expeditionlist-contract` task.
- FR-5 (ShoptetOrders adapter for it) → `expeditionlist-adapter` task.
- FR-6 (ExpeditionList handler switched over) → `expeditionlist-handler` task.
- FR-7 (verify no remaining direct coupling) → `module-boundary-enforcement` task (grep check + new/edited
  reflection-based architecture test).
- NFR-1/NFR-2/NFR-3 (no perf/security/behavior change) → satisfied by every adapter being a bare 1:1
  delegation with no added logic, verified by the "propagates exception unmodified" tests in the two
  adapter tasks and the retained 404/500 handler tests.
- arch-review.r1.md's Specification Amendments (404 pass-through test, `ModuleBoundariesTests.cs` updates,
  new `ExpeditionList -> ShoptetOrders` rule) → covered by `expeditionlist-adapter`'s
  `GetOrderStatusIdAsync_PropagatesNotFoundHttpRequestException_Unmodified` test and the
  `module-boundary-enforcement` task.

**Placeholder scan:** No "TBD"/"TODO"/"add appropriate error handling" phrases used; every step shows
complete code or an exact command with expected output.

**Type consistency:** `IPackedOrderStatusUpdater.MarkAsPackedAsync(string orderCode, CancellationToken ct = default)`
and `IOrderStatusReader.GetOrderStatusIdAsync(string orderCode, CancellationToken ct = default)` are used
identically across every task that references them (contract definition, adapter implementation, adapter
test, handler, handler test).
