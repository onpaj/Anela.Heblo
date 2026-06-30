# test(e2e): target Anela sidebar section instead of Personalni

**Source:** Nightly E2E triage — run 28147951139 (2026-06-25). Clears **3** core failures (sidebar-navigation).

## Problem
`frontend/test/e2e/core/sidebar-navigation.spec.ts:13/30/55` waits for `getByRole('button', { name: /Personální/i })` → not found.

## Root cause (confirmed)
The section containing the "Struktura" org-chart link is named **"Anela"** in current code (`frontend/src/components/Layout/Sidebar.tsx` id `"anela"`, items `Porady` + `Struktura`); the ARIA snapshot confirms a `button "Anela"`. There is no "Personální" section.

## Scope / files
- `frontend/test/e2e/core/sidebar-navigation.spec.ts` — target the **"Anela"** section; revisit the "between Sklad and Automatizace" positioning assertion to match the real sidebar order.

## Decision note
If product intends the label to be "Personální", rename the sidebar section instead and keep the tests. **Default: update the tests** to match the current "Anela" label.

## Acceptance criteria
- Sidebar-navigation specs target the real section and pass.
- The "open Struktura in new window" flow works.
