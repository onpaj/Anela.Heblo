# Design r1 — feat-3365

## Summary

No new components, no visual design changes in light mode. In dark mode, all six Article
sub-components gain Graphite palette rendering consistent with `ArticlesPage.tsx`.

## Colour mapping (final, incorporating arch-review corrections)

| Light class(es) | Dark variant |
|---|---|
| `text-gray-900`, `text-gray-800` | `dark:text-graphite-text` |
| `text-gray-700`, `text-gray-600` | `dark:text-graphite-muted` |
| `text-gray-500`, `text-gray-400` | `dark:text-graphite-faint` |
| `bg-gray-50`, `bg-gray-100` | `dark:bg-graphite-surface-2` |
| `border-gray-*`, `divide-gray-*`, bare `border-t` | `dark:border-graphite-border` |
| `hover:bg-gray-50` | `dark:hover:bg-graphite-hover` |
| `bg-blue-50` (selected) | `dark:bg-graphite-surface` |
| `text-blue-500`, `text-blue-600` | `dark:text-graphite-accent` |
| `text-blue-700`, `bg-blue-100` | `dark:text-graphite-accent dark:bg-graphite-surface-2` |
| `text-purple-700`, `bg-purple-100` | `dark:text-graphite-accent-strong dark:bg-graphite-surface-2` |
| `text-green-700`, `bg-green-100` | `dark:text-graphite-text dark:bg-graphite-surface-2` |
| `text-green-600` (KB icon) | `dark:text-graphite-accent` |
| `text-gray-700`, `bg-gray-100` (Queued badge) | `dark:text-graphite-muted dark:bg-graphite-surface-2` |
| `text-red-700`, `bg-red-100` | `dark:text-red-400 dark:bg-red-950/40` |
| `bg-red-50 border-red-200 text-red-700` (error block) | `dark:bg-red-950/40 dark:border-red-800 dark:text-red-400` |
| `text-red-600` | `dark:text-red-400` |
| `text-amber-600` | `dark:text-amber-400` |
| `border-gray-300` inputs | `dark:border-graphite-border dark:bg-graphite-surface dark:text-graphite-text` |
| Submit button `bg-blue-600 hover:bg-blue-700` | **no dark: override** (arch-review §Decision) |
| Checkbox `text-blue-600 focus:ring-blue-500` | `dark:text-graphite-accent dark:focus:ring-graphite-accent` |

## HtmlContent iframe

```js
const isDark = document.documentElement.classList.contains('dark');
const srcdoc = `<!DOCTYPE html><html><head><meta charset="utf-8"><style>
  body{font-family:system-ui,sans-serif;line-height:1.6;color:${isDark ? '#E6E8EC' : '#1f2937'};background:${isDark ? '#202327' : 'transparent'};padding:1rem;margin:0}
  h1,h2,h3{color:${isDark ? '#E6E8EC' : '#111827'}}p{margin:0 0 1em}ul,ol{padding-left:1.5em}
  a{color:${isDark ? '#38BDF8' : '#2563eb'}}
</style></head><body>${html}</body></html>`;
```
