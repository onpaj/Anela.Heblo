### task: wire-handler-to-targeted-lookup

Replaces `GetConsumptionHistoryHandler.Handle`'s `GetAllAsync` call with the new page-scoped `GetMaterialNamesByIdsAsync` lookup, turning the previous task's test green and completing FR-2.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetConsumptionHistory/GetConsumptionHistoryHandler.cs`

**Steps:**

1. In `GetConsumptionHistoryHandler.cs`, replace:

```csharp
        var materialNames = (await _repository.GetAllAsync(cancellationToken))
            .ToDictionary(m => m.Id, m => m.Name);
```

with:

```csharp
        var materialIds = records.Select(r => r.PackingMaterialId).Distinct();
        var materialNames = await _repository.GetMaterialNamesByIdsAsync(materialIds, cancellationToken);
```

The surrounding `Handle` method (after the edit) reads:

```csharp
        var (records, totalCount) = await _repository.GetConsumptionHistoryAsync(
            filter, skip, pageSize, ascending: !request.SortDescending, cancellationToken);

        var materialIds = records.Select(r => r.PackingMaterialId).Distinct();
        var materialNames = await _repository.GetMaterialNamesByIdsAsync(materialIds, cancellationToken);

        var items = records.Select(r => MapToDto(r, materialNames)).ToList();
```

No other line in the file changes.

2. Run the query-count test added in the previous task and confirm it now passes (green):

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetConsumptionHistoryQueryCountTests"
```

Expected: `Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1`.

3. Run the existing handler test suite to confirm all four pre-existing tests still pass unchanged:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetConsumptionHistoryHandlerTests"
```

Expected: `Passed! - Failed: 0, Passed: 4, Skipped: 0, Total: 4` — covering `Handle_ResolvesMaterialName_AndUnionsSources`, `Handle_ClampsPageSizeToMaximum`, `Handle_ConsumptionOnlyFilter_ExcludesLogs`, and `Handle_UnknownMaterial_FallsBackToPlaceholderName` (the last one specifically re-confirms the `"Neznámý"` fallback for an id with no resolvable name still works).

4. Run the full PackingMaterials test suite and a full build as a final regression check:

```bash
dotnet build
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PackingMaterials"
```

Expected: build succeeds with 0 errors; all PackingMaterials tests pass, 0 failed.

5. Commit:

```bash
git add backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetConsumptionHistory/GetConsumptionHistoryHandler.cs
git commit -m "#4027: Resolve material names from the current page instead of GetAllAsync in GetConsumptionHistoryHandler"
```

---

## Self-Review

**FR-1 coverage** (add `GetMaterialNamesByIdsAsync` to `IPackingMaterialRepository`, `WHERE Id IN (...)` implementation, empty-collection short-circuit, missing-id absence, deduplication) — covered by `add-repository-method`: interface signature added verbatim, `PackingMaterialRepository` implementation added verbatim (server-side `Select` + in-memory `ToDictionary`, per the arch review's EF Core translation guidance), and `PackingMaterialRepositoryGetMaterialNamesByIdsAsyncTests.cs` directly exercises all four acceptance criteria (targeted subset lookup, duplicate-id dedup, missing-id absence, empty-input-no-query via a disposed-context probe).

**FR-2 coverage** (handler drops `GetAllAsync`, calls `GetMaterialNamesByIdsAsync` exactly once with page-scoped distinct ids, empty-`records` path needs no full-table fallback, response shape/`"Neznámý"` fallback unchanged) — covered by `wire-handler-to-targeted-lookup`'s handler edit plus the `add-query-count-test` task's assertions (`GetAllAsyncCallCount == 0`, `GetMaterialNamesByIdsAsyncCallCount == 1`, ids are a subset of the page's distinct `PackingMaterialId`s). The empty-`records` path is structurally guaranteed (`records.Select(...).Distinct()` on an empty list yields an empty enumerable, which FR-1's own empty-collection test proves short-circuits with no DB round trip) and does not need a separate handler-level test. The unchanged `"Neznámý"` fallback and response shape are reconfirmed by re-running all four pre-existing `GetConsumptionHistoryHandlerTests.cs` tests unchanged in `wire-handler-to-targeted-lookup` step 3.

**FR-3 coverage** (update `MockPackingMaterialRepository`, add a query-count test proving zero `GetAllAsync` calls / exactly one `GetMaterialNamesByIdsAsync` call with page-scoped ids, all four existing handler tests keep passing, `dotnet build` and the PackingMaterials suite pass) — the mock update is `add-repository-method` step 5; the query-count test is the whole `add-query-count-test` task; the four existing handler tests are reconfirmed in `wire-handler-to-targeted-lookup` step 3; `dotnet build` and the full PackingMaterials suite are run in `wire-handler-to-targeted-lookup` step 4.

**Placeholder scan:** no "TBD", "similar to Task N", or unresolved references found — every code block is complete and self-contained, and every task repeats the exact code it needs rather than pointing at another task.

**Type/method-name consistency:** `GetMaterialNamesByIdsAsync(IEnumerable<int> packingMaterialIds, CancellationToken cancellationToken = default) : Task<IReadOnlyDictionary<int, string>>` is spelled identically across the interface (task 1 step 3), the `PackingMaterialRepository` implementation (task 1 step 4), `MockPackingMaterialRepository` (task 1 step 5), the existing `PackingMaterialsListQueryCountTests.cs` wrapper passthrough (task 1 step 6), the new `PackingMaterialRepositoryGetMaterialNamesByIdsAsyncTests.cs` (task 1 step 1), the new `GetConsumptionHistoryQueryCountTests.cs` wrapper (task 2 step 1), and the handler call site (task 3 step 1). `GetAllAsync` naming and the `"Neznámý"` fallback string are unchanged from the current codebase throughout.

An additional build hazard not explicitly called out in the source artifacts, but confirmed by direct inspection of the codebase before writing this plan, is folded into `add-repository-method`: the file-local `CountingRepositoryWrapper` inside the pre-existing `PackingMaterialsListQueryCountTests.cs` also implements `IPackingMaterialRepository` in full and would fail to compile the moment the interface gains a new member. Step 6 of `add-repository-method` adds the required passthrough there so the whole solution stays buildable after every task's commit.
