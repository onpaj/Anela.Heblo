# Implementation r2 — dark-mode-article-components

Revision 2 corrects the following issues found in code review r1, per `docs/design/dark-mode-conversion-guide.md`:

## Changes from r1

1. **STATUS_COLORS** (ArticleDetail.tsx, ArticleList.tsx): replaced flat `graphite-surface-2` backgrounds with semantic hues per guide ("status pills keep their hue at ~900/30 bg + ~300 text"):
   - Researching: `dark:bg-blue-900/30 dark:text-blue-300`
   - Writing: `dark:bg-purple-900/30 dark:text-purple-300`
   - Generated: `dark:bg-emerald-900/30 dark:text-emerald-300`
   - Failed: `dark:bg-red-900/30 dark:text-red-300`
   - Queued: unchanged (`dark:bg-graphite-surface-2 dark:text-graphite-muted` — gray, correct)

2. **STEP_STATUS_COLORS** (ArticleDebugPanel.tsx): same semantic hue corrections for Running/Succeeded/Failed.

3. **Error block bg** (ArticleDetail.tsx, ArticleDebugPanel.tsx): `dark:bg-red-950/40` → `dark:bg-red-900/30` for consistency with guide.

4. **hover:bg-gray-50** (ArticleList.tsx): `dark:hover:bg-graphite-hover` → `dark:hover:bg-white/5` per guide.

5. **selected state bg-blue-50** (ArticleList.tsx): `dark:bg-graphite-surface` → `dark:bg-graphite-accent/10` per guide.

6. **text-gray-500** (all files): changed from `dark:text-graphite-faint` to `dark:text-graphite-muted` per guide mapping (`text-gray-800/700/600/500` → `graphite-muted`; `text-gray-400/300` → `graphite-faint`). Affected elements: InProgressView container, article topic/metadata, list empty state, list item topic, debug panel sequence/model/summary labels, empty steps text.

7. **Form inputs** (ArticleGenerationForm.tsx): `dark:bg-graphite-surface` → `dark:bg-graphite-surface-2`, added `dark:placeholder-graphite-faint` per guide's input formula.

8. **Source link colors** (ArticleSourceList.tsx):
   - BookOpen icon `text-green-600` → `dark:text-emerald-400` per guide
   - Web link `text-blue-600` → `dark:text-blue-400` per guide (non-accent info)
   - KB button `text-green-700` → `dark:text-emerald-400`

9. **HtmlContent iframe**: added `key={isDark ? 'dark' : 'light'}` so the iframe remounts on theme change, making dark mode reactive.

## Verification
- `eslint`: exit 0, no errors
- `tsc --noEmit`: 38 lines (unchanged from baseline)
