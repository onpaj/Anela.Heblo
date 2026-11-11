### task: lots-refresh-logging
FR-2 in `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMetaRefreshService.cs`.
- In `RefreshLotsData`: Stopwatch around `_lotsClient.GetAsync(cancellationToken: ct)`, materialize list, `_cacheStore.SetLotsData(list)`, then `_logger.LogInformation("Catalog.LotsRefreshCompleted rows={Rows} elapsedMs={ElapsedMs}", ...)`. Exceptions propagate unchanged (no try/catch).
- Add a test in the existing Catalog tests area (find with `grep -rl CatalogMetaRefreshService backend/test`; create `CatalogMetaRefreshServiceLotsTests.cs` if none) asserting the event is emitted once with row count and that a throwing client propagates.
- Verify: `dotnet test backend --filter CatalogMetaRefresh` passes.

