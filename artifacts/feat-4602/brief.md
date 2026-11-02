# Coverage gap — `frontend/src/api/hooks/useManufactureOrders.ts`

**Line coverage:** 31% | **CI run:** 41210660028

## What is untested

`useUpdateManufactureOrderStatusMutation` has an optimistic update in `onMutate` and a cache rollback in `onError`, but no test file exists for this hook.

Key untested paths:

1. **`onMutate` guard** — `if (!old) return old;` silently does nothing when the query has not yet loaded. If this guard is wrong, the optimistic update could crash.
2. **`onError` rollback** — restores the previous order status from the context saved by `onMutate`. If the rollback does not fire (e.g., `previousOrders` is undefined), the UI permanently displays the wrong status after a failed mutation — with no error message and no way to recover without a page reload.

## Suggested tests

1. Successful mutation — verify the cache is updated with the new status.
2. Failed mutation — verify the cache is **restored** to the previous value via the `onError` rollback.
3. Mutation when cache is empty (`old` is undefined) — verify no crash and the guard short-circuits cleanly.
