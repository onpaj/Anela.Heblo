# Architecture Review: Fix ArticleDetail `HtmlContent` theme reactivity

## Skip Design: true

This is a one-line-of-behavior bug fix inside an existing component: swapping a non-reactive DOM read for an existing reactive hook. No new UI, no new visual states, no layout change, no new component. Visual output when the fix works correctly (iframe colors match the active theme) is identical to what the component already intends to render — it just now does so reliably. Nothing here needs `docs/design/ui_design_document.md` or `docs/design/layout_definition.md` review beyond what's already encoded in the existing `srcDoc` string.

## Architectural Fit Assessment

This change fits cleanly into an established, already-adopted pattern — it does not introduce one. `ThemeContext.tsx` (`frontend/src/contexts/ThemeContext.tsx`) is the single source of theme truth for the app: it holds `theme: 'light' | 'dark'` in React state, syncs `document.documentElement`'s `dark` class and `localStorage` via `useEffect`, and exposes `useTheme()`. Every other consumer in the codebase already reads theme through this hook, not through the DOM:

- `frontend/src/components/common/ThemeToggle.tsx:10-11` — `const { theme, toggle } = useTheme(); const isDark = theme === "dark";`
- `frontend/src/pages/OrgChartPage.tsx:26` — `const { theme } = useTheme();`

A repo-wide search (`grep -rn "document.documentElement.classList"` under `frontend/src`) confirms `ArticleDetail.tsx` is the **only** application component still reading the DOM class list directly (the only other hits are `ThemeContext.tsx` itself, which is the legitimate place to own that side effect, and its test file). This is a straightforward isolated anti-pattern instance, not a systemic issue — the spec correctly scopes the fix to this one file and defers any broader sweep as a separate follow-up (see Specification Amendments below for one clarification on that).

The integration point is trivial: `ArticleDetail.tsx` already sits under the app's root `ThemeProvider` (same as every other routed page/component), so `useTheme()` is safe to call with no new provider wiring.

## Proposed Architecture

### Component Overview

No new components, no new boundaries. The existing tree is unchanged:

```
ThemeProvider (app root, already wraps the whole tree)
  └─ ArticleDetail (frontend/src/features/articles/ArticleDetail.tsx)
       └─ ArticleView
            └─ HtmlContent  ← only this function changes
                 - was: isDark = document.documentElement.classList.contains('dark')  (non-reactive)
                 - now: isDark = useTheme().theme === 'dark'                          (reactive)
```

`HtmlContent` moves from an ad-hoc, imperative theme read to the same declarative subscription every other themed component uses. The `key={isDark ? 'dark' : 'light'}` remount trick on the `<iframe>` is preserved as-is — it becomes *effective* now that `isDark` is backed by state that actually changes on toggle, whereas before it was a no-op safety net around a value that was frozen at mount/last-unrelated-render.

### Key Design Decisions

#### Decision 1: Source of truth for `isDark`
**Options considered:**
1. Keep the DOM query but wrap it in a `useEffect`/`MutationObserver` to make it reactive.
2. Read `useTheme()` directly, as every other component in the codebase does.
3. Lift theme derivation to a prop passed down from `ArticleDetail` or `ArticleView`.

**Chosen approach:** Option 2 — call `useTheme()` inside `HtmlContent` directly, exactly as specified.

**Rationale:** Option 1 reinvents state management that `ThemeContext` already provides, adds an observer to watch a class list `ThemeContext` itself controls, and is strictly worse than just reading the context. Option 3 (prop drilling through `ArticleView`) adds an unnecessary parameter to two components for no benefit, since `HtmlContent` can call the hook itself — React context is designed precisely to avoid this kind of drilling, and no other data threads through `ArticleView`/`ArticleDetail` for this purpose today. Option 2 matches the codebase's existing, singular convention (`ThemeToggle.tsx`, `OrgChartPage.tsx`) and requires touching only the two lines the spec identifies plus one import.

#### Decision 2: Keep the `key`-based iframe remount mechanism
**Options considered:**
1. Replace `key={isDark ? 'dark' : 'light'}` remount with `postMessage`/direct DOM manipulation of the iframe's existing document to update styles in place.
2. Keep the existing remount-via-`key` approach; only fix its input.

