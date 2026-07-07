# Specification: Leaflet Generator — Graphite Dark Mode Compliance (ADR-006)

## Summary
Add Tailwind `dark:` class variants to the three Leaflet Generator feature components (`LeafletGeneratorPage.tsx`, `LeafletDocumentsTab.tsx`, `LeafletGenerateTab.tsx`) so they render with correct contrast and surface colors under the "Graphite" dark theme, per ADR-006 and the token mapping in `docs/design/dark-mode-conversion-guide.md`. This is a purely additive, surgical CSS-class change — no markup structure, props, logic, or light-mode appearance may change.

## Background
ADR-006 (accepted 2026-06-25) requires every frontend component that renders color to work correctly in both light and Graphite dark mode, across routes, modals, tab panels, tables, and badges. The Leaflet Generator feature (`frontend/src/features/leaflet-generator/`) was built with light-only Tailwind utility classes (`bg-white`, `text-gray-*`, `border-gray-*`, raw `bg-{color}-100`/`text-{color}-800` badges, etc.) and has no `dark:` variants anywhere. A daily automated arch-review routine flagged this on 2026-07-04. The feature includes a tab shell, a data table with filters/sorting/pagination, a delete-confirmation modal, and a generation form with loading skeletons and error banners — the same set of pattern types ADR-006 explicitly calls out (tables, modals, tab panels, badges).

The project's global dark-mode conversion guide (`docs/design/dark-mode-conversion-guide.md`) defines the canonical raw-Tailwind-class → `dark:` mapping and the custom Graphite color tokens available in `frontend/tailwind.config.js` (`graphite.bg`, `.surface`, `.surface-2`, `.hover`, `.chrome`, `.border`, `.border-strong`, `.text`, `.muted`, `.faint`, `.accent`, `.accent-strong`, `.accent-ink`), plus `boxShadow.soft-dark`. This spec applies that mapping mechanically to the three affected files, enumerating every raw color utility class found in each and its required `dark:` addition.

Out of the three files listed in the brief, `LeafletUploadTab.tsx` and `LeafletForm.tsx`/`LeafletResult.tsx`/`LeafletChunkDetailModal.tsx` are siblings in the same directory but are **not** in scope for this change (see Out of Scope) — only the three files named in the brief are covered.

## Functional Requirements

### FR-1: `LeafletGeneratorPage.tsx` — page header and tab navigation dark-mode support
Add `dark:` variants to the page header text/icon and the tab-bar underline/text colors so the tab shell renders correctly on the Graphite background.

Concrete changes (current line numbers in the file as read; may shift slightly but content is unique and locatable by string match):

- Line 27 — icon: `<FileText className="w-6 h-6 text-blue-600" />`
  - `text-blue-600` is a non-accent-context icon color paired with a heading; treat consistent with the tab active-accent usage below → add `dark:text-graphite-accent`.
- Line 28 — heading: `className="text-2xl font-semibold text-gray-900"`
  - `text-gray-900` → add `dark:text-graphite-text`.
- Line 31 — tab bar container: `className="border-b border-gray-200"`
  - `border-gray-200` → add `dark:border-graphite-border`.
- Lines 37–41 — tab button template literal:
  ```
  className={`py-2 text-sm font-medium border-b-2 transition-colors ${
    activeTab === tab.id
      ? 'border-blue-600 text-blue-600'
      : 'border-transparent text-gray-500 hover:text-gray-700'
  }`}
  ```
  - Active branch `'border-blue-600 text-blue-600'` → `'border-blue-600 text-blue-600 dark:border-graphite-accent dark:text-graphite-accent'`.
  - Inactive branch `'border-transparent text-gray-500 hover:text-gray-700'` → `'border-transparent text-gray-500 hover:text-gray-700 dark:text-graphite-muted dark:hover:text-graphite-text'` (guide has no explicit `hover:text-gray-700` rule; `hover:text-gray-700` is a "move toward primary text on hover" affordance, so its dark equivalent is `dark:hover:text-graphite-text`, consistent with the muted→text hover pattern used for the primary heading color — see Open Questions/assumption A1).

