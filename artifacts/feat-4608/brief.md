# Coverage gap — `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Analytics/ShoptetPaging.cs`

**Line coverage:** 39.1% | **CI run:** 41210660028

## What is untested

No test file exists for `ShoptetPaging`.

The class comment states the invariant explicitly:

> A page series that ends early therefore does not read as an error, it reads as "that window held fewer orders than it did" — and nothing ever revisits it. So a truncated read has to throw rather than return short.

The cursor advance after a paged sync is **irreversible** — any silently-shortened read is permanently lost.  All three guard-throw paths are untested:

1. **`IsLastPage` — null paginator:**
   ```csharp
   if (paginator == null)
       throw new ShoptetOrderSyncException(...);
   ```
2. **`IsLastPage` — empty page when more pages promised:**
   ```csharp
   if (itemsOnPage == 0)
       throw new ShoptetOrderSyncException(...);
   ```
3. **`EnsureComplete` — collected < totalCount:**
   ```csharp
   throw new ShoptetOrderSyncException(...);
   ```

## Suggested tests

1. `IsLastPage` — `paginator == null` → verify `ShoptetOrderSyncException` is thrown.
2. `IsLastPage` — `page >= paginator.PageCount` → verify `true` is returned (last page, no throw).
3. `IsLastPage` — `itemsOnPage == 0` with more pages remaining → verify `ShoptetOrderSyncException` is thrown.
4. `IsLastPage` — `itemsOnPage > 0` with more pages remaining → verify `false` is returned.
5. `EnsureComplete` — `collected < totalCount` → verify `ShoptetOrderSyncException` is thrown.
6. `EnsureComplete` — `collected >= totalCount` → verify no exception.
7. `EnsureComplete` — `paginator == null` → verify no exception (treated as complete).
