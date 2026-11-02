# Coverage gap — `frontend/src/api/hooks/useStockTaking.ts`

**Line coverage:** 26.7% | **CI run:** 41210660028

## What is untested

`useSubmitStockTaking` mutation is entirely absent from the test suite.  The existing test file (`frontend/src/api/hooks/__tests__/useStockTaking.test.tsx`) only exercises `useStockTakingHistory`.

The critical untested branch is:

```ts
onSuccess: (_, variables) => {
  if (!variables.softStockTaking) {
    // Only hard stock-taking invalidates / updates the local cache
    queryClient.setQueryData(...)
  }
}
```

If the `softStockTaking` guard is accidentally removed or inverted, a soft stock-take will incorrectly overwrite the cached inventory state, and a hard stock-take will silently leave stale data in the UI — neither shows an error.

## Suggested tests

1. Hard stock-taking (`softStockTaking: false`) — verify the React Query cache is updated with the new quantities.
2. Soft stock-taking (`softStockTaking: true`) — verify the cache is **not** touched.
3. Mutation error — verify no cache write happens.