**Acceptance criteria:**
- All four raw-color classes identified above have a corresponding `dark:` class appended in the same `className` string/branch, with light classes unchanged.
- The active tab indicator (bottom border + label) uses `dark:border-graphite-accent dark:text-graphite-accent` in Graphite mode.
- The inactive tab label is `dark:text-graphite-muted` at rest and `dark:hover:text-graphite-text` on hover.
- No structural, prop, or logic changes; `tabs` array and conditional rendering (lines 18–22, 50–52) are untouched.

### FR-2: `LeafletDocumentsTab.tsx` — filter bar, table, badges, dialog, pagination
This is the highest-impact file (515 lines, full table/filter/modal/pagination UI). Add `dark:` variants to every raw-color utility across five sub-regions: the `StatusBadge` color map, the `ConfirmDeleteDialog`, the `SortableHeader`, the filter bar, and the table + pagination footer.

**FR-2a: `StatusBadge` color map (lines 12–24)**
The `colorMap` object maps status → Tailwind classes. Per guide rule 5 ("For color maps... add dark variants to each value string") and the status/semantic mapping table:

- `indexed: 'bg-green-100 text-green-800'` → `'bg-green-100 text-green-800 dark:bg-emerald-900/30 dark:text-emerald-300'`
- `processing: 'bg-yellow-100 text-yellow-800'` → `'bg-yellow-100 text-yellow-800 dark:bg-amber-900/30 dark:text-amber-300'`
- `failed: 'bg-red-100 text-red-800'` → `'bg-red-100 text-red-800 dark:bg-red-900/30 dark:text-red-300'`
- fallback `?? 'bg-gray-100 text-gray-800'` → `'bg-gray-100 text-gray-800 dark:bg-graphite-surface-2 dark:text-graphite-muted'` (guide's `bg-gray-100` → `dark:bg-graphite-surface-2` background rule; `text-gray-800` → `dark:text-graphite-muted` text rule; this is the "unknown status" fallback pill, not a semantic color, so it follows the neutral surface/text mapping rather than the status-pill `~900/30`/`~300` pattern).

**FR-2b: `ConfirmDeleteDialog` (lines 26–57)**
- Line 33 — `className="bg-white rounded-lg shadow-xl p-6 max-w-sm w-full"`
  - `bg-white` → add `dark:bg-graphite-surface`; `shadow-xl` is not in the guide's shadow list (only `shadow`/`shadow-sm`/`shadow-md`) — leave `shadow-xl` unchanged per guide rule 1 (don't invent mappings beyond the table); see Open Questions A2 for the explicit assumption.
  - Heading `text-lg font-semibold` (line 34) has no color utility — no change needed.
- Line 35 — `className="text-sm text-gray-600 mb-4"` → `text-gray-600` → add `dark:text-graphite-muted`.
- Line 39 — `className="text-sm text-red-600 mb-3"` → `text-red-600` → add `dark:text-red-400` (guide: `text-red-600`/`text-red-500` → `dark:text-red-400`).
- Line 44 — Cancel button `className="px-4 py-2 text-sm rounded border border-gray-300 hover:bg-gray-50"` → `border-gray-300` → add `dark:border-graphite-border`; `hover:bg-gray-50` → add `dark:hover:bg-white/5`.
- Line 50 — Confirm/delete button `className="px-4 py-2 text-sm rounded bg-red-600 text-white hover:bg-red-700"` — this is a solid destructive-action button (bg-red-600 + text-white), not a status pill or neutral surface; the guide does not map solid semantic action buttons. Leave unchanged (see Open Questions A3): solid `bg-red-600`/`hover:bg-red-700`/`text-white` combinations already have sufficient contrast against both light and dark backgrounds and are not covered by any guide rule, so no `dark:` classes are added here (same treatment applies to `bg-indigo-600 hover:bg-indigo-700 text-white` and `bg-gray-500 hover:bg-gray-600 text-white` buttons in FR-2d below).

**FR-2c: `SortableHeader` (lines 59–87)**
- Line 71 — `className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider cursor-pointer hover:bg-gray-100 select-none"`
  - `text-gray-500` → add `dark:text-graphite-muted`.
  - `hover:bg-gray-100` → add `dark:hover:bg-white/5`.
