# Specification: Fix ArticleDetail `HtmlContent` theme reactivity

## Summary
`HtmlContent`, the component in `ArticleDetail.tsx` that renders an article's HTML body inside an `<iframe>`, currently determines dark/light styling by querying `document.documentElement.classList` directly at render time instead of subscribing to the app's reactive `useTheme()` hook. Because this read is not reactive state, toggling the theme while an article detail panel is mounted does not update the iframe's inline colors, leaving it visually stuck on the theme that was active when it first rendered. The fix is to source `isDark` from `useTheme()` so the component re-renders (and the iframe remounts via its existing `key` prop) whenever the theme changes.

## Background
The app uses a `ThemeContext` (`frontend/src/contexts/ThemeContext.tsx`) that exposes a `useTheme()` hook backed by React state (`theme: 'light' | 'dark'`, plus `toggle()`). The provider keeps `document.documentElement`'s `dark` class in sync with this state via a `useEffect`, and persists the choice to `localStorage`.

`HtmlContent` (`frontend/src/features/articles/ArticleDetail.tsx`, lines 13–38) needs to know the current theme to build an inline `srcDoc` HTML string with theme-appropriate colors for the sandboxed iframe (since the iframe's contents are isolated from the app's Tailwind/CSS cascade and dark-mode class, colors must be hardcoded into the generated markup). It currently does this by reading `document.documentElement.classList.contains('dark')` directly on every render call, rather than consuming `useTheme()`.

This DOM read is not React state, so it does not trigger a re-render when the theme changes — it only reflects the DOM's state at whatever moment the component happens to render (e.g., initial mount, or a re-render triggered by unrelated state/props changes such as article data refetching). The component also sets `key={isDark ? 'dark' : 'light'}` on the `<iframe>`, apparently intended to force React to unmount/remount the iframe (and thus regenerate `srcDoc`) when the theme changes — but since `isDark` is only recomputed when `HtmlContent` re-renders for some other reason, the key does not reliably change in response to a theme toggle, so the iframe keeps stale colors after the user switches themes while viewing an article.

This is a small, targeted bug fix within a single component — no new UI, no new data flow, no architectural change.

## Functional Requirements

### FR-1: `HtmlContent` derives `isDark` from `useTheme()`
Replace the direct DOM query with the reactive theme hook.

**Current code (lines 13–14):**
```tsx
function HtmlContent({ html }: { html: string }) {
  const isDark = document.documentElement.classList.contains('dark');
```

**Required change:**
```tsx
function HtmlContent({ html }: { html: string }) {
  const { theme } = useTheme();
  const isDark = theme === 'dark';
```

Add the corresponding import:
```tsx
import { useTheme } from '../../contexts/ThemeContext';
```
(Adjust the relative path to match the actual location of `ThemeContext.tsx` relative to `ArticleDetail.tsx`; based on current repo layout this resolves to `../../contexts/ThemeContext`.)

No other logic in `HtmlContent` changes: the `srcdoc` string construction, the `key={isDark ? 'dark' : 'light'}` prop, the `sandbox`, `className`, `style`, `onLoad` height-adjustment handler, and `title` all remain exactly as they are today.

**Acceptance criteria:**
- `HtmlContent` no longer references `document.documentElement.classList` anywhere in its body.
- `isDark` is derived from the `theme` value returned by `useTheme()` (i.e., `theme === 'dark'`), not from a DOM query.
- With an article detail panel open (an article whose `status === ArticleStatus.Generated` and which has non-empty `htmlContent`), toggling the app theme (light → dark or dark → light) via the existing theme toggle control causes the iframe to remount with updated colors: background, body text color, heading color, and link color all switch to match the new theme, without requiring the user to navigate away and back or refresh the page.
- The iframe's `srcDoc` content (the actual article HTML markup passed in via the `html` prop) is unchanged after a theme toggle — only the inline `<style>` colors differ.
- No other behavior of `ArticleDetail.tsx` (loading state, error state, in-progress state, status badge, source list, feedback section, debug panel) changes.
- `HtmlContent` continues to work correctly when rendered while `ThemeProvider` is an ancestor in the component tree (as it always is in the app's actual render tree) — i.e., no new "must be used within a ThemeProvider" runtime error is introduced under normal app usage.

## Non-Functional Requirements

### NFR-1: Performance
No measurable performance regression. `useTheme()` is a cheap context read already used elsewhere in the app; swapping a synchronous DOM `classList.contains` call for a context read has negligible cost. The existing remount-via-`key` behavior (which was already present, just not correctly triggered) is unchanged in cost — it still fully remounts the iframe on a genuine theme change, which is the existing, accepted mechanism for refreshing `srcDoc`.

### NFR-2: Security
No change. The iframe continues to use `sandbox="allow-same-origin"` and renders content via `srcDoc` exactly as before; this fix does not touch sanitization, sandboxing, or how `html` is interpolated into the generated markup.

## Data Model
No data model changes. No new entities, no API/DTO changes. This is a pure frontend rendering fix scoped to a single component's internal state source.

## API / Interface Design
No new endpoints or events. Internal component interface change only:
- `HtmlContent`'s prop signature (`{ html: string }`) is unchanged.
- Internally, `HtmlContent` now calls `useTheme()` (from `frontend/src/contexts/ThemeContext.tsx`) instead of querying the DOM.

No changes to `ArticleDetail`'s exported default component or its props (`{ articleId: string }`).

## Dependencies
- `frontend/src/contexts/ThemeContext.tsx` — provides `useTheme()`, already implemented and used elsewhere in the app; `ArticleDetail` (and thus `HtmlContent`) must render under a `ThemeProvider` ancestor, which is already guaranteed by the app's root component tree.
- No new third-party libraries or backend changes required.

## Out of Scope
- Any change to how the iframe's `srcDoc` HTML/CSS is generated beyond swapping the theme source (e.g., no restyling, no switching to CSS custom properties, no removing the `key`-based remount trick).
- Any change to `ThemeContext.tsx` itself.
- Any change to other components in `frontend/src/features/articles/` (`ArticleSourceList`, `ArticleFeedbackSection`, `ArticleDebugPanel`, `articleStatusConfig`).
- Broader audit of other components in the codebase for the same DOM-query-instead-of-`useTheme()` anti-pattern; this spec covers only the reported `HtmlContent` instance. (If a codebase-wide sweep is desired, it should be tracked as a separate follow-up item.)
- Adding automated tests beyond what's naturally covered by existing test setup, unless the team's standard practice requires a unit/component test for this specific fix (see Open Questions).

## Open Questions
None.

## Status: COMPLETE
