## Module
Dashboard

## Finding
`frontend/src/components/dashboard/DashboardSettings.tsx` has two dark-mode gaps:

**1. Loading skeleton elements (lines 59-68)** use bare `bg-gray-200` with no `dark:` variant.
In Graphite dark mode these render as near-white blocks on a dark surface — broken contrast.

**2. "New tile" ring (line 141)** uses `ring-blue-200` with no `dark:` variant.
`ring-blue-200` is barely visible on dark backgrounds.

## Why it matters
ADR-006 requires every component that renders color to be correct in both light and dark mode.
These gaps produce broken contrast in the Graphite theme.

## Suggested fix
Apply design-system token mappings per `docs/design/dark-mode-conversion-guide.md`:
- `bg-gray-200` → add `dark:bg-graphite-hover`
- `ring-blue-200` → add `dark:ring-graphite-accent` (matches existing "new/active" ring convention used elsewhere, e.g. TransportBoxList.tsx)

---
_Filed by daily arch-review routine on 2026-06-30. Issue #3440._
