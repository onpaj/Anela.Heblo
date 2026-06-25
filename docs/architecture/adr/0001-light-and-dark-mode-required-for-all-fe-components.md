# ADR 0001: Light and Dark Mode Are Required for Every Frontend Component

- **Status:** Accepted
- **Date:** 2026-06-25
- **Deciders:** Frontend team
- **Tags:** frontend, design-system, theming, accessibility

## Context

The application supports a light theme and a "Graphite" dark theme. Theming is implemented with Tailwind's class strategy (`darkMode: 'class'` in `frontend/tailwind.config.js`). The active theme is toggled by adding/removing the `dark` class on `<html>` via `ThemeContext` (`frontend/src/contexts/ThemeContext.tsx`), and persisted in `localStorage` with an initial value derived from `prefers-color-scheme`.

Because the dark theme was rolled out incrementally, many components were authored with light-only utility classes (`bg-white`, `text-gray-900`, `border-gray-200`, …). In dark mode these render with broken contrast (e.g. white panels on a dark page, invisible text). Retrofitting dark mode after the fact is expensive and error-prone, and gaps keep reappearing as new screens are built.

We need a durable rule that prevents new theming gaps rather than repeatedly auditing for them.

## Decision

**Every frontend component that renders color (background, text, border, ring, shadow, divider, icon, or status color) MUST render correctly in both light and dark mode.** A component is not "done" until it is verified in both themes.

Concretely:

1. **No light-only color utilities.** Any element with a light color utility must carry a matching `dark:` variant, OR use a shared design-system class (`.card`, `.input`, `.btn-*`, `.text-h*`, `.text-body*`, `.badge-*`) that already encodes both themes. Prefer the design-system class.
2. **Use the Graphite token scale, not ad-hoc hex/grays.** Map raw light classes to the `graphite-*` tokens and semantic dark variants per the conversion guide (surfaces → `graphite-surface`/`surface-2`, text → `graphite-text`/`muted`/`faint`, borders → `graphite-border`, accents → `graphite-accent`, status pills keep their hue at `~900/30` bg + `~300` text). See the mapping table in the dark-mode guide referenced below.
3. **Light classes are never removed.** Dark support is additive: keep the light class and append the `dark:` variant.
4. **Verify in both themes before merge.** Toggle the theme (or use the Playwright dev instance) and confirm contrast, surfaces, hover/active/selected states, status badges, modals, and tab controls all read correctly.
5. **Accessibility.** Both themes must keep text/UI contrast at WCAG 2.1 AA, consistent with the design document's accessibility principle.

This applies to all routes, modals/dialogs, drawers, tab panels, tables, forms, badges, and shared/reused components.

## Consequences

**Positive**
- Dark mode stays complete by construction; no recurring full-app audits.
- Consistent visual language via shared tokens instead of one-off colors.
- New screens are theme-correct on first review.

**Negative / costs**
- Slightly more work per component (a `dark:` variant per colored element).
- Reviewers must check both themes; PRs touching UI should state that both were verified.

**Enforcement**
- Code review checklist item: "Renders correctly in light AND dark mode."
- Recommended follow-up (not yet implemented): an ESLint/Tailwind lint or CI check that flags light-only color utilities (`bg-white`, `text-gray-*`, `border-gray-*`, etc.) lacking a `dark:` sibling and not part of a design-system class.

## References

- Conversion mapping & token table: `docs/architecture/adr/dark-mode-conversion-guide.md` (raw light class → graphite `dark:` variant).
- Theme implementation: `frontend/src/contexts/ThemeContext.tsx`, `frontend/tailwind.config.js`, `frontend/src/index.css` (design-system component classes with built-in `dark:` variants).
- Design system: `docs/design/ui_design_document.md`.
- Decision summary for cross-session memory: `memory/decisions/light-dark-mode-required.md`.
