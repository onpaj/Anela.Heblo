# Architecture Review: Dark-mode support for LeafletUploadTab

## Skip Design: true
Pure className mapping with a fully specified token table in the spec; no new components, layout or visual decisions.

## Architectural Fit Assessment
Fits ADR-006 exactly: additive `dark:` variants and `graphite-*` tokens, as already used in `LeafletDocumentsTab.tsx` (verified: `dark:bg-graphite-surface`, `dark:border-graphite-border`, `dark:text-graphite-text/muted/faint`, `dark:hover:bg-white/5`, status hues `dark:text-red-400`, emerald-300). Tokens exist in `frontend/tailwind.config.js`. No integration points beyond the single file.

## Proposed Architecture

### Component Overview
Unchanged: `LeafletUploadTab` (drop zone, file list, action buttons, `statusLabel` helper).

### Key Design Decisions
#### Decision 1: Inline `dark:` variants vs design-system classes
**Options considered:** `.card`/`.btn-*` design-system classes; inline `dark:` variants.
**Chosen approach:** Inline additive `dark:` variants.
**Rationale:** Swapping to design-system classes would alter light-mode appearance and violate surgical-change rules; the sibling tab uses inline variants.

#### Decision 2: Status hues
Use the issue's mapping (blue-400, emerald-300, red-400, graphite-faint) as plain text colours; these are text-only labels, not pills, so no 900/30 bg is needed.

## Implementation Guidance
### Directory / Module Structure
Edit only `frontend/src/features/leaflet-generator/LeafletUploadTab.tsx`; optionally extend `__tests__/LeafletUploadTab.test.tsx`.
### Interfaces and Contracts
No prop/API changes. Keep light classes byte-identical; append dark classes after them.
### Data Flow
Unchanged.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Template-literal ternary in drop zone mis-edited, breaking light styling | Low | Edit each branch string; existing tests plus a class assertion |
| Tailwind purge misses dynamic classes | Low | All classes are full literal strings |
| `graphite-accent/10` opacity syntax unsupported | Low | Token is a hex colour so `/10` works; same pattern used elsewhere |

## Specification Amendments
Add `dark:text-graphite-accent` for the "Vybrat soubory" link (line 111), omitted from the issue table.

## Prerequisites
None.
