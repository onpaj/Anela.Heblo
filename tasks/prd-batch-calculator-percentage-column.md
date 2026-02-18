# PRD: Batch Calculator – Percentage Column per Ingredient

## Introduction

The batch calculator (Kalkulačka dávek) displays a table of ingredients with original and recalculated quantities. Users currently have no way to see what share of the total batch each ingredient represents. This feature adds a read-only percentage column so users can instantly understand the composition of the recalculated batch without doing manual math.

## Goals

- Show each ingredient's share of the recalculated batch as a percentage
- Keep it purely informational (read-only) — no interactive input required
- Require no backend changes — all data is already available in the existing API response
- Handle edge cases (zero batch size) gracefully

## User Stories

### US-001: Display percentage column in the results table

**Description:** As a user calculating a batch, I want to see what percentage of the total batch each ingredient represents so I can quickly understand the formula composition.

**Acceptance Criteria:**
- [ ] A new column header `%` appears between "Přepočítané množství" and "Skladem" in the results table
- [ ] Each ingredient row shows `(calculatedAmount / newBatchSize * 100).toFixed(2) + '%'`, e.g. `18.45%`
- [ ] If `newBatchSize` is zero or null, the cell shows `N/A` instead of crashing
- [ ] The column is read-only (no input, no click interaction)
- [ ] The column appears for both calculation modes: "Podle velikosti dávky" and "Podle ingredience"
- [ ] The column does NOT appear in the template-only table shown before the user triggers calculation (no `newBatchSize` available at that point)
- [ ] Typecheck passes (`npm run build` or `tsc --noEmit`)
- [ ] Verify in browser using dev-browser skill

## Functional Requirements

- **FR-1:** Add a `%` column header between "Přepočítané množství" and "Skladem" in the ingredient results table
- **FR-2:** For each ingredient row, compute the percentage as `calculatedAmount / newBatchSize * 100` and display it formatted to 2 decimal places with a `%` suffix (e.g. `18.45%`)
- **FR-3:** When `newBatchSize` is `0`, `null`, or `undefined`, display `N/A` in the percentage cell to avoid division by zero
- **FR-4:** The percentage column must be present in both calculation modes (by batch size and by ingredient)
- **FR-5:** The percentage column must be absent from the read-only "template" table rendered before a calculation has been performed

## Non-Goals

- No editable percentage input (typing a % to drive quantity recalculation is out of scope)
- No "original recipe %" column (only recalculated batch percentage is shown)
- No backend or API changes
- No sorting or filtering by percentage

## Design Considerations

- Column header: `%` (short, matches the numeric data style of the table)
- Cell value format: `18.45%` — number formatted with `toFixed(2)`, then `%` appended
- Fallback value: `N/A` (not `—` or empty) to make it obvious data is unavailable
- Column width: narrow, similar to the "KÓD" column — no need for wide cells
- Visual style: plain text, same as other numeric columns — no badge, no color coding

## Technical Considerations

- **Single file change:** `frontend/src/components/pages/ManufactureBatchCalculator.tsx`
  - Add one `<th>` in the results table header
  - Add one `<td>` in the ingredient row map function
- **Data already available:** Both `CalculatedBatchSizeResponse` and `CalculateBatchByIngredientResponse` carry `newBatchSize`; each `CalculatedIngredientDto` carries `calculatedAmount` — no new props, state, or API calls needed
- **Zero-guard:** Wrap the division in a conditional: `newBatchSize > 0 ? (calculatedAmount / newBatchSize * 100).toFixed(2) + '%' : 'N/A'`
- **Template table exclusion:** The component renders a second, static table before calculation results are shown — this table has no `newBatchSize` so the `%` column must not be added there

## Success Metrics

- The percentage value is visible immediately after clicking "Vypočítat" without any additional user action
- Values sum to approximately 100% across all ingredients (minor floating-point variance acceptable)
- No regression in existing batch calculator functionality

## Open Questions

- None — all requirements confirmed during brainstorming session on 2026-02-18
