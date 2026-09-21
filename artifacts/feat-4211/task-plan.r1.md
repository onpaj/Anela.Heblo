# Remove Dead Manufacturing Severity Helpers Implementation Plan

**Goal:** Delete the two unused, ADR-006-violating exports `getManufacturingSeverityColorClass` and `getManufacturingSeverityDisplayText` from `frontend/src/api/hooks/useManufacturingStockAnalysis.ts`.

**Architecture:** Single-file deletion. No component, hook signature, or data-flow changes — both functions have zero consumers repo-wide (verified by grep in `spec.r1.md`), so removing them changes no runtime behavior.

**Tech Stack:** React, TypeScript.

---

### task: remove-dead-severity-helpers

**Files:**
- Modify: `frontend/src/api/hooks/useManufacturingStockAnalysis.ts:125-164`

**Context for the engineer:**
This file exports a React Query hook (`useManufacturingStockAnalysis`) plus a handful of small standalone helpers. Two of those helpers, `getManufacturingSeverityColorClass` (lines 126–143) and `getManufacturingSeverityDisplayText` (lines 146–163), each preceded by a one-line comment, have zero consumers anywhere in the repository (confirmed by a full-repo grep, including test files). `getManufacturingSeverityColorClass` also violates ADR-006 (`docs/architecture/development_guidelines.md`) because it returns hardcoded light-only Tailwind classes (e.g. `"text-red-600 bg-red-50"`) with no `dark:` variant.

The component that would logically use severity-to-color mapping, `frontend/src/components/pages/ManufacturingStockAnalysis.tsx`, already has its own inline, dark-mode-aware color logic and does not import either helper. Per the architecture review, the fix is outright deletion, not adding `dark:` variants to the dead helper — there is no real consumer to validate a color/shade choice against, so "fixing" it would just be more untested, speculative dead code.

Do **not** touch anything else in the file: the re-exported types/enums (including `ManufacturingStockSeverity`, which other files still import from this module), `getTimePeriodDisplayText`, and the `useManufacturingStockAnalysis` hook itself must remain exactly as they are.

- [ ] **Step 1: Confirm the exact lines to delete**

Run:
```bash
grep -n "Helper function to get severity\|^export const getManufacturingSeverity\|^};" frontend/src/api/hooks/useManufacturingStockAnalysis.ts
```
Expected output includes these lines (line numbers may have drifted slightly if the file changed since this plan was written — if so, use what this grep reports instead of the numbers below):
```
123:};
125:// Helper function to get severity color class
126:export const getManufacturingSeverityColorClass = (
143:};
145:// Helper function to get severity display text
146:export const getManufacturingSeverityDisplayText = (
163:};
```
Confirm line 124 and line 164 are both blank (`sed -n '124p;164p' frontend/src/api/hooks/useManufacturingStockAnalysis.ts` should print two empty lines) and that line 165 begins the next declaration's comment (`// Helper function to format Czech number`).

- [ ] **Step 2: Delete both helper functions and their comments**

Using the line numbers confirmed in Step 1, delete the block starting at the first helper's comment line (125) through the blank line immediately after the second helper's closing brace (164), inclusive — this removes both functions, both preceding comments, and the blank line between them, while leaving exactly one blank line (the original line 124) between the end of the `useManufacturingStockAnalysis` hook and the next declaration:

```bash
sed -i '125,164d' frontend/src/api/hooks/useManufacturingStockAnalysis.ts
```

After running this, the file must read (around the former line 123 area):
```ts
    staleTime: 1000 * 60 * 2, // 2 minutes (stock data changes less frequently than purchase orders)
  });
};

// Helper function to format Czech number
```
with no blank-line gap larger than one line and no trace of either deleted function or its comment.

- [ ] **Step 3: Verify nothing else changed**

Run:
```bash
git diff -- frontend/src/api/hooks/useManufacturingStockAnalysis.ts
```
Expected: a single contiguous deletion hunk removing exactly the two helper functions and their comments (lines 125–164 as confirmed in Step 1). No other line in the file is added, removed, or modified — the re-exported types/enums, `getTimePeriodDisplayText`, and the `useManufacturingStockAnalysis` hook must show no diff.

- [ ] **Step 4: Confirm both symbols are fully gone repo-wide**

Run:
```bash
grep -rn "getManufacturingSeverityColorClass\|getManufacturingSeverityDisplayText" --include="*.ts" --include="*.tsx" .
```
Expected: no output (zero matches anywhere in the repository).

- [ ] **Step 5: Build the frontend**

Run:
```bash
cd frontend && npm run build
```
Expected: build succeeds with no new TypeScript errors (in particular, no "cannot find name" errors, confirming no hidden consumer was missed).

- [ ] **Step 6: Lint the frontend**

Run:
```bash
cd frontend && npm run lint
```
Expected: no new lint errors introduced by this change.

- [ ] **Step 7: Run the existing frontend unit tests for the touched module and its consumer**

Run:
```bash
cd frontend && npx jest src/api/hooks/__tests__/useManufacturingStockAnalysis.test.tsx src/components/pages/__tests__/ManufacturingStockAnalysis.test.tsx
```
Expected: both test files pass unmodified — neither references the deleted symbols, so no test changes are needed (per spec FR-3).

- [ ] **Step 8: Commit**

```bash
git add frontend/src/api/hooks/useManufacturingStockAnalysis.ts
git commit -m "refactor(manufacture): remove dead severity helper exports (ADR-006)"
```

## Self-Review

**1. Spec coverage:** FR-1 (remove `getManufacturingSeverityColorClass`) is covered by Steps 1–2 and verified by Step 4. FR-2 (remove `getManufacturingSeverityDisplayText`) is covered by the same steps. FR-3 (no behavior change to `ManufacturingStockAnalysis.tsx` or any consumer) is verified by Step 3's diff scope check, Step 5's build, and Step 7's unchanged consumer test run. No spec requirement lacks a task.

**2. Placeholder scan:** No "TBD"/"TODO"/"handle edge cases" placeholders. Every step shows the actual command and exact expected output; no code is described without being shown.

**3. Type consistency:** No new types, functions, or method signatures are introduced — this plan only deletes two existing functions, so there is nothing to drift out of sync across tasks. `ManufacturingStockSeverity` (the only type the deleted functions consumed) is explicitly called out as untouched in both the design and this plan.
