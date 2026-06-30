# Design: Update Marketing Calendar E2E Selectors

## Component Design

This is a pure test-selector update with no UI or backend changes. The architect review set `Skip Design: true` — there are no new visual components, screens, layouts, or visual design decisions required.

### Files modified (all in-place edits)

**`frontend/test/e2e/helpers/e2e-auth-helper.ts`**
- `navigateToMarketingCalendar`: Add a readiness wait after navigation (both sidebar and direct-navigation paths) using `page.locator('h1').filter({ hasText: 'Marketingový kalendář' }).waitFor({ timeout: 15000 })`. This replaces the implicit reliance on callers waiting for the now-removed "Kalendář" toggle button.

**`frontend/test/e2e/marketing/loading.spec.ts`**
- "should display page heading and toolbar controls": Replace `button … hasText 'Kalendář'` with `button … hasText '5 týdnů'`; add assertion for `button … hasText '14 dní'`
- "should load calendar view by default": Replace `button … hasText 'Kalendář'` with `button … hasText '5 týdnů'` in both the active-state check and the inactive-check for the list toggle remains unchanged

**`frontend/test/e2e/marketing/calendar-view.spec.ts`**
- `beforeEach`: Replace `button … hasText 'Kalendář'` with `button … hasText '5 týdnů'`; variable rename from `calendarToggle` to `calendarToggle` (same name, new selector)

**`frontend/test/e2e/marketing/grid-view.spec.ts`**
- "should deactivate calendar toggle when switching to grid view": Replace `button … hasText 'Kalendář'` with `button … hasText '5 týdnů'`

**`frontend/test/e2e/marketing/mobile-agenda.spec.ts`**
- "renders the mobile agenda view…": Replace `h1 … hasText: 'Kalendář'` with `h1 … hasText: 'Marketingový kalendář'`

**`frontend/test/e2e/marketing/create-record.spec.ts`**
- No changes required

## Data Schemas

Not applicable — no data model changes. This feature modifies only Playwright selector strings in test files.
