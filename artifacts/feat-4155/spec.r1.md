# Specification: OrgChartPage outer wrapper missing dark mode theming (ADR-006)

## Summary
`frontend/src/pages/OrgChartPage.tsx` renders its outermost full-viewport wrapper with a hardcoded light-only gradient (`bg-gradient-to-br from-indigo-500 to-purple-600`) and no `dark:` override, violating ADR-006 ("Light and Dark Mode Required for Every Frontend Component"). This fix adds the missing dark-mode variant so the wrapper matches the Graphite theme already used by its children, with zero change to light-mode appearance or to any other part of the page.

## Background
ADR-006 (accepted 2026-06-25, `docs/architecture/development_guidelines.md`) requires every component that renders color to work correctly in both the light theme and the "Graphite" dark theme (Tailwind `darkMode: 'class'`, toggled via `ThemeContext` on `<html>`). This was rolled out incrementally, leaving isolated gaps on already-shipped screens; the daily arch-review routine's job is to find and file these gaps as they surface.

On `OrgChartPage.tsx`:
- Line 195 (outermost wrapper): `bg-gradient-to-br from-indigo-500 to-purple-600` — **no `dark:` variant**. This is the bug.
- Line 197 (controls bar): already themed correctly (`dark:bg-graphite-surface`, `dark:border-graphite-border`, etc.).
- Line 276 (scrollable chart canvas area): already themed correctly (`dark:from-graphite-bg dark:to-graphite-bg`).

Because the controls bar and canvas area are opaque and correctly themed, in normal layout the bright gradient on the outer wrapper is not visible. It becomes visible in two situations: (1) the outer wrapper is `h-screen` while its content can exceed the viewport height, so the bright gradient shows through below the last child during scroll/overscroll, and (2) any future gap or margin between children exposes it directly. In dark mode this bright indigo/purple gradient clashes with the surrounding Graphite dark surfaces, breaking contrast and visual consistency — exactly the class of defect ADR-006 exists to prevent.

## Functional Requirements

### FR-1: Add dark-mode override to the OrgChartPage outer wrapper
The outermost `<div>` in `frontend/src/pages/OrgChartPage.tsx` (currently line 195) must render using Graphite dark-theme tokens when `dark` mode is active, while preserving the existing indigo-to-purple gradient in light mode unchanged.

Change:
```tsx
<div className="h-screen bg-gradient-to-br from-indigo-500 to-purple-600 flex flex-col">
```
to:
```tsx
<div className="h-screen bg-gradient-to-br from-indigo-500 to-purple-600 dark:from-graphite-bg dark:to-graphite-bg flex flex-col">
```

This mirrors the pattern already used one section down on the scrollable chart canvas wrapper (line 276: `dark:from-graphite-bg dark:to-graphite-bg`), keeping the fix consistent with an established convention in the same file rather than introducing a new one.

**Acceptance criteria:**
- In light mode (no `dark` class on `<html>`), `OrgChartPage` renders pixel-identical to its current behavior — the indigo-to-purple gradient wrapper is unchanged.
- In dark mode (`dark` class on `<html>` / Graphite theme active), the outer wrapper renders with `graphite-bg` (flat, matching the canvas area background) instead of the indigo/purple gradient — no bright gradient is visible at any point during scroll, overscroll, or through any gap between child elements.
- No other lines in `OrgChartPage.tsx` are modified — this is a single-line, single-class-list change.
- Both themes are manually verified (theme toggle, or the Playwright dev instance on `:3100`) per the ADR-006 verification step, per `memory/decisions/light-dark-mode-required.md`.

## Non-Functional Requirements

### NFR-1: Visual consistency / accessibility
The dark-mode wrapper color must match the Graphite tokens already in use by sibling elements in this component (`graphite-bg`, as used at line 276) so there is no visible seam or contrast break between the outer wrapper and the canvas area beneath it. No new WCAG contrast requirement is introduced by this change since the wrapper itself carries no text content — the requirement is purely to eliminate the bright-gradient bleed-through in dark mode.

### NFR-2: No regression risk
This is an additive Tailwind utility-class change (`dark:` variants only). No behavioral, state, layout, or data-flow code is touched. No new dependencies. No API surface, DTO, or contract changes.

## Data Model
Not applicable — this is a pure presentation/styling change to a single React component; no data model, entity, or persistence changes are involved.

## API / Interface Design
Not applicable — no endpoints, events, hooks, or props change. `OrgChartPage.tsx`'s outer `<div>` gains two dark-mode Tailwind utility classes (`dark:from-graphite-bg dark:to-graphite-bg`); its DOM structure, component tree, and public interface are unchanged.

## Dependencies
- Existing Tailwind `graphite-bg` design token, already defined and in use elsewhere in this same file (line 276) and across the codebase's dark-mode conversion (`docs/design/dark-mode-conversion-guide.md`).
- No new libraries, services, or feature flags.

## Out of Scope
- Any other OrgChart styling, layout, or behavioral change not called out in the "Suggested fix" of the originating issue.
- Any change to the controls bar (line 197) or chart canvas area (line 276) — both are already correctly themed and are not part of this finding.
- Introducing a lint/CI check for light-only color utilities lacking a `dark:` sibling — noted as a recommended follow-up in ADR-006 but not part of this fix.
- Any change to other pages or components beyond `OrgChartPage.tsx`.

## Open Questions

None.

## Status: COMPLETE
