# Decision: Light AND Dark Mode Required for Every FE Component

**Decision:** Every frontend component that renders color must render correctly in both light and the "Graphite" dark theme. A component is not done until verified in both. (ADR-006 in `docs/architecture/development_guidelines.md`.)

**Why:** Dark mode (`darkMode: 'class'`, toggled via `ThemeContext` on `<html>`) was rolled out incrementally, leaving many components light-only (white panels / invisible text in dark mode). Retrofitting is costly; an additive, always-on rule prevents new gaps.

**How to apply:**
- Every colored element needs a matching `dark:` variant, OR use a design-system class (`.card`, `.input`, `.btn-*`, `.text-h*`, `.text-body*`, `.badge-*`) that already encodes both themes — prefer the latter.
- Map raw light classes to `graphite-*` tokens per `docs/design/dark-mode-conversion-guide.md` (surfaces→`graphite-surface`/`surface-2`, text→`graphite-text`/`muted`/`faint`, borders→`graphite-border`, accent→`graphite-accent`; status pills keep hue at `~900/30` bg + `~300` text).
- Additive only: never remove light classes.
- Verify both themes before merge (toggle or Playwright on :3100). Covers routes, modals, drawers, tab panels, tables, forms, badges, shared components.
- WCAG 2.1 AA contrast in both themes.
