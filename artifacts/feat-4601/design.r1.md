# Design: Test coverage for useSubmitStockTaking

## Component Design
- Test file: `frontend/src/api/hooks/__tests__/useStockTaking.test.tsx` (extend).
- Helper: `createClientWrapper()` returns `{ queryClient, wrapper }` (QueryClient with `queries.retry=false`, `mutations.retry=false`); existing `createWrapper` remains.
- Mocks: `getAuthenticatedApiClient` -> `{ stockTaking_SubmitStockTaking: jest.fn() }`; `useToast` -> `{ showError: mockShowError }`.
- Test cases (describe `useSubmitStockTaking`):
  1. hard (`softStockTaking: false`): seeds detail + inventory cache, mutate, assert both updated, other product untouched, `setQueryData` and `setQueriesData` called.
  2. omitted `softStockTaking`: treated as hard; request sent with `softStockTaking: false`.
  3. soft (`true`): spies not called, seeded values unchanged, `invalidateQueries` called.
  4. missing `item.stock` / `items` in cache: returned unchanged.
  5. error: API rejects -> no cache write, `showError` called with title and message / fallback, `{ duration: 5000 }`.

## Data Schemas
- Detail key: `[...QUERY_KEYS.catalog, "detail", productCode, 1]`, value `{ item: { name, stock: { available, eshop } } }`.
- Inventory key: `[...QUERY_KEYS.catalog, "inventory", {...}]`, value `{ items: [{ productCode, stock: { available, eshop } }] }`.
- Mutation variables: `{ productCode: string; targetAmount: number; softStockTaking?: boolean }`.
