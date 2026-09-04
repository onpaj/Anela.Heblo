### task: replace-applyenrichment-call-with-inline-cooling-loop

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs:76-80`

This is the only production-code change in this plan. No test file needs to change — the two tests listed above already cover the exact behavior being preserved.

- [ ] Open `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs` and replace this exact block (current lines 76-80):

  ```csharp
        ShoptetApiExpeditionListSource.ApplyEnrichment(
            order.Items,
            new Dictionary<string, decimal>(),
            new Dictionary<string, string>(),
            coolingByCode);
  ```

  with:

  ```csharp
        foreach (var item in order.Items)
        {
            if (coolingByCode.TryGetValue(item.ProductCode, out var cooling))
                item.Cooling = cooling;
        }
  ```

  (Same indentation level as the removed call — 8 spaces — since it sits directly inside `GetPackingOrderAsync`'s method body, not inside another block.)

- [ ] Confirm `ShoptetApiExpeditionListSource` is still referenced elsewhere in the file (so its `using` — implicit via the shared `Anela.Heblo.Adapters.ShoptetApi.Expedition` namespace already imported at the top of the file — stays justified) and is not left as an unused import:

  ```bash
  grep -n "ShoptetApiExpeditionListSource" backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs
  ```

  Expected: one remaining match, `ShoptetApiExpeditionListSource.MapToExpeditionOrder(detail);` (line 64) — confirming the `using Anela.Heblo.Adapters.ShoptetApi.Expedition;` line at the top of the file is still needed and must **not** be removed.

- [ ] Build the solution:

  ```bash
  dotnet build Anela.Heblo.sln
  ```

  Expected: `Build succeeded.` with 0 errors and no new warnings.

- [ ] Run `dotnet format` and confirm it makes no further changes beyond what was just hand-edited:

  ```bash
  dotnet format Anela.Heblo.sln --verify-no-changes
  ```

  Expected: exits 0 with no output. If it reports a violation, run `dotnet format Anela.Heblo.sln`, inspect `git diff` to confirm the only changes are whitespace/formatting in the touched file, and re-run `--verify-no-changes` to confirm.

- [ ] Run the two cooling-behavior tests directly to confirm behavior parity:

  ```bash
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~ShoptetApiPackingOrderClientTests"
  ```

  Expected: all tests in `ShoptetApiPackingOrderClientTests` pass, 0 failed — in particular:
  - `GetPackingOrderAsync_ComputesCooling_FromCarrierMatrixAndCatalog` still asserts `IsCooled == true`
  - `GetPackingOrderAsync_NotCooled_WhenCarrierMatrixEmpty` still asserts `IsCooled == false`
  - `GetPackingOrderAsync_MapsHeaderAndItems` and all other tests in the file are unaffected

- [ ] Run the picking-list / expedition test suites to confirm the untouched `ApplyEnrichment` call site and method are unaffected:

  ```bash
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~ShoptetApiExpeditionListSourceTests"
  dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj \
    --filter "FullyQualifiedName~ShoptetApiExpeditionListSource"
  ```

  Expected: all tests pass, 0 failed — confirming `ApplyEnrichment` and `PickingListBatchProcessor`'s use of it are unchanged.

- [ ] Run the full backend test suite once, to catch any unrelated regression:

  ```bash
  dotnet test Anela.Heblo.sln
  ```

  Expected: all tests pass, 0 failed.

- [ ] Review the diff to confirm the change is surgical — only the `ApplyEnrichment` call is replaced, nothing else in the file changed:

  ```bash
  git diff backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs
  ```

  Expected: one contiguous block replaced (the 5-line `ApplyEnrichment(...)` call becomes a 5-line `foreach` loop); no other line in the file shows as added or removed.

- [ ] Commit:

  ```bash
  git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs
  git commit -m "refactor: replace ApplyEnrichment call with inline cooling enrichment in ShoptetApiPackingOrderClient

  GetPackingOrderAsync only ever needs the cooling branch of ApplyEnrichment
  (stock/location dictionaries were always empty). Replacing the call with a
  direct loop removes two dead allocations per packing-screen load and
  severs this class's static coupling to ShoptetApiExpeditionListSource's
  expedition-list enrichment logic. No behavior change; ApplyEnrichment and
  its other (real) call site in PickingListBatchProcessor are untouched."
  ```

---

## Self-review against the spec

- **FR-1** (apply only cooling enrichment at the packing call site): done in `replace-applyenrichment-call-with-inline-cooling-loop` — the replacement loop is the exact cooling branch already inside `ApplyEnrichment`, applied at the same point in the method, over the same `order.Items` collection. No stock/location/price logic is introduced. Verified against both existing `IsCooled` tests.
- **FR-2** (preserve `ApplyEnrichment` for the expedition/picking-list path): verified during architecture review and re-confirmed here via `grep` — `PickingListBatchProcessor.cs:89` is `ApplyEnrichment`'s only other caller and is untouched; `ShoptetApiExpeditionListSourceTests` and the Shoptet adapter test project are re-run in this plan's final tasks to confirm no regression.
- **NFR-1** (performance — eliminate two dictionary allocations per call): satisfied by construction — the replacement code allocates nothing; the diff-review step confirms no other allocation is introduced.
- **NFR-2** (behavior parity / risk): the two named existing tests are the concrete parity check; both are run and expected to pass unmodified.
- **Out of scope items** (`ApplyEnrichment`'s own signature/body, the picking-list `CreatePickingList` path, other `ShoptetApiPackingOrderClient` methods, any new shared abstraction, any test-suite restructuring beyond what's needed here): none are touched — confirmed by the diff-review step showing a single-file, single-block change, and by the "no test file needs to change" note at the top of the task.

No placeholders, no "TBD", no references to undefined types/methods — the task shows the exact current code, the exact resulting code, and exact runnable commands with expected output.

## Status: COMPLETE
