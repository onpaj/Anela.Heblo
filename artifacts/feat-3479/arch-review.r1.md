# Architecture Review: Leaflet Generator — Graphite Dark Mode Compliance (ADR-006)

## Skip Design: true

This is a class-level dark-mode styling fix applied to three existing components. No new UI components, screens, layouts, interaction patterns, or visual design decisions are introduced — the token map and conventions already exist and are already in production use elsewhere in the codebase (`frontend/src/features/articles/*`). There is nothing here for a designer to review; the only design authority is `docs/design/dark-mode-conversion-guide.md`, which is already accepted (ADR-006).

## Architectural Fit Assessment

This spec is architecturally sound and low-risk. I verified all three claims that matter for approving it as-is:

1. **The spec's line numbers and class strings are accurate.** I read all three source files (`LeafletGeneratorPage.tsx` 57 lines, `LeafletDocumentsTab.tsx` 515 lines, `LeafletGenerateTab.tsx` 102 lines) and every cited line/class in FR-1 through FR-3 matches the current file content exactly (e.g. line 14 `indexed: 'bg-green-100 text-green-800'`, line 33 `bg-white rounded-lg shadow-xl`, line 470-474 page-number ternary, line 88-90 skeleton bars). No drift between spec and code.

2. **The token map is the one already in force.** `docs/design/dark-mode-conversion-guide.md` and ADR-006 (`docs/architecture/development_guidelines.md` lines 294-298) define exactly the mapping the spec applies. `frontend/tailwind.config.js` (lines 48-62) already defines the full `graphite.*` scale and `boxShadow.soft-dark`; nothing needs to be added there.

3. **The convention is already load-bearing elsewhere**, not invented for this feature. `frontend/src/features/articles/` (`ArticleList.tsx`, `ArticleDebugPanel.tsx`, `articleStatusConfig.ts`, `ArticleGenerationForm.tsx`, `ArticleSourceList.tsx`, `ArticleDetail.tsx`) already ships this exact pattern set:
   - Status color maps: `'bg-green-100 text-green-700 dark:bg-emerald-900/30 dark:text-emerald-300'` (articleStatusConfig.ts:15) — identical shape to the spec's `StatusBadge` fix.
   - Fallback/neutral pill: `'bg-gray-100 text-gray-700 dark:bg-graphite-surface-2 dark:text-graphite-muted'` (ArticleDebugPanel.tsx:32) — identical to the spec's FR-2a fallback treatment.
   - Raw input bundle: `dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint` (ArticleGenerationForm.tsx:91, 81, 176, 183) — identical to the spec's FR-2d input/select treatment.
   - Hover backgrounds: `hover:bg-gray-50 dark:hover:bg-white/5` (ArticleList.tsx:47) — identical to the spec's row/button hover treatment.
   - `divide-gray-100 dark:divide-graphite-border`, `border-t dark:border-graphite-border`, `bg-blue-50 dark:bg-graphite-accent/10` — all present verbatim in shipped code.

   Conclusion: there is no new abstraction to design. The correct architectural posture is to confirm the mechanical mapping and get out of the way.

I have one substantive disagreement with the spec (Decision 2 below) and one scope note (Decision 3), both minor; neither changes the overall shape of the work.

## Proposed Architecture

### Component Overview

No new components. Three existing presentational components in `frontend/src/features/leaflet-generator/` get additive `className` edits only:

- `LeafletGeneratorPage.tsx` — tab shell (header, tab nav)
- `LeafletDocumentsTab.tsx` — filter bar, table, `StatusBadge`, `ConfirmDeleteDialog`, `SortableHeader`, pagination footer (all defined in this one file)
- `LeafletGenerateTab.tsx` — error banners, loading skeleton

`LeafletUploadTab.tsx`, `LeafletForm.tsx`, `LeafletResult.tsx`, `LeafletChunkDetailModal.tsx` remain untouched (correctly out of scope per the brief — flag as follow-up work, not silently expand this spec).

