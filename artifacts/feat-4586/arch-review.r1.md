# Architecture Review: PurchaseOrderForm test coverage

## Skip Design: true
Test-only change; no new or changed UI.

## Architectural Fit Assessment
Fits existing frontend test conventions (Jest + RTL in `frontend/src/components/pages/__tests__/`, e.g. `PurchaseStockAnalysis.test.tsx` which already mocks the planning-list context). No production code changes.

## Proposed Architecture

### Component Overview
One new test file `frontend/src/components/pages/__tests__/PurchaseOrderForm.test.tsx` rendering `PurchaseOrderForm` with mocked collaborators.

### Key Design Decisions

#### Decision 1: Test through the component, do not export helpers
**Options considered:** export `normalizeSupplierName`; extract to util; test via rendering.
**Chosen approach:** test via rendering with a mocked `usePurchasePlanningList` and supplier selection driven by mocked `PurchaseOrderHeader`.
**Rationale:** Surgical-change rule in CLAUDE.md; coverage goal does not justify refactoring. Mocking `PurchaseOrderHeader` (jest.mock) with a button that calls its `onSupplierChange`-style prop lets tests set `formData.selectedSupplier` deterministically; verify actual prop names by reading the component before writing.

#### Decision 2: Mock fetch at the api client boundary
Mock `../../../api/client` `getAuthenticatedApiClient` to return `{ baseUrl: "http://test", http: { fetch: jest.fn() } }`. The component uses a dynamic `import()`; jest.mock handles this.

#### Decision 3: Characterise, don't fix
Zero-price fallback and substring false positives are asserted as current behaviour with explanatory test names/comments.

## Implementation Guidance

### Directory / Module Structure
`frontend/src/components/pages/__tests__/PurchaseOrderForm.test.tsx` (only new file).

### Interfaces and Contracts
Mock: `usePurchasePlanningList` -> `{ items, removeItem: jest.fn() }`; `useCreatePurchaseOrderMutation`, `useUpdatePurchaseOrderMutation`, `usePurchaseOrderDetailQuery`; child components `PurchaseOrderHeader`, `PurchaseOrderLines`, `MaterialResolver` as lightweight stubs exposing form state (e.g. rendering lines count and unit prices as data-testid).

### Data Flow
Select supplier (stub) -> `formData.selectedSupplier` -> memo filters context items -> list rendered -> click add -> fetch mock -> setFormData -> stub lines render -> `removeItem` asserted.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Planning list section only renders under certain conditions (isOpen, UI toggles) | Low | Read lines ~520-600 before writing tests |
| Over-mocking hides regressions | Medium | Keep real component logic; stub only children/hooks |
| Console noise from catch path | Low | spy `console.error` |

## Specification Amendments
None.

## Prerequisites
None. Run `npm test -- PurchaseOrderForm` and `npm run lint` in `frontend/`.
