# Task Plan r1 — feat-3365

### task: dark-mode-article-components

**Goal**

Add `dark:` Tailwind variants to all six Article sub-components so they render correctly in the
Graphite dark theme (ADR-006).

**Files to change**

1. `frontend/src/features/articles/ArticleDetail.tsx`
2. `frontend/src/features/articles/ArticleList.tsx`
3. `frontend/src/features/articles/ArticleDebugPanel.tsx`
4. `frontend/src/features/articles/ArticleFeedbackSection.tsx`
5. `frontend/src/features/articles/ArticleGenerationForm.tsx`
6. `frontend/src/features/articles/ArticleSourceList.tsx`

**Implementation instructions**

Follow the spec (`spec.r1.md`) and design (`design.r1.md`) exactly. Key points:

- Add `dark:` variants to every color-bearing class in each file per the colour-mapping table in
  design.r1.md.
- For `STATUS_COLORS` in `ArticleDetail.tsx` and `ArticleList.tsx`, add dark: classes inline in the
  string values (e.g. `'bg-gray-100 text-gray-700 dark:bg-graphite-surface-2 dark:text-graphite-muted'`).
- For `STEP_STATUS_COLORS` in `ArticleDebugPanel.tsx`, same approach.
- For `HtmlContent` in `ArticleDetail.tsx`, compute `isDark` from
  `document.documentElement.classList.contains('dark')` and use it to select inline CSS values.
- Submit button in `ArticleGenerationForm.tsx`: do NOT add dark: override to `bg-blue-600` or
  `hover:bg-blue-700` — see arch-review.r1.md.
- Do not modify any other files.

**Verification**

Run from `frontend/`:
```
npm run build
npm run lint
```
Both must pass with no new errors or warnings.

Write implementation summary to `artifacts/feat-3365/impl/dark-mode-article-components.r1.md`.