- Lines 77–79 and 80–82 — chevron icon classNames (ternary, ChevronUp/ChevronDown):
  ```
  `h-3 w-3 ${isActive && !sortDescending ? 'text-indigo-600' : 'text-gray-300'}`
  `h-3 w-3 -mt-1 ${isActive && sortDescending ? 'text-indigo-600' : 'text-gray-300'}`
  ```
  - Per guide rule 4 (add the dark variant inside each ternary branch): `'text-indigo-600'` → `'text-indigo-600 dark:text-graphite-accent'`; `'text-gray-300'` → `'text-gray-300 dark:text-graphite-faint'`.

**FR-2d: Filter bar (lines 272–347)**
- Line 273 — `className="bg-white shadow rounded-lg p-4 mb-4"` → `bg-white` → add `dark:bg-graphite-surface`; `shadow` → add `dark:shadow-soft-dark`.
- Line 277 — `<Filter className="h-4 w-4 text-gray-400 mr-2" />` → `text-gray-400` → add `dark:text-graphite-faint`.
- Line 278 — `<span className="text-sm font-medium text-gray-900">` → `text-gray-900` → add `dark:text-graphite-text`.
- Line 284 — `<Search className="h-4 w-4 text-gray-400" />` → `text-gray-400` → add `dark:text-graphite-faint`.
- Line 292 — filename input: `className="focus:ring-indigo-500 focus:border-indigo-500 block w-full pl-10 pr-3 py-2 sm:text-sm border-gray-300 rounded-md"`
  - This is a raw input (not using `.input` design-system class). Per guide's "Inputs / selects / textareas (raw)" rule, append the full input dark bundle: `dark:bg-graphite-surface-2 dark:border-graphite-border dark:text-graphite-text dark:placeholder-graphite-faint`. `focus:ring-indigo-500`/`focus:border-indigo-500` are left as-is per guide rule ("focus rings OK as-is").
