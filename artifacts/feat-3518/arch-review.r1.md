# Architecture Review: Dark-mode variants for shared grid components (GridHeader & ColumnChooser)

## Skip Design: true

This is a pure CSS/Tailwind class conversion of two already-existing shared components, following an established and already-documented conversion pattern (`docs/design/dark-mode-conversion-guide.md`). There is no new UI, no new component, no new layout, and no novel visual design decision. The Graphite dark palette, the token names, and the append-in-place `dark:` convention are all pre-existing and in production use on other pages. Nothing here warrants a design pass — the visual target is "match the already-shipped dark theme," and the design system that defines it is fixed. Skip Design is correct.

## Architectural Fit Assessment

The change fits the codebase's existing conventions exactly. Verified against real source, not just the spec:

- **`tailwind.config.js` is configured for this.** `darkMode: 'class'` (line 3) and the full `graphite-*` color scale (lines 48–62) plus `boxShadow['soft-dark']` (line 67) are already defined. Every token the spec references exists: `graphite-surface` (#202327), `graphite-surface-2` (#272A30), `graphite-border` (#2D3138), `graphite-text` (#E6E8EC), `graphite-muted` (#9AA0AA), `graphite-faint` (#6A707A), `graphite-accent` (#38BDF8), and `shadow-soft-dark`. No config change is needed and none should be made.
- **The append-in-place `dark:` pattern is the real, established convention** — not a separate dark stylesheet, not a helper function, not a `clsx`/CVA variant map. Confirmed in production code: `ClassificationHistoryPage.tsx:261` renders `className="bg-gray-50 dark:bg-graphite-surface-2"` on a real `<thead>`, and `GroupDetailPage.tsx:284/327/342/364` render `className="bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark …"`. Both files also use `border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint` on inputs. The spec's approach is a byte-for-byte match to how the rest of the app was converted.
- **ADR-006 is real** and is recorded in `docs/architecture/development_guidelines.md` and `memory/decisions/light-dark-mode-required.md` (both matched on `ADR-006`). It mandates that every color-rendering component work in both light and Graphite dark. These two shared grid components are a genuine gap: they back every grid using `useGridLayout`, so the omission degrades dark mode app-wide. This is a legitimate, high-leverage conformance fix.

The spec also correctly caught and corrected the brief's two inverted mappings (`bg-gray-50` and `bg-white`). I re-verified: the guide says `bg-white → dark:bg-graphite-surface` and `bg-gray-50 → dark:bg-graphite-surface-2`, and both real converted files agree. The spec follows the guide and real code; the brief was wrong on those two. Follow the spec.

**Verdict: the spec's per-line class mappings are correct and grounded. No new approach is warranted or permitted for a task this mechanical.**

## Proposed Architecture

### Component Overview

Two files change; nothing else. No new modules, files, exports, props, hooks, or config.

- `frontend/src/features/grid-layout/GridHeader.tsx` — shared sortable/resizable/reorderable table header (`<thead>`, `<th>`, sort chevrons, grip icon, resize handle).
- `frontend/src/features/grid-layout/ColumnChooser.tsx` — shared "Sloupce" dropdown for toggling column visibility and resetting layout.

Both are consumed by every grid built on `useGridLayout`, which is exactly why fixing them once fixes dark mode everywhere.

### Key Design Decisions

#### Decision 1: Append `dark:` variants in place vs. any alternative styling mechanism

**Options considered:**
- (A) Append `dark:` sibling utilities directly onto the existing `className` strings (the spec's approach).
- (B) Introduce a shared theme helper / class-map function or a separate dark stylesheet.
- (C) Replace raw utilities with design-system component classes (`.card`, `.input`, etc.).

**Chosen approach:** (A) — append `dark:` variants in place, per the conversion guide and the per-line table in `spec.r1.md`.

**Rationale:** (A) is the only approach that matches the established, verified convention (`ClassificationHistoryPage.tsx`, `GroupDetailPage.tsx`). (B) would introduce a novel pattern the codebase does not use and the guide explicitly does not sanction — it would be a gratuitous architectural divergence for a mechanical fix. (C) is out of scope and risky: these elements use raw utilities, not DS classes, and the guide's Rule 2 only exempts elements already on DS classes; swapping them wholesale would change light-mode appearance, which is forbidden by the spec's Out of Scope and the guide's "NEVER change light classes." (A) is surgical, reversible, and consistent.

#### Decision 2: The two mappings with no literal guide entry

**Options considered:**
- Resize-handle hover `hover:bg-indigo-200`: guide has no entry for an indigo *hover background* (only active `bg-indigo-50 → dark:bg-graphite-accent/10`).
- Dropdown `shadow-lg`: guide lists `shadow`/`shadow-sm`/`shadow-md → dark:shadow-soft-dark` but not `shadow-lg`.

**Chosen approach:** Follow the spec: `hover:bg-indigo-200 → dark:hover:bg-graphite-accent/20`, and `shadow-lg → dark:shadow-soft-dark`.

**Rationale:** Both stay strictly within existing tokens. `graphite-accent/20` is a defensible in-system choice for a transient hover affordance (a touch stronger than the `/10` active tint because `indigo-200` is stronger than `indigo-50`), and it introduces no new color. `shadow-lg → dark:shadow-soft-dark` matches how the shadow family is universally collapsed to `shadow-soft-dark` in real converted code (`GroupDetailPage.tsx`, `ClassificationHistoryPage.tsx` both pair any elevation with `dark:shadow-soft-dark`). Neither deviation adds a token or touches `tailwind.config.js`. Accept the spec's calls as written.

## Implementation Guidance

### Directory / Module Structure

No structural change. Edit exactly these two files, in place:
- `/home/user/worktrees/feature-3518-Arch-Review-Gridlayouts-Gridheader-And-Columnchoos/frontend/src/features/grid-layout/GridHeader.tsx`
- `/home/user/worktrees/feature-3518-Arch-Review-Gridlayouts-Gridheader-And-Columnchoos/frontend/src/features/grid-layout/ColumnChooser.tsx`

Do not touch `types.ts`, `useGridLayout`, the grid body/rows, `tailwind.config.js`, or any other file.

### Interfaces and Contracts

No interface changes. Props, exports, generics (`<TRow>`), event handlers, and text (including the Czech labels "Sloupce" / "Reset rozvržení") are unchanged. The only observable contract change is rendered appearance under the `dark` class on the document root.

**Follow the spec's per-line table verbatim.** The line numbers in `spec.r1.md` match the current source exactly (I re-verified every one against the real files); apply them as-is. Reproduced here for the developer as the authoritative checklist:

**`GridHeader.tsx`**

| Line | Element | Existing (do not alter) | Append |
|------|---------|-------------------------|--------|
| 91  | `<th>` | `text-gray-500` | `dark:text-graphite-muted` |
| 99  | grip `<span>` | `text-gray-300 hover:text-gray-500` | `dark:text-graphite-faint dark:hover:text-graphite-muted` |
| 105 | label `<span>` (truthy ternary branch only) | `hover:text-gray-700` | `dark:hover:text-graphite-muted` |
| 110 | `ChevronUp` | active `text-indigo-600` / inactive `text-gray-300` | active `dark:text-graphite-accent` / inactive `dark:text-graphite-faint` (both branches) |
| 111 | `ChevronDown` | active `text-indigo-600` / inactive `text-gray-300` | active `dark:text-graphite-accent` / inactive `dark:text-graphite-faint` (both branches) |
| 117 | resize handle `<div>` | `hover:bg-indigo-200` | `dark:hover:bg-graphite-accent/20` |
| 162 | `<thead>` | `bg-gray-50` | `dark:bg-graphite-surface-2` |

**`ColumnChooser.tsx`**

| Line | Element | Existing (do not alter) | Append |
|------|---------|-------------------------|--------|
| 24 | trigger `<button>` | `text-gray-600 border-gray-300 hover:bg-gray-50` (leave `focus:ring-indigo-500`) | `dark:text-graphite-muted dark:border-graphite-border dark:hover:bg-white/5` |
| 38 | dropdown panel `<div>` | `bg-white border-gray-200 shadow-lg` | `dark:bg-graphite-surface dark:border-graphite-border dark:shadow-soft-dark` |
| 46 | column `<label>` | `text-gray-700 hover:text-gray-900` | `dark:text-graphite-muted dark:hover:text-graphite-text` |
| 51 | checkbox `<input>` | `text-indigo-600 border-gray-300` (leave `focus:ring-indigo-500`) | `dark:text-graphite-accent dark:border-graphite-border` |
| 61 | footer separator `<div>` | `border-gray-100` | `dark:border-graphite-border` |
| 64 | reset `<button>` | `text-gray-500 hover:text-gray-700 hover:bg-gray-50` | `dark:text-graphite-muted dark:hover:text-graphite-muted dark:hover:bg-white/5` |

Line 35 (overlay `fixed inset-0 z-20`) has no color classes — no change.

Rules the developer must respect (from the guide): append only; never remove, reorder, or rewrite an existing class; leave `focus:ring-indigo-500` focus rings as-is; add the dark variant inside each branch of every ternary consistently.

### Data Flow

Unchanged. No state, props, data, or event flow is touched. This is compile-time-generated CSS only; utilities are emitted by Tailwind at build time and applied by the DOM based on the `dark` root class.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Line numbers drift from the spec if the file is edited before this task runs | Low | I verified all 13 line references match current source exactly. Anchor on the element + existing class strings in the table above, not the raw line number, when applying. |
| Accidentally altering/reordering a light-mode class, changing light appearance | Medium | Append-only edits; each `dark:` class is added to the end of the existing string. Diff must show only additions of `dark:` tokens. Compare rendered light mode before/after if unsure. |
| Editing a ternary but only one branch (chevrons on lines 110/111) | Low | Both branches get their mapping: active `→ dark:text-graphite-accent`, inactive `→ dark:text-graphite-faint`. Table calls this out explicitly. |
| Adding a `dark:` for a focus ring (`focus:ring-indigo-500`) | Low | Guide Rule: focus rings stay as-is. Leave both occurrences (ColumnChooser lines 24, 51) untouched. |
| Introducing a non-existent token or hex color | Low | Every token used is confirmed present in `tailwind.config.js` (lines 48–67). No new tokens; `white/5` and `graphite-accent/20` are opacity utilities on existing colors, valid with JIT. |
| `dark:shadow-soft-dark` / `dark:hover:bg-graphite-accent/20` not literally in the guide | Low | Both are within-system, reasoned in the spec, and consistent with real converted code. Accept as specified; do not substitute. |

## Specification Amendments

None. The spec is complete, internally consistent, correctly overrides the brief's two inverted mappings, and every mapping is grounded in the conversion guide, `tailwind.config.js`, and real converted components. Implement `spec.r1.md` exactly as written — do not introduce any alternative approach.

## Prerequisites

None. All dependencies are already in place:
- `darkMode: 'class'` and the full `graphite-*` scale + `shadow-soft-dark` exist in `frontend/tailwind.config.js` (verified).
- The conversion guide (`docs/design/dark-mode-conversion-guide.md`) and reference implementations (`ClassificationHistoryPage.tsx`, `GroupDetailPage.tsx`) exist and are consistent with the spec.
- No new libraries, tokens, feature flags, migrations, or config changes.

Validation on completion (per CLAUDE.md and the spec's acceptance criteria): `npm run build` and `npm run lint` from `frontend/` must pass. No unit tests assert these class strings, so none need updating; a quick visual check of a grid page in dark mode is sufficient confirmation.
