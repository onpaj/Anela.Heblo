# Design: ManufactureBatchCalculator test coverage for computePercentage edge cases and batch-size fallback

## Component Design

This is a test-only change. There is no application UI to design — `arch-review.r1.md` sets `Skip Design: true` and confirms no production component, screen, or visual behavior changes. The design below covers the structure of the test file and its mocks, which are the only artifacts being added.

### Test file (single, extended in place)

`frontend/src/components/pages/__tests__/ManufactureBatchCalculator.test.tsx` — no new files. Structure:

```
describe('computePercentage helper')
  ├─ [existing cases: null, undefined, 0, negative, NaN, normal, rounding]
  └─ +FR-1: Infinity / -Infinity → 'N/A'

describe('ManufactureBatchCalculator')
  ├─ [existing smoke test: empty-state render]
  ├─ +FR-2: batch-size fallback precedence (3 cases: MMQ wins / BOM fallback / both-falsy-no-call)
  ├─ +FR-3: URL parameter auto-selection (?productCode=X&batchSize=Y overrides template default)
  └─ +FR-4: calculation-mode toggle (default mode, switch to ingredient mode, correct calc fn per mode)
```

### Mock restructuring: `useManufactureBatch`

Responsibility: let each test configure `getBatchTemplate` / `calculateBySize` / `calculateByIngredient` independently, replacing the current fixed `{ success: false }` factory.

- Module-scoped `jest.fn()` references (`mockGetBatchTemplate`, `mockCalculateBySize`, `mockCalculateByIngredient`), named with the `mock` prefix required by Jest's hoist allow-list, referenced from the `jest.mock('../../../api/hooks/useManufactureBatch', ...)` factory.
- `beforeEach` resets each mock and restores a default `mockResolvedValue({ success: false })`, preserving the existing smoke test's assumption unchanged.
- Individual tests override via `mockResolvedValueOnce` (or `mockResolvedValue` for the duration of that test) to supply FR-specific response shapes.
- Contract returned by the mocked hook stays `{ getBatchTemplate, calculateBySize, calculateByIngredient, isLoading }` — matches the real hook's consumed surface; `error` is omitted, matching current behavior.

### Mock enhancement: `CatalogAutocomplete`

Responsibility: give tests a seam to trigger `handleProductSelect` the same way a manual product pick does (the component's internal callback is not exported).

- Extends the existing inert `<div data-testid="catalog-autocomplete" />` stub into a small functional mock that accepts the real `onSelect` prop and exposes a `data-testid="catalog-autocomplete-select"` trigger (e.g. a button) wired to call `onSelect(product)` with a per-test `CatalogItemDto` fixture.
- Stays stateless and minimal — it does not attempt to reproduce the real component's search/filter UI, only the `onSelect` handoff contract.
- Only the test-file-local mock changes; the real `frontend/src/components/common/CatalogAutocomplete.tsx` is untouched.

### Other existing mocks (unchanged)

`InventoryStatusCell`, `ManufactureInventoryDetail`, `CatalogDetail` remain the current inert mocks — none of FR-1–FR-4 requires interaction with them.

## Data Schemas

No new or changed data entities or API contracts. Tests construct instances/fixtures of existing generated DTOs and response shapes purely as in-memory mock inputs/outputs:

- **`CatalogItemDto`** (from `../../../api/generated/api-client`) — `{ productCode: string, productName: string, type: ProductType.SemiProduct }`. Used both for the URL-effect's auto-constructed instance (FR-3) and as the fixture passed through the mocked `CatalogAutocomplete.onSelect` (FR-2, FR-4). Constructed via the real generated class for type-correctness, not a plain literal.
- **`CalculatedBatchSizeResponse`-shaped object** — resolved value of mocked `getBatchTemplate` / `calculateBySize`: `{ success: boolean, productCode, productName, originalBatchSize: number, newBatchSize: number, scaleFactor, ingredients: [] }`. Plain object literals are sufficient (component only reads properties, never calls instance methods).
- **`CalculateBatchByIngredientResponse`-shaped object** — resolved value of mocked `calculateByIngredient`, same plain-literal convention, used only in FR-4.

No database, persistence, or REST contract changes.
