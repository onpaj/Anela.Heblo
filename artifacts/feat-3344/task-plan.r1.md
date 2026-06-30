# Task Plan: Update Marketing Calendar E2E Selectors

## Overview

This plan covers the targeted selector updates to fix ~15 marketing E2E test failures. All changes are in existing test files only. No production code is touched. The plan is organized as two tasks: the shared helper fix (which unblocks all specs) and the spec-file selector updates.

---

### task: fix-navigation-helper

**Goal:** Add a readiness wait to `navigateToMarketingCalendar` in `e2e-auth-helper.ts` so the function only returns after the calendar page is confirmed loaded.

**Files to modify:**
- `frontend/test/e2e/helpers/e2e-auth-helper.ts`

**Implementation steps:**

1. Locate the `navigateToMarketingCalendar` export function (lines 409–443 in current file).
2. After the `console.log('✅ UI navigation to marketing calendar successful');` line (after sidebar navigation succeeds), add:
   ```typescript
   await page.locator('h1').filter({ hasText: 'Marketingový kalendář' }).waitFor({ timeout: 15000 });
   ```
3. After the `console.log('✅ Direct navigation to marketing calendar completed');` line (after direct navigation), add the same wait:
   ```typescript
   await page.locator('h1').filter({ hasText: 'Marketingový kalendář' }).waitFor({ timeout: 15000 });
   ```
4. Add a comment above the sidebar `text="Kalendář"` click clarifying it targets the sidebar `<a>` link, not the view-toggle button:
   ```typescript
   // Note: 'Kalendář' here is the sidebar <a> link text, not the view-toggle button
   ```

**Acceptance criteria:**
- `navigateToMarketingCalendar` waits for `h1 "Marketingový kalendář"` to be visible before returning
- The sidebar navigation path `text="Kalendář"` (sidebar link) is unchanged
- No 35-second timeout occurs

---

### task: fix-spec-selectors

**Goal:** Replace all stale `button … hasText 'Kalendář'` view-toggle selectors and the `h1 hasText 'Kalendář'` mobile assertion across the four affected spec files.

**Files to modify:**
- `frontend/test/e2e/marketing/loading.spec.ts`
- `frontend/test/e2e/marketing/calendar-view.spec.ts`
- `frontend/test/e2e/marketing/grid-view.spec.ts`
- `frontend/test/e2e/marketing/mobile-agenda.spec.ts`

**Implementation steps:**

**loading.spec.ts:**

1. Test "should display page heading and toolbar controls" (around line 29):
   - Replace: `page.locator('button').filter({ hasText: 'Kalendář' }).first()`
   - With: `page.locator('button').filter({ hasText: '5 týdnů' }).first()`
   - Add after the "5 týdnů" visibility assertion:
     ```typescript
     await expect(page.locator('button').filter({ hasText: '14 dní' }).first()).toBeVisible();
     ```

2. Test "should load calendar view by default" (around line 36):
   - Replace: `page.locator('button').filter({ hasText: 'Kalendář' }).first()` (the `calendarToggle` variable)
   - With: `page.locator('button').filter({ hasText: '5 týdnů' }).first()`
   - The `bg-indigo-600` class check and the listToggle/Seznam check remain unchanged

**calendar-view.spec.ts:**

3. `beforeEach` block (around line 9):
   - Replace: `page.locator('button').filter({ hasText: 'Kalendář' }).first()`
   - With: `page.locator('button').filter({ hasText: '5 týdnů' }).first()`
   - The `toHaveClass(/bg-indigo-600/)` assertion is unchanged; only the selector text changes

**grid-view.spec.ts:**

4. Test "should deactivate calendar toggle when switching to grid view" (around line 15):
   - Replace: `page.locator('button').filter({ hasText: 'Kalendář' }).first()`
   - With: `page.locator('button').filter({ hasText: '5 týdnů' }).first()`
   - The `not.toHaveClass(/bg-indigo-600/)` assertion is unchanged

**mobile-agenda.spec.ts:**

5. Test "renders the mobile agenda view, not the desktop calendar grid" (around line 12):
   - Replace: `page.locator('h1').filter({ hasText: 'Kalendář' })`
   - With: `page.locator('h1').filter({ hasText: 'Marketingový kalendář' })`

**Acceptance criteria:**
- No `button … hasText 'Kalendář'` selectors remain in any marketing spec file
- `loading.spec.ts` asserts "5 týdnů" and "14 dní" buttons are visible
- `calendar-view.spec.ts` `beforeEach` clicks "5 týdnů" and asserts it has `bg-indigo-600`
- `grid-view.spec.ts` asserts "5 týdnů" button does NOT have `bg-indigo-600` when list view active
- `mobile-agenda.spec.ts` asserts `h1` contains "Marketingový kalendář"
- `create-record.spec.ts` is untouched

---

## Execution order

1. `fix-navigation-helper` — must land first; it is the shared root-cause fix
2. `fix-spec-selectors` — can be executed in any order relative to each other within this task; all four spec files are independent

## Verification

After both tasks: run `npx playwright test frontend/test/e2e/marketing/ --project=chromium` against staging. All 5 spec files should pass without 35-second timeouts.
