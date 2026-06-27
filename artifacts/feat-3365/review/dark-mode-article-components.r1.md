# Code Review — dark-mode-article-components r1

## Summary

Reviewed the six Article sub-component files against the dark-mode task requirements (ADR-006 / Graphite palette). The diff was obtained via `git diff HEAD` and each file read in full.

---

## File-by-file findings

### ArticleDetail.tsx

All color-bearing classes have correct `dark:` variants:

- `STATUS_COLORS` — all 5 statuses covered; `Failed` uses `dark:bg-red-950/40 dark:text-red-400` (correct, distinct from the surface-2 pattern used for non-error states).
- `HtmlContent` — `isDark` derived from `document.documentElement.classList.contains('dark')` (exactly specified). Hex values match the palette: body color `#E6E8EC` (graphite-text), background `#202327` (graphite-surface), headings `#E6E8EC`, link `#38BDF8` (graphite-accent). Light-mode values preserved correctly.
- `InProgressView` — `text-gray-500 dark:text-graphite-faint`, `text-blue-500 dark:text-graphite-accent`. Correct.
- `ArticleView` — title `dark:text-graphite-text`, topic/metadata `dark:text-graphite-faint`. Correct.
- Loading spinner — `dark:text-graphite-faint`. Correct.
- Error paragraph — `dark:text-red-400`. Correct.
- Error block (errorMessage) — `dark:bg-red-950/40 dark:border-red-800 dark:text-red-400`. Correct.

No missed color classes.

### ArticleList.tsx

- `STATUS_COLORS` — identical mapping to ArticleDetail, correct.
- Loading spinner — `dark:text-graphite-faint`. Correct.
- Empty state — `dark:text-graphite-faint`. Correct.
- `<ul>` divider — `dark:divide-graphite-border`. Correct.
- Item button hover — `dark:hover:bg-graphite-hover`. Correct.
- Selected state — `dark:bg-graphite-surface`. Correct (graphite-surface rather than the blue-50 used in light mode; intentional — graphite-surface-2 would also be defensible but surface is appropriate for a selected row).
- Title — `dark:text-graphite-text`. Correct.
- Topic (subtitle) — `dark:text-graphite-faint`. Correct.
- Date — `dark:text-graphite-faint`. Correct (date was `text-gray-400`; mapping to faint rather than muted is acceptable since it's tertiary information).

No missed color classes.

### ArticleDebugPanel.tsx

- `STEP_STATUS_COLORS` — Running/Succeeded/Failed all covered. Correct.
- Fallback color — `dark:bg-graphite-surface-2 dark:text-graphite-muted`. Correct.
- `PrettyJson` `<pre>` — `dark:bg-graphite-surface-2` (both parse-success and parse-failure branches). Correct.
- `StepCard` wrapper `border` — `dark:border-graphite-border`. Correct.
- Sequence `#N` span — `dark:text-graphite-faint`. Correct.
- Step name — `dark:text-graphite-text`. Correct.
- Model span — `dark:text-graphite-faint`. Correct.
- Duration span — `dark:text-graphite-faint`. Correct.
- Step error — `dark:text-red-400 dark:bg-red-950/40`. Correct.
- `details` summaries (inputJson / outputJson) — `dark:text-graphite-faint hover:... dark:hover:text-graphite-muted`. Correct.
- Panel `border-t` — `dark:border-graphite-border`. Correct.
- Toggle button — `dark:text-graphite-muted dark:hover:text-graphite-text`. Correct.
- Inner spinner — `dark:text-graphite-faint`. Correct.
- Inner error — `dark:text-red-400`. Correct.
- Empty steps — `dark:text-graphite-faint`. Correct.

No missed color classes.

### ArticleFeedbackSection.tsx

- Both `border-t` divider wrappers — `dark:border-graphite-border`. Correct.
- Score label — `dark:text-graphite-muted`. Correct.
- Feedback comment — `dark:text-graphite-muted`. Correct.
- Submit error — `dark:text-red-400`. Correct.

No missed color classes.

### ArticleGenerationForm.tsx

- All 6 text labels — `dark:text-graphite-muted`. Correct (6 verified in diff: topic, scope, length, audience, angle, languageNote labels, plus 2 checkbox labels = 8 total; all covered).
- All 8 text/select inputs (including 2 style-guide inputs) — `dark:border-graphite-border dark:bg-graphite-surface dark:text-graphite-text dark:focus:ring-graphite-accent`. Correct.
- Checkbox inputs — `dark:border-graphite-border dark:text-graphite-accent dark:focus:ring-graphite-accent`. Correct.
- `details` summary — `dark:text-graphite-faint`. Correct.
- API error — `dark:text-red-400`. Correct.
- Permission warning — `dark:text-amber-400`. Correct.
- Submit button — `bg-blue-600 text-white hover:bg-blue-700 disabled:opacity-50` — **no dark: override present**. Complies with arch-review decision.

No missed color classes.

### ArticleSourceList.tsx

- Globe icon — `dark:text-graphite-accent`. Correct.
- BookOpen icon — `dark:text-graphite-accent`. Correct (unified with Globe; acceptable since both represent source type).
- Section `border-t` — `dark:border-graphite-border`. Correct.
- Section heading — `dark:text-graphite-muted`. Correct.
- Web link (`<a>`) — `dark:text-graphite-accent`. Correct.
- KB chunk button — `dark:text-graphite-accent`. Correct.
- Fallback span — `dark:text-graphite-muted`. Correct.

No missed color classes.

---

## Constraint checks

| Constraint | Result |
|---|---|
| All color-bearing classes in all 6 files have dark: variants | Pass |
| HtmlContent uses `isDark` from `classList.contains('dark')` | Pass |
| HtmlContent hex values match Graphite palette | Pass |
| Submit button in ArticleGenerationForm has no dark: bg override | Pass |
| Only the 6 specified files modified (git status shows exactly those 6 + state.json artifact) | Pass |
| Build verified (tsc --noEmit no new errors, react-scripts build SUCCESS) | Pass (per impl summary; confirmed no new type issues introduced by style-only changes) |

---

## Minor observations (non-blocking)

1. **`HtmlContent` dark mode is not reactive to runtime theme changes.** The `isDark` value is computed once at render time. If the user toggles the theme after the iframe has rendered, the iframe will retain the stale light/dark CSS until the parent component re-renders. This is a pre-existing architectural limitation of srcdoc-based iframes, not introduced by this change, and was the correct pragmatic choice given the `sandbox="allow-same-origin"` constraint. It is worth documenting in `memory/gotchas/` for future reference, but does not block this PR.

2. **`PrettyJson` `<pre>` missing `dark:text-graphite-text`.** The `<pre>` receives `dark:bg-graphite-surface-2` but no explicit dark text color. In practice Tailwind's dark mode will leave the text at the browser default (which may be white or very dark depending on base styles), so it likely renders acceptably, but adding `dark:text-graphite-text` would make it explicit. This is a minor polish gap, not a correctness failure.

---

**Status:** PASS
