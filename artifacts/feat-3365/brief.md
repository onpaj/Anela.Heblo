## Module
Article

## Finding
`ArticlesPage.tsx` was partially migrated to dark mode (it already uses `dark:text-graphite-text`, `dark:border-graphite-border`, `dark:text-graphite-accent`, etc.), but all six child components are missing `dark:` variants on every color-bearing class, violating ADR-006 (accepted 2026-06-25).

Specific missing coverage:

**`frontend/src/features/articles/ArticleDetail.tsx`**
- Lines 21–26: `STATUS_COLORS` uses only light classes (`bg-gray-100 text-gray-700`, `bg-blue-100 text-blue-700`, etc.) — no `dark:` variants.
- Lines 31–33: `HtmlContent` renders into an iframe with hardcoded light-mode inline CSS (`color:#1f2937`, `color:#111827`).
- `InProgressView`, `ArticleView` — all color utilities (`text-gray-500`, `text-gray-900`, `text-blue-500`, etc.) lack `dark:` counterparts.
- Error blocks (`bg-red-50 border border-red-200 text-red-700`) have no dark equivalents.

**`frontend/src/features/articles/ArticleList.tsx`**
- Lines 20–26: `STATUS_COLORS` — same issue as above.
- List dividers (`divide-gray-100`), hover state (`hover:bg-gray-50`), selected state (`bg-blue-50`), and all text utilities are missing `dark:` variants.

**`frontend/src/features/articles/ArticleDebugPanel.tsx`**
- `STEP_STATUS_COLORS` (lines 9–13) — only light classes.
- `PrettyJson` uses `bg-gray-50` with no dark equivalent.
- `StepCard` border, text colors, error block, and expand summary text are all light-only.

**`frontend/src/features/articles/ArticleFeedbackSection.tsx`**
- Feedback display (`text-gray-700`, `text-gray-600`) and the border-t separator have no `dark:` variants.

**`frontend/src/features/articles/ArticleGenerationForm.tsx`**
- All form labels, inputs, selects, checkbox labels, and the style-guide details summary use light-only utilities.

**`frontend/src/features/articles/ArticleSourceList.tsx`**
- Section border, heading, link colors (`text-blue-600`, `text-green-700`), and fallback text (`text-gray-700`) have no `dark:` equivalents.

## Why it matters
Users who have enabled the Graphite dark theme see the Article section with jarring light-mode backgrounds and text on all sub-components, even though the wrapping page shell correctly renders in dark mode. The ADR-006 decision explicitly requires all new and existing UI to carry `dark:` variants.

## Suggested fix
Add `dark:` Tailwind variants (from the Graphite palette: `graphite-text`, `graphite-muted`, `graphite-faint`, `graphite-accent`, `graphite-border`, `graphite-surface`, `graphite-surface-2`, `graphite-hover`) to every color-bearing class in each of the six child components. Update `HtmlContent`'s iframe srcdoc to conditionally apply dark styles based on the `dark` class present on `document.documentElement`.