**Chosen approach:** Option 2.

**Rationale:** The iframe uses `sandbox="allow-same-origin"` with `srcDoc` (no `allow-scripts`), and its content is regenerated wholesale from `html` + inline colors on every render anyway. Remount-on-key-change is the idiomatic React way to force a full re-render of an uncontrolled subtree (here, an iframe's document) and was already the intended design — it just never fired correctly because its input never changed. This is explicitly Out of Scope per the spec, and there's no architectural reason to revisit it: changing the remount strategy would be a scope expansion with no corresponding bug to justify it.

## Implementation Guidance

### Directory / Module Structure
No new files, no new directories. Single-file change:

- `frontend/src/features/articles/ArticleDetail.tsx` — add one import, replace one line inside `HtmlContent`.

Relative import path from `frontend/src/features/articles/ArticleDetail.tsx` to `frontend/src/contexts/ThemeContext.tsx` is `../../contexts/ThemeContext` (verified against actual directory depth: `features/articles/` → up two levels to `src/`, then into `contexts/`). This matches the spec's stated path exactly.

### Interfaces and Contracts
No public interface changes.
- `HtmlContent`'s prop shape (`{ html: string }`) — unchanged.
- `ArticleDetail`'s exported default and its props (`{ articleId: string }`) — unchanged.
- New internal dependency: `HtmlContent` now calls `useTheme(): { theme: 'light' | 'dark'; toggle: () => void }` from `ThemeContext.tsx`. Only `theme` is consumed; `toggle` is not needed here (matches `OrgChartPage.tsx`'s usage, which also destructures only `theme`).

### Data Flow
Before: `document.documentElement` (DOM, mutated by `ThemeProvider`'s effect) → read once per render by `HtmlContent` → `isDark` (stale after re-renders unrelated to theme).

After: `ThemeProvider` state (`theme`) → React Context → `useTheme()` in `HtmlContent` → `isDark` recomputed on every render, and `HtmlContent` re-renders whenever `ThemeContext`'s value changes (context consumers re-render on Provider value change) → `key` prop changes → iframe unmounts/remounts → new `srcDoc` built with correct colors.

This closes the reactivity gap described in the brief: theme changes now flow through React's normal render cycle instead of requiring an incidental unrelated re-render to "catch up" to the DOM.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| `HtmlContent` renders outside a `ThemeProvider` ancestor, causing `useTheme()`'s guard to throw | Low | Already mitigated by app structure — `ArticleDetail` is only ever rendered inside the routed app tree, which is wrapped by the root `ThemeProvider` (same guarantee every other `useTheme()` consumer already relies on, e.g. `ThemeToggle`, `OrgChartPage`). No new risk introduced. |
| Regression: iframe stops remounting on theme change if the `key` logic is inadvertently altered during the edit | Low | Spec explicitly calls out that `key={isDark ? 'dark' : 'light'}` must remain untouched; a manual/E2E check (toggle theme with an article panel open, confirm colors update) is a cheap, high-confidence verification step. |
| Scope creep into "fix this everywhere" | Low | Confirmed via search that `ArticleDetail.tsx` is the only application component with this anti-pattern today, so there is no hidden second instance to accidentally miss or unnecessarily touch in this change. |

## Specification Amendments

None required to the functional requirements — the spec's prescribed code change is correct, minimal, and matches the codebase's existing convention exactly (verified against `ThemeToggle.tsx` and `OrgChartPage.tsx`).

One informational note, not a spec change: the spec's Out of Scope section mentions a possible "codebase-wide sweep" as a hypothetical follow-up. Architecturally confirmed: there is currently no other instance of this anti-pattern to sweep — `ArticleDetail.tsx` was the only offender. No follow-up ticket is architecturally necessary on that basis alone; leave it to product/PM discretion.

## Prerequisites

None. `ThemeContext.tsx` and its `ThemeProvider`/`useTheme()` already exist, are already mounted at the app root, and require no changes. No migrations, no config, no new dependencies. Implementation can start immediately.