### Key Design Decisions

#### Decision 1: Confirm — mechanical token mapping, no abstraction
**Options considered:** (a) apply `dark:` utility classes directly per the guide's table, matching the `articles` feature precedent; (b) extract a shared `StatusBadge`/`colorMap` component or hook shared across features to avoid repeating the color-map pattern.
**Chosen approach:** (a). Apply `dark:` classes directly, file by file, exactly as the spec enumerates.
**Rationale:** ADR-006's decision text explicitly frames this as additive utility-class work, not a components refactor. `articles` already has its own local `StatusBadge`-equivalent colorMap (`articleStatusConfig.ts`) that is not shared with `leaflet-generator`'s local `StatusBadge`, and the two status vocabularies (`indexed/processing/failed` vs `Queued/Researching/Writing/Generated/Failed`) don't overlap cleanly. Introducing a shared badge abstraction now would be an uninvited refactor outside this fix's blast radius — exactly what the brief and CLAUDE.md's "surgical changes" rule warn against. Leave the two colorMaps as parallel, independently-themed literals.

#### Decision 2: Amend — solid action buttons should NOT be blanket-exempted
**Options considered:** (a) accept the spec's Open Question A3 as-is — leave all `bg-{indigo,red,gray}-600 ... text-white` buttons unchanged since "solid saturated buttons with white text retain WCAG-sufficient contrast against both light and Graphite backgrounds"; (b) require a per-button check against the guide before exempting.
**Chosen approach:** (b), with a narrow correction to FR-2b/FR-2d/FR-3's scope, not the whole exemption.
**Rationale:** The technical claim in A3 (solid `-600`/`-700` background + white text has sufficient contrast on any surface) is true and is a reasonable general rule — the guide itself doesn't map solid buttons, and I found no counter-example in the `articles` feature where a solid action button was given a `dark:` variant. So the exemption is correctly grounded. However, the guide's silence should not be read as "any button using text-white is exempt" — it should be read as "solid saturated background + white text is exempt because contrast is invariant." The distinction matters for a reviewer skimming this list later: keep A3 confined to buttons that are *both* solid-saturated (`bg-*-600`+) *and* white text, not "buttons that render text-white for any reason." All buttons the spec cites (Filtrovat = indigo-600, Vymazat = gray-500/600, Smazat = red-600) satisfy this narrower rule, so **no code changes result from this decision** — it's a documentation tightening, not a functional change. Record it so a future arch-review pass doesn't need to re-litigate A3 for a different, less-saturated button.

#### Decision 3: Reject scope creep — do not "fix" `bg-gray-500` on the Vymazat button
The spec correctly leaves `bg-gray-500 hover:bg-gray-600 text-white` (Vymazat button, line 341) unchanged. Flagging explicitly: `bg-gray-500` is a mid-tone that is *not* white-on-white-adjacent, so at a glance it looks more like a candidate for the neutral-surface mapping than the accent-button mapping. It is not — it is a solid button with white text, same as the indigo Filtrovat button next to it, and A3's contrast argument applies identically. No action; call-out is to prevent an implementer "fixing" this during PR review under a mistaken read of the guide.

## Implementation Guidance

### Directory / Module Structure
No structural changes. All edits stay within the existing three files in `frontend/src/features/leaflet-generator/`. No new files, no new directories, no new shared modules.

### Interfaces and Contracts
None affected. No props, no component signatures, no exported types change. `Props` (`canDelete: boolean`), `SortableHeaderProps`, `ConfirmDeleteDialog`'s inline prop type, and `ErrorBanner`/`ApiError` in `LeafletGenerateTab.tsx` are all untouched — verified by reading the full files; the spec's claim that no interfaces change is correct.

### Data Flow
Unaffected — this is presentation-only. `useLeafletDocumentsQuery`, `useLeafletContentTypesQuery`, `useDeleteLeafletDocumentMutation` (`frontend/src/api/hooks/useLeaflet.ts`), URL-param sync (`useSearchParams`), and `usePermissionsContext`/`hasPermission('marketing.leaflet.write')` gating are all read-only dependencies of this change and must not be touched, consistent with NFR-2.

