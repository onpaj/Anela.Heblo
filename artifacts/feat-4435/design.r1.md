# Design: ManufactureOrderFilters tests

## Component Design
Single test file `frontend/src/components/manufacture/list/__tests__/ManufactureOrderFilters.test.tsx`.

- Helper `renderFilters()` returns `{ onFiltersChange, onApplyFilters }` (`jest.fn()`, latter `mockResolvedValue(undefined)`).
- Helper `expand()` clicks the "Filtry" button to reveal the panel.
- Mocks: `useManufactureSettingsQuery` returns `{ data: { manufactureGroupId: "g1" } }`; `CatalogAutocomplete` mock = `<input placeholder={placeholder} value={value} onChange={e => onSelect(e.target.value || null)} />`; `ResponsiblePersonCombobox` mock similarly with `onChange`.
- Test groups: apply mapping (all fields, Enter key), empty-to-null, product-code clearing, manual-action select, collapsed quick toggle (fake timers, stale-closure characterization), reset, collapsed summary text.

## Data Schemas
`GetManufactureOrdersRequest` (existing): `orderNumber, state, dateFrom, dateTo, responsiblePerson, productCode, erpDocumentNumber, manualActionRequired, lotNumber` all `T | null`.
