# Implementation r3 — dark-mode-article-components

Two remaining guide violations fixed from review r2:

1. **ArticleGenerationForm.tsx** `<summary>` element: `dark:text-graphite-faint` → `dark:text-graphite-muted` (text-gray-500 maps to muted per guide)

2. **ArticleSourceList.tsx** Globe icon: `dark:text-graphite-accent` → `dark:text-blue-400` (non-accent informational icon, not active state)

Lint: exit 0. No other changes.
