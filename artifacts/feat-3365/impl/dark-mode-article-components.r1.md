# Implementation r1 — dark-mode-article-components

## Changes made

Added `dark:` Tailwind variants (Graphite palette) to all six Article sub-components:

### ArticleDetail.tsx
- `STATUS_COLORS`: added `dark:bg-graphite-surface-2` + dark text variants to all 5 statuses
- `HtmlContent`: computed `isDark` from `document.documentElement.classList.contains('dark')`; srcdoc now conditionally uses Graphite colors for body/headings/links
- `InProgressView`: `text-gray-500` → +`dark:text-graphite-faint`, `text-blue-500` → +`dark:text-graphite-accent`
- `ArticleView`: `text-gray-900` → +`dark:text-graphite-text`, `text-gray-500` → +`dark:text-graphite-faint`
- Loading spinner: `text-gray-400` → +`dark:text-graphite-faint`
- Error text: `text-red-600` → +`dark:text-red-400`
- Error block: +`dark:bg-red-950/40 dark:border-red-800 dark:text-red-400`

### ArticleList.tsx
- `STATUS_COLORS`: same dark variants as ArticleDetail
- Loading spinner: +`dark:text-graphite-faint`
- Empty state: +`dark:text-graphite-faint`
- `<ul>`: +`dark:divide-graphite-border`
- Item button: +`dark:hover:bg-graphite-hover`; selected state: +`dark:bg-graphite-surface`
- Article title: +`dark:text-graphite-text`
- Topic/date text: +`dark:text-graphite-faint`

### ArticleDebugPanel.tsx
- `STEP_STATUS_COLORS`: dark variants for Running/Succeeded/Failed
- Fallback color: +`dark:bg-graphite-surface-2 dark:text-graphite-muted`
- `PrettyJson` `<pre>`: +`dark:bg-graphite-surface-2`
- `StepCard` border: +`dark:border-graphite-border`
- Sequence/model/duration spans: +`dark:text-graphite-faint`
- Step name: +`dark:text-graphite-text`
- Step error: +`dark:text-red-400 dark:bg-red-950/40`
- Details summaries (input/output): +`dark:text-graphite-faint dark:hover:text-graphite-muted`
- Panel toggle button: +`dark:text-graphite-muted dark:hover:text-graphite-text`
- Panel border-t: +`dark:border-graphite-border`
- Spinner/error/empty inside panel: +respective dark variants

### ArticleFeedbackSection.tsx
- Both `border-t` wrappers: +`dark:border-graphite-border`
- Score text: +`dark:text-graphite-muted`
- Comment text: +`dark:text-graphite-muted`
- Error text: +`dark:text-red-400`

### ArticleGenerationForm.tsx
- All 6 form labels (`block text-sm font-medium text-gray-700`): +`dark:text-graphite-muted`
- Checkbox labels: +`dark:text-graphite-muted`
- All 8 text/select inputs: +`dark:border-graphite-border dark:bg-graphite-surface dark:text-graphite-text dark:focus:ring-graphite-accent`
- Checkbox inputs: +`dark:border-graphite-border dark:text-graphite-accent dark:focus:ring-graphite-accent`
- Details summary: +`dark:text-graphite-faint`
- API error: +`dark:text-red-400`
- Permission warning: +`dark:text-amber-400`
- Submit button: **no dark: override** on bg-blue-600 (per arch-review decision)

### ArticleSourceList.tsx
- Section border-t: +`dark:border-graphite-border`
- Section heading: +`dark:text-graphite-muted`
- Globe icon: +`dark:text-graphite-accent`
- BookOpen icon: +`dark:text-graphite-accent`
- Web link: +`dark:text-graphite-accent`
- KB chunk button: +`dark:text-graphite-accent`
- Fallback span: +`dark:text-graphite-muted`

## Verification

- `tsc --noEmit`: 38 lines output (identical to main branch — all pre-existing dependency type errors, no new errors)
- `eslint src/features/articles/`: exit 0, no errors or warnings
- `react-scripts build`: SUCCESS — built 1.25 MB JS + 20.91 kB CSS
