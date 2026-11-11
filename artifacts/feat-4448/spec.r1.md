# Specification: Dark-mode support for LeafletUploadTab

## Summary
`frontend/src/features/leaflet-generator/LeafletUploadTab.tsx` has no `dark:` Tailwind variants, so the Leaflet upload tab renders with broken contrast under the Graphite theme. Add additive `dark:` variants, mirroring the already-compliant `LeafletDocumentsTab.tsx`, so the tab meets ADR-006.

## Background
ADR-006 (accepted 2026-06-25, `docs/architecture/development_guidelines.md`) requires every colour-rendering component to work in light and dark mode, additively (keep light classes, append `dark:`), mapped to `graphite-*` tokens per `docs/design/dark-mode-conversion-guide.md`; status pills use ~900/30 bg + ~300 text; WCAG 2.1 AA. Tokens are defined in `frontend/tailwind.config.js` (`graphite.bg/surface/surface-2/border/text/muted/faint/accent`). No behaviour change is wanted.

## Functional Requirements

### FR-1: Status labels themed
`statusLabel` spans (lines 84-90) get dark variants: uploading `dark:text-blue-400`, done `dark:text-emerald-300`, error `dark:text-red-400`, waiting `dark:text-graphite-faint`.
**Acceptance criteria:**
- Each of the four spans carries its light class unchanged plus the listed `dark:` class.

### FR-2: Drop zone themed
Both states (lines 102-106): default adds `dark:border-graphite-border dark:hover:border-graphite-accent dark:bg-graphite-surface`; dragOver adds `dark:border-graphite-accent dark:bg-graphite-accent/10`.
**Acceptance criteria:**
- Light class strings are preserved verbatim; dark classes appended in each branch.

### FR-3: Drop zone content themed
Upload icon `dark:text-graphite-faint`; primary text `dark:text-graphite-text`; "nebo" `dark:text-graphite-muted`; hint `dark:text-graphite-faint`. The "Vybrat soubory" link (`text-blue-600`, line 111, not listed in the issue but same defect) gets `dark:text-graphite-accent`.
**Acceptance criteria:**
- No `text-gray-*`/`text-blue-*` in the drop zone lacks a `dark:` sibling.

### FR-4: File list themed
Container `dark:border-graphite-border dark:divide-graphite-border`; FileText icon `dark:text-graphite-faint`; filename `dark:text-graphite-text`; remove button `dark:text-graphite-faint dark:hover:text-graphite-text`.

### FR-5: Action buttons themed
"Zrušit vše": `dark:border-graphite-border dark:text-graphite-text dark:hover:bg-white/5`. The primary "Nahrát vše" button (`bg-blue-600 text-white`) has acceptable contrast in both themes and is left unchanged.

### FR-6: No behaviour change
Logic, markup structure, `data-testid`, aria labels and text are unchanged; existing tests still pass.

## Non-Functional Requirements
### NFR-1: Accessibility
Both themes meet WCAG 2.1 AA; verify visually in dark mode (Playwright dev instance :3100 or theme toggle).
### NFR-2: Surgical change
Only `LeafletUploadTab.tsx` (plus an optional test) changes; no config or token changes.

## Data Model
None.

## API / Interface Design
None; className-only change.

## Dependencies
Existing `graphite-*` tokens in `tailwind.config.js`; `LeafletDocumentsTab.tsx` as reference.

## Out of Scope
Other Leaflet components; the lint rule recommended in ADR-006; the primary blue button.
