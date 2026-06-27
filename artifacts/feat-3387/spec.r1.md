# Specification: Dark Mode Fix – RecurringJobsPage Happy-Path Render

## Summary

The happy-path (normal, data-populated) render branch of `RecurringJobsPage.tsx` omits `dark:` Tailwind variants on the page heading and the main content card, causing a broken Graphite dark-mode experience on the most-used code path. This fix adds the two missing variant pairs to bring the happy-path branch into parity with the already-correct error and empty-state branches, satisfying ADR-006.

## Background

ADR-006 (accepted 2026-06-25) mandates that every color-bearing Tailwind class must have a corresponding `dark:` variant so the component renders correctly in both light mode and Graphite dark mode. The error-state and empty-state branches of `RecurringJobsPage` were written with full dark-mode support. The happy-path branch — introduced separately or overlooked during review — was not updated to match. Because the happy path is the default state for any user who has recurring jobs configured, the broken dark mode is prominently visible in normal production use.

## Functional Requirements

### FR-1: Happy-path heading renders correctly in dark mode

The `<h1>` element on line 169 of `frontend/src/pages/RecurringJobsPage.tsx` must include `dark:text-graphite-text` alongside its existing `text-gray-900` class.

**Acceptance criteria:**
- In Graphite dark mode, the "Správa Recurring Jobs" heading is rendered in `graphite-text` colour (not the light-mode `gray-900` colour).
- The fix matches exactly what the error-state heading on line 119 and the empty-state heading on line 151 already render.
- No other classes on the element are changed.

### FR-2: Happy-path content card renders correctly in dark mode

The outer content `<div>` on line 173 must include `dark:bg-graphite-surface` and `dark:shadow-soft-dark` alongside its existing `bg-white` and `shadow` classes.

**Acceptance criteria:**
- In Graphite dark mode, the content card background is `graphite-surface` (not white).
- In Graphite dark mode, the card shadow resolves to the `soft-dark` token.
- The fix matches exactly what the error-state card on line 123 and the empty-state card on line 155 already render.
- No other classes on the element are changed.

## Non-Functional Requirements

### NFR-1: Performance

No performance impact. The change is purely additive Tailwind class tokens; no runtime logic is modified.

### NFR-2: Security

No security implications. The change is limited to CSS utility classes.

### NFR-3: Consistency

The corrected classes must exactly mirror the tokens used in the error and empty-state branches of the same component. No new design tokens are introduced.

## Data Model

Not applicable. This change does not touch data, state, or API contracts.

## API / Interface Design

Not applicable. This is a pure styling fix with no API or interface changes.

## Dependencies

- Tailwind CSS with the project's custom Graphite design token configuration (already present; tokens `graphite-text`, `graphite-surface`, and `shadow-soft-dark` are defined and used elsewhere in the same file).
- ADR-006 (dark mode policy).

## Out of Scope

- Dark-mode audit of any other component or page.
- Changes to the error-state or empty-state branches (already correct).
- Changes to any element inside the happy-path render other than the two identified elements (heading and outer content card).
- Design-token changes or additions.
- Any backend, test, or build changes.

## Open Questions

None.

## Status: COMPLETE
