# Specification: Dark-mode (Graphite) variants for Leaflet generator form and result

## Summary
Add missing `dark:` Tailwind variants using the existing graphite tokens to `LeafletForm.tsx` and the action buttons in `LeafletResult.tsx`, so the Leaflet generate tab is legible in dark mode as required by ADR-006. Frontend styling only; no behaviour, API or data change.

## Background
ADR-006 requires every frontend component that renders color to have light and dark variants. Adjacent components (`LeafletGenerateTab.tsx`, `LeafletDocumentsTab.tsx`) already use `dark:text-graphite-text`, `dark:bg-graphite-surface-2`, `dark:border-graphite-border` etc. An arch-review (issue #4447) found `LeafletForm.tsx` has none and `LeafletResult.tsx` buttons lack dark border/hover/text.

## Functional Requirements

### FR-1: LeafletForm text colors
Add `dark:text-graphite-text` to the topic `<label>`, both `<legend>` elements, and the radio `<label>` elements (audience and length groups). Add `dark:text-graphite-muted` to the character-count `<span>`.
**Acceptance criteria:**
- Each listed element's className contains the stated dark class; existing light classes unchanged.

### FR-2: LeafletForm topic input
Add `dark:bg-graphite-surface-2 dark:border-graphite-border dark:text-graphite-text dark:placeholder-graphite-faint` to the topic `<input>`.
**Acceptance criteria:**
- Input className contains all four dark classes.

### FR-3: LeafletResult action buttons
Add `dark:border-graphite-border dark:hover:bg-white/5 dark:text-graphite-text` to the "Kopírovat" and "Generovat znovu" buttons.
**Acceptance criteria:**
- Both buttons' classNames contain the three dark classes.

### FR-4: Submit button check
The submit button (`bg-blue-600 hover:bg-blue-700 text-white disabled:opacity-50`) needs no dark override; verify disabled contrast is acceptable and make no change unless clearly deficient.
**Acceptance criteria:**
- Submit button className unchanged, or change justified in the PR.

## Non-Functional Requirements

### NFR-1: Performance
No impact (static class strings).

### NFR-2: Security
None applicable.

## Data Model
None.

## API / Interface Design
No props, API, or DOM structure changes. Class strings only.

## Dependencies
Graphite tokens in the Tailwind config (`graphite-text`, `graphite-muted`, `graphite-faint`, `graphite-surface-2`, `graphite-border`) already used elsewhere in the repo.

## Out of Scope
- The `prose` markdown container in `LeafletResult.tsx` (not listed in the finding; may be noted for follow-up, e.g. `dark:prose-invert`).
- Other leaflet components, light-mode appearance, behaviour changes.

## Open Questions
None.

## Status: COMPLETE
