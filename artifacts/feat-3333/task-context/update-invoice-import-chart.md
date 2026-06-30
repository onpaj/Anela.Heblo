### task: update-invoice-import-chart

**Goal:** Update `InvoiceImportChart.tsx` to use `item.date` as a `Date` directly rather than calling `parseISO` on a string.

**Files to change:**
- `frontend/src/components/charts/InvoiceImportChart.tsx` — remove `parseISO` call; use `item.date!` (non-null assertion) where the date is accessed

**Implementation steps:**
1. Locate every usage of `parseISO(item.date)` (or equivalent string-to-Date conversion) in the component.
2. Replace each with `item.date!` since the backend always populates the field and the type is now `Date`.
3. Remove any now-unused `parseISO` import from `date-fns` if it is no longer referenced elsewhere in the file.

**Acceptance criteria:**
- No TypeScript errors in this file.
- No `parseISO` call remains for `item.date` in this component.
- `npm run build` passes.

**Dependencies:** migrate-invoice-import-statistics-hook
