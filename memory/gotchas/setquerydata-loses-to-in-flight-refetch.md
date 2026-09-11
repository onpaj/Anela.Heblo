# `setQueryData` loses to an in-flight refetch a sibling mutation started

**Symptom.** A mutation folds fresh rows into a react-query cache with `setQueryData`, the UI
shows them, and a moment later they silently revert to older values. No error, no spinner.

**Cause.** React Query does not discard a fetch that is already in flight because a
`setQueryData` happened meanwhile. Whatever the fetch resolves to overwrites the cache when it
lands. Any sibling mutation on the same query key that calls `invalidateQueries` opens this
window, and the window is as wide as the refetch is slow.

Concretely on the price comparison screen (`useProductPricing.ts`): `useSetProductPrice`
invalidates the divergence query on success and on `ProductPriceFlexiWriteFailed`, starting a
whole-catalogue refetch across Shoptet and Flexi. If the operator then clicks *Synchronizovat*,
the scoped sync returns much faster, merges — and the refetch lands last. Worse than stale: the
report refetch reads Flexi with `forceReload: false`, so the freshly force-reloaded prices are
replaced by the five-minute cached ceník, which is precisely what the sync exists to defeat.

**Fix.** `await queryClient.cancelQueries({ queryKey })` before `setQueryData`, the same
ordering the optimistic-update pattern uses.

**Testing trap — a race test passes vacuously by default.** Resolving the deferred promise and
then asserting proves nothing: the assertion runs before React Query can apply the result, so
the test is green whether or not the bug exists. It only becomes a real test once it waits for
the query to settle:

```ts
resolveSlowRefetch(staleReport);
await waitFor(() => expect(queryClient.isFetching({ queryKey })).toBe(0));
// only now assert
```

The first version of `PriceDivergenceReport.syncRace.test.tsx` passed against the unfixed code
for exactly this reason. Always confirm a race test fails before the fix.