### Execution order (for the implementer)
Follow the spec's own ordering — `LeafletDocumentsTab.tsx` first (highest surface area: FR-2a through FR-2h), then `LeafletGeneratorPage.tsx` (FR-1), then `LeafletGenerateTab.tsx` (FR-3). Within `LeafletDocumentsTab.tsx`, do the `StatusBadge` color map and `ConfirmDeleteDialog` first (self-contained, low line-count), then the filter bar, then the table body, then pagination — each is independently verifiable by toggling dark mode and eyeballing that region before moving to the next.

### Verification approach
No new automated visual regression infrastructure is needed for a change this size (per Out of Scope), but each FR's acceptance criteria are individually checkable by hand:
- Toggle dark mode (Graphite) via `ThemeContext`, confirm each region matches the token map.
- Toggle back to light mode, confirm pixel-identical rendering to pre-change (per FR-2's acceptance criteria) — since every change is a pure `dark:`-prefixed addition, light-mode DOM output is unaffected by construction; this is a low-risk claim to verify by diffing `className` strings rather than requiring a screenshot tool.
- `npm run build` and `npm run lint` per CLAUDE.md's standard validation gate — no new lint categories are introduced by this change (no new custom classes, no arbitrary-value Tailwind syntax beyond what's already in the token map, e.g. `graphite-accent/10` and `white/5` which are standard Tailwind opacity-modifier syntax already used elsewhere in `articles`).

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Implementer treats A3 (solid buttons) as "any white-text element is exempt" and skips a button that actually needs a dark variant | Low | Decision 2 above narrows the exemption explicitly to solid `-600`+ saturated backgrounds + white text; PR reviewer should spot-check any new/changed button against this narrower rule, not the spec's broader phrasing |
| Ternary/template-literal edits (tab active state, sort chevrons, pagination active page) introduce a syntax error in the class string interpolation | Low | These are mechanical string edits inside existing template literals with well-defined branch boundaries (spec cites exact original strings); a `tsc`/`npm run build` pass will catch any malformed JSX/template literal immediately |
| Reviewer or implementer "improves" adjacent code while in the file (e.g., the `(response as any).id` cast noted in Out of Scope, or unrelated lint warnings) | Low | Explicitly called out as Out of Scope in the spec and reinforced by CLAUDE.md's "surgical changes" rule — no action needed beyond flagging it in code review if it happens |
| `LeafletUploadTab.tsx` / `LeafletForm.tsx` / `LeafletResult.tsx` / `LeafletChunkDetailModal.tsx` remain dark-mode-broken after this fix, and a user navigating between tabs sees inconsistent theming (documents/generate tabs themed, upload tab not) | Medium (UX, not correctness) | Already flagged in the spec's Out of Scope with a note to file a follow-up arch-review item; recommend filing that follow-up issue immediately after this PR merges so the tab-level inconsistency window is short |

## Specification Amendments

1. **Narrow Open Question A3's stated rationale** (see Decision 2): the exemption for solid action buttons applies specifically to *solid, saturated (`-600`/`-700`+) background + white text* combinations, not to "any button using `text-white`." No code changes result — this is a one-sentence clarification for the PR description / commit message so a future reviewer doesn't misapply the exemption to a lighter-background button elsewhere in the app.
2. No other amendments. The FR-1/FR-2/FR-3 line-by-line mapping was independently verified against the current source files and against the accepted guide/ADR and is approved as written.

## Prerequisites

None. `frontend/tailwind.config.js` already defines the full `graphite.*` token scale and `boxShadow.soft-dark` (confirmed at lines 48-62 and 67); `docs/design/dark-mode-conversion-guide.md` and ADR-006 are already accepted; the `articles` feature already demonstrates every mapping this spec needs in shipped code. Implementation can start immediately with no setup work.
