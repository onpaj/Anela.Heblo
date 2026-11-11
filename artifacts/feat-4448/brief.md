## Module
Leaflet

## Finding
`frontend/src/features/leaflet-generator/LeafletUploadTab.tsx` has zero `dark:` Tailwind classes. Every colored surface, text, border and icon in the component is light-mode-only, producing broken contrast on the Graphite theme.

Concrete gaps:

| Line(s) | Element | Light class | Missing dark |
|---------|---------|-------------|--------------|
| 84, 86, 88, 90 | status labels (`statusLabel`) | `text-blue-600`, `text-green-600`, `text-red-600`, `text-gray-400` | `dark:text-blue-400`, `dark:text-emerald-300`, `dark:text-red-400`, `dark:text-graphite-faint` |
| 102–107 | drop-zone div | `border-gray-300 bg-gray-50 hover:border-gray-400` / `border-blue-400 bg-blue-50` | `dark:border-graphite-border dark:bg-graphite-surface dark:hover:border-graphite-accent` / `dark:border-graphite-accent dark:bg-graphite-accent/10` |
| 108 | Upload icon | `text-gray-400` | `dark:text-graphite-faint` |
| 109 | primary drop text | `text-gray-700` | `dark:text-graphite-text` |
| 110, 112 | secondary/hint text | `text-gray-500`, `text-gray-400` | `dark:text-graphite-muted`, `dark:text-graphite-faint` |
| 124 | file list container | `border-gray-200 divide-gray-100` | `dark:border-graphite-border dark:divide-graphite-border` |
| 128 | FileText icon | `text-gray-400` | `dark:text-graphite-faint` |
| 129 | filename span | `text-gray-700` | `dark:text-graphite-text` |
| 136 | remove button | `text-gray-400 hover:text-gray-600` | `dark:text-graphite-faint dark:hover:text-graphite-text` |
| 160 | "Zrušit vše" button | `border-gray-300 hover:bg-gray-50` | `dark:border-graphite-border dark:hover:bg-white/5 dark:text-graphite-text` |

## Why it matters
ADR-006 (accepted 2026-06-25) requires all components that render color to render correctly in both themes. The upload tab is the sole way administrators ingest leaflet documents. A dark-mode user sees an illegible drop zone (white text on white background for the container), invisible file-list borders, and invisible action buttons.

## Suggested fix
Mirror the graphite token set used by `LeafletDocumentsTab.tsx` (same module, already compliant). Apply `dark:bg-graphite-surface`, `dark:border-graphite-border`, `dark:text-graphite-text / muted / faint`, and status-pill hues following the ADR-006 formula (`~900/30 bg + ~300 text`).

---
_Filed by daily arch-review routine on 2026-10-06._