- Line 304 — status `<select>`: `className="block w-full pl-3 pr-10 py-2 text-base border-gray-300 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm rounded-md"` → same raw-input treatment: append `dark:bg-graphite-surface-2 dark:border-graphite-border dark:text-graphite-text`. (No placeholder on a `<select>`, so `dark:placeholder-graphite-faint` is omitted.)
- Line 320 — content-type `<select>`: identical classes to line 304 → same treatment: append `dark:bg-graphite-surface-2 dark:border-graphite-border dark:text-graphite-text`.
- Line 335 — "Filtrovat" button: `className="bg-indigo-600 hover:bg-indigo-700 text-white font-medium py-2 px-4 rounded-md transition-colors duration-200 text-sm"` — solid primary action button; no guide mapping for solid indigo buttons. Leave unchanged (consistent with FR-2b's A3 assumption).
- Line 341 — "Vymazat" button: `className="bg-gray-500 hover:bg-gray-600 text-white font-medium py-2 px-3 rounded-md transition-colors duration-200 text-sm"` — solid secondary action button with `text-white`; sufficient contrast on any background. Leave unchanged (same A3 assumption).

**FR-2e: Empty state (line 350)**
- `className="text-gray-500 text-sm text-center py-8"` → `text-gray-500` → add `dark:text-graphite-muted`.

**FR-2f: Table (lines 355–400)**
- Line 356 — `className="min-w-full divide-y divide-gray-200 text-sm"` → `divide-gray-200` → add `dark:divide-graphite-border`.
- Line 357 — `<thead className="bg-gray-50">` → `bg-gray-50` → add `dark:bg-graphite-surface-2`.
- Line 363 — `<th className="px-6 py-3" />` — no color utility, no change.
- Line 366 — `<tbody className="divide-y divide-gray-100">` → `divide-gray-100` → add `dark:divide-graphite-border`.
- Line 370 — row template literal: `` `hover:bg-gray-50 ${doc.firstChunkId ? 'cursor-pointer' : ''}` `` → `hover:bg-gray-50` → add `dark:hover:bg-white/5` (the ternary branch itself, `cursor-pointer`/`''`, has no color and needs no change).
- Line 373 — `className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900"` → `text-gray-900` → add `dark:text-graphite-text`.
- Lines 377 and 380 — both `className="px-6 py-4 whitespace-nowrap text-sm text-gray-500"` → `text-gray-500` → add `dark:text-graphite-muted` (both occurrences).
- Line 390 — delete icon button: `className="text-gray-400 hover:text-red-600 transition-colors"` → `text-gray-400` → add `dark:text-graphite-faint`; `hover:text-red-600` is a semantic danger hover with no guide mapping — leave unchanged (danger-red hover retains sufficient contrast in dark mode; see Open Questions A4).

**FR-2g: Pagination footer (lines 403–491)**
- Line 403 — `className="bg-white px-3 py-2 flex items-center justify-between border-t border-gray-200 text-xs"` → `bg-white` → add `dark:bg-graphite-surface`; `border-gray-200` → add `dark:border-graphite-border`.
- Lines 408 and 415 (mobile Předchozí/Další buttons) — `className="relative inline-flex items-center px-2 py-1 border border-gray-300 text-xs font-medium rounded text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"` (both identical) → `border-gray-300` → add `dark:border-graphite-border`; `text-gray-700` → add `dark:text-graphite-muted`; `bg-white` → add `dark:bg-graphite-surface`; `hover:bg-gray-50` → add `dark:hover:bg-white/5`.
- Line 422 — `className="text-xs text-gray-600"` (result count) → `text-gray-600` → add `dark:text-graphite-muted`.
- Line 427 — `className="text-xs text-gray-600"` ("Zobrazit:" label) → `text-gray-600` → add `dark:text-graphite-muted`.
- Line 434 — page-size `<select>`: `className="border border-gray-300 rounded px-1 py-0.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-transparent"` → raw select, append `dark:bg-graphite-surface-2 dark:border-graphite-border dark:text-graphite-text` (focus ring untouched).
- Line 444 — `className="relative z-0 inline-flex rounded shadow-sm -space-x-px"` → `shadow-sm` → add `dark:shadow-soft-dark`.
- Line 450 — prev-page nav button: `className="relative inline-flex items-center px-1 py-1 rounded-l border border-gray-300 bg-white text-xs font-medium text-gray-500 hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"` → `border-gray-300` → `dark:border-graphite-border`; `bg-white` → `dark:bg-graphite-surface`; `text-gray-500` → `dark:text-graphite-muted`; `hover:bg-gray-50` → `dark:hover:bg-white/5`.
- Lines 470–474 — numbered page-button ternary:
  ```
  pageNum === pageNumber
    ? 'z-10 bg-indigo-50 border-indigo-500 text-indigo-600'
    : 'bg-white border-gray-300 text-gray-500 hover:bg-gray-50'
  ```
  - Active branch → `'z-10 bg-indigo-50 border-indigo-500 text-indigo-600 dark:bg-graphite-accent/10 dark:border-graphite-accent dark:text-graphite-accent'` (guide's accent bg/border/text active mapping).
  - Inactive branch → `'bg-white border-gray-300 text-gray-500 hover:bg-gray-50 dark:bg-graphite-surface dark:border-graphite-border dark:text-graphite-muted dark:hover:bg-white/5'`.
- Line 484 — next-page nav button, same classes as line 450 → same treatment: `border-gray-300` → `dark:border-graphite-border`; `bg-white` → `dark:bg-graphite-surface`; `text-gray-500` → `dark:text-graphite-muted`; `hover:bg-gray-50` → `dark:hover:bg-white/5`.

**FR-2h: Loading skeleton (lines 256–263) and error text (line 267)**
- Line 260 — `className="h-10 bg-gray-100 rounded"` → `bg-gray-100` → add `dark:bg-graphite-surface-2` (per guide: `bg-gray-100` → `dark:bg-graphite-surface-2`, panels/badges/inputs category, applies equally to skeleton blocks — see Open Questions A5 for why this is not mapped to `bg-graphite-hover` despite visually being a "loading stripe").
- Line 267 — `className="text-red-600 text-sm"` → `text-red-600` → add `dark:text-red-400`.

**Acceptance criteria (FR-2, all sub-parts):**
- Every raw Tailwind color utility enumerated above has the specified `dark:` class appended in place, in the same string/branch, without altering any light-mode class, JSX structure, prop, or handler.
- The `StatusBadge` component renders all three named statuses (`indexed`, `processing`, `failed`) plus the unknown-status fallback with visibly distinct, sufficiently-contrasted pill colors in Graphite mode.
- The delete-confirmation modal's backdrop, panel, body text, and error text are legible on the Graphite background; the two action buttons are unchanged (already high-contrast).
- The table header, row dividers, hover state, cell text, and pagination controls all use Graphite surface/border/text tokens instead of `white`/`gray-*` in dark mode.
- The active vs. inactive sort-chevron and active vs. inactive page-number states remain visually distinguishable in dark mode via `dark:text-graphite-accent` / `dark:bg-graphite-accent/10` etc.
- Visual/manual check (or Playwright screenshot diff if available) confirms no light-mode regression: toggling the theme back to light must render pixel-identical to the current (pre-change) light mode.

### FR-3: `LeafletGenerateTab.tsx` — error banners and loading skeleton
- Lines 63–67 — error banner ternary:
  ```
  errorBanner.kind === 'insufficient'
    ? 'bg-amber-100 text-amber-900'
    : 'bg-red-100 text-red-900'
  ```
  - `'bg-amber-100 text-amber-900'` → `'bg-amber-100 text-amber-900 dark:bg-amber-900/30 dark:text-amber-300'` (guide's yellow/amber status mapping applied to the amber warning banner).
  - `'bg-red-100 text-red-900'` → `'bg-red-100 text-red-900 dark:bg-red-900/30 dark:text-red-300'` (guide's red status mapping).
- Lines 88–90 — loading skeleton bars: `className="h-4 bg-gray-200 rounded w-3/4"`, `className="h-4 bg-gray-200 rounded"`, `className="h-4 bg-gray-200 rounded w-5/6"` (three sibling divs, identical `bg-gray-200` base) → each `bg-gray-200` → add `dark:bg-graphite-hover` (guide: `bg-gray-200` → `dark:bg-graphite-hover`).

**Acceptance criteria:**
- Both error-banner variants (`insufficient` / `transient`) render with the `~900/30` background + `~300` text pattern in Graphite mode, matching the badge/status convention used elsewhere in the app.
- All three loading-skeleton bars use `dark:bg-graphite-hover` and are visually distinguishable from the Graphite page background (no "bright stripe" regression called out in the original finding).
- `LeafletForm` and `LeafletResult` (rendered as children on lines 74 and 93) are out of scope and untouched — this file's changes are limited to the banner and inline skeleton markup owned directly by `LeafletGenerateTab`.

## Non-Functional Requirements

### NFR-1: Performance
No runtime performance impact is expected or in scope: this change only adds static Tailwind utility classes (compiled at build time), with zero new JS logic, state, re-renders, or bundle-size-relevant dependencies. `npm run build` output size may grow negligibly (a handful of already-whitelisted utility classes/token colors defined in `tailwind.config.js`); no budget or benchmark is required.

### NFR-2: Security
Not applicable. No auth, data-handling, API surface, or permission logic is touched. `hasPermission('marketing.leaflet.write')` gating (`LeafletGeneratorPage.tsx` line 13) and all `canUpload`/`canDelete` prop plumbing must remain byte-for-byte unchanged.

## Data Model
Not applicable — this is a presentation-layer styling change. No entities, DTOs, or API contracts are modified. (For reference, `LeafletDocumentSummary` and the `useLeaflet` query/mutation hooks in `frontend/src/api/hooks/useLeaflet.ts` are consumed as-is and must not be touched.)

## API / Interface Design
Not applicable — no backend endpoints, MediatR handlers, or generated API client changes. This spec only touches JSX `className` attributes in three existing React components:
- `frontend/src/features/leaflet-generator/LeafletGeneratorPage.tsx`
- `frontend/src/features/leaflet-generator/LeafletDocumentsTab.tsx`
- `frontend/src/features/leaflet-generator/LeafletGenerateTab.tsx`

No new components, hooks, routes, or props are introduced. The dark theme itself is activated globally via Tailwind's `darkMode: 'class'` strategy (already configured in `frontend/tailwind.config.js`); this change only supplies the missing `dark:` variants so these three files respond correctly when the `dark` class is present on a root ancestor.

## Dependencies
- `frontend/tailwind.config.js` — must already define the `graphite` color scale (`bg`, `surface`, `surface-2`, `hover`, `chrome`, `border`, `border-strong`, `text`, `muted`, `faint`, `accent`, `accent-strong`, `accent-ink`) and `boxShadow.soft-dark`. Confirmed present at lines 48–62 and 67 of the current config — no config changes are needed for this spec.
- `docs/design/dark-mode-conversion-guide.md` — authoritative mapping table and rules; this spec's every FR item traces to a rule in that guide (or an explicit assumption noted below where the guide is silent).
- ADR-006 (referenced in the brief; not read directly, but its requirement — "every component that renders color must render correctly in both light and dark mode" — is treated as the compliance bar).
- No new npm packages, no backend changes, no OpenAPI/client regeneration required.

## Out of Scope
- `LeafletUploadTab.tsx`, `LeafletForm.tsx`, `LeafletResult.tsx`, `LeafletChunkDetailModal.tsx`, and any other sibling file under `frontend/src/features/leaflet-generator/` not named in the brief — these may have the same violations but are not covered by this fix; a follow-up arch-review item should be filed if needed.
- Solid-color primary/secondary/danger action buttons using `bg-{color}-600 ... text-white` (e.g., "Filtrovat", "Vymazat", "Smazat", delete-confirm) — left unchanged per Open Questions assumption A3, since the guide does not define a mapping for solid action buttons and their contrast is already theme-independent.
- Any change to component structure, state management, URL-param sync logic, sorting/pagination/filtering behavior, permission checks, or API calls.
- Any new automated dark-mode visual regression test infrastructure (Playwright screenshot baselines) — manual verification is assumed sufficient for this scoped fix; the testing-strategy doc does not currently mandate visual regression coverage for this feature.
- Global Tailwind config changes — the Graphite token set is assumed complete and correct as-is (confirmed present).
- Fixing any of the three files' *other* pre-existing lint/type/accessibility issues not related to dark-mode contrast (e.g., the `(response as any).id` cast in `LeafletGenerateTab.tsx` line 38) — untouched, out of scope.

## Open Questions
All ambiguities below were resolved with an explicit, documented assumption so the pipeline can proceed without a human decision; flagged here for the architect/reviewer to double-check during PR review.

- **A1** (`LeafletGeneratorPage.tsx` inactive tab hover): the guide has no explicit rule for `hover:text-gray-700`. Assumption: since `text-gray-700` maps to `dark:text-graphite-muted` per the text table and the hover is a "move toward heading emphasis" affordance, `dark:hover:text-graphite-text` was chosen (mirrors the muted→text relationship used for headings elsewhere). If the architect prefers a lighter hover (e.g., staying muted-adjacent), this is a one-line tweak.
- **A2** (`ConfirmDeleteDialog` panel shadow): `shadow-xl` is not in the guide's shadow table (only `shadow`/`shadow-sm`/`shadow-md` are mapped). Assumption: leave `shadow-xl` as-is, unmapped, rather than guessing at a `dark:shadow-soft-dark` substitution not sanctioned by the guide.
- **A3** (solid action buttons): `bg-{indigo,red,gray}-600/700 text-white` buttons are not covered by any guide rule. Assumption: no `dark:` variant needed — solid saturated buttons with white text retain WCAG-sufficient contrast against both light and Graphite backgrounds, so they're treated as already dark-mode-safe. This affects the "Filtrovat", "Vymazat" buttons in `LeafletDocumentsTab.tsx` and the "Smazat"/"Zrušit"-adjacent confirm button in `ConfirmDeleteDialog`.
- **A4** (delete-icon hover, `hover:text-red-600` on line 390 of `LeafletDocumentsTab.tsx`): no guide rule for hover-only semantic-danger text. Assumption: leave unmapped for the same reason as A3 (saturated red-600 remains legible on a dark surface).
- **A5** (loading skeleton `bg-gray-100` in `LeafletDocumentsTab.tsx` line 260 vs. `bg-gray-200` in `LeafletGenerateTab.tsx` lines 88–90): the guide maps these to different tokens (`dark:bg-graphite-surface-2` vs. `dark:bg-graphite-hover` respectively) even though both are "loading skeleton" bars. Assumption: followed the guide literally by source Tailwind class rather than by visual role, since the guide's rule 5 is keyed on the raw class, not semantic intent — this preserves the brief's exact suggested mapping for `bg-gray-200` → `dark:bg-graphite-hover` (brief line 24) while applying the general `bg-gray-100` rule to the other file's skeleton.

## Status: COMPLETE
