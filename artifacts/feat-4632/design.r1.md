# Design: SupplierAutocomplete unit test coverage

## Component Design
Single test module `SupplierAutocomplete.test.tsx` with `describe` blocks: rendering, keyboard navigation (input and option handlers), clear path, selection, click-outside. `useSupplierSearch` is mocked via a `mockUseSupplierSearch` jest.fn; a `renderComponent(props)` helper supplies defaults (`value: null`, `onSelect: jest.fn()`). Fixtures: three `SupplierDto` objects. No production component changes.

## Data Schemas
Mock return: `{ suppliers: SupplierDto[], isLoading: boolean }`. `SupplierDto` fixture: `{ id, name, code, email?, phone?, note? }`.
