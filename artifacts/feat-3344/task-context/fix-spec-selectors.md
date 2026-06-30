# Task Context: fix-spec-selectors

## Goal
Replace all stale `button … hasText 'Kalendář'` view-toggle selectors and the `h1 hasText 'Kalendář'` mobile assertion in the four affected marketing spec files.

## Context

**Root cause:** The marketing calendar view-toggle was redesigned. The old single "Kalendář" toggle button is gone. The new buttons are:
- "5 týdnů" — 5-week calendar/month grid (the default active view)
- "14 dní" — 14-day view
- "Seznam" — list/grid view

All `button … hasText 'Kalendář'` selectors in spec files that reference the view-toggle must be changed to `button … hasText '5 týdnů'`.

Additionally, `mobile-agenda.spec.ts` incorrectly checks for an `h1` with text "Kalendář"; the heading is actually "Marketingový kalendář".

`create-record.spec.ts` has no stale selectors and requires no changes.

## Files to modify

### 1. `frontend/test/e2e/marketing/loading.spec.ts`

**Test: "should display page heading and toolbar controls"** (around line 28–33)

Current:
```typescript
    // View toggle buttons
    await expect(page.locator('button').filter({ hasText: 'Kalendář' }).first()).toBeVisible();
    await expect(page.locator('button').filter({ hasText: 'Seznam' }).first()).toBeVisible();
```

Replace with:
```typescript
    // View toggle buttons
    await expect(page.locator('button').filter({ hasText: '5 týdnů' }).first()).toBeVisible();
    await expect(page.locator('button').filter({ hasText: '14 dní' }).first()).toBeVisible();
    await expect(page.locator('button').filter({ hasText: 'Seznam' }).first()).toBeVisible();
```

**Test: "should load calendar view by default"** (around line 36–45)

Current:
```typescript
    // Calendar toggle should have active (indigo) styling
    const calendarToggle = page.locator('button').filter({ hasText: 'Kalendář' }).first();
    await expect(calendarToggle).toHaveClass(/bg-indigo-600/);

    // List toggle should not be active
    const listToggle = page.locator('button').filter({ hasText: 'Seznam' }).first();
    await expect(listToggle).not.toHaveClass(/bg-indigo-600/);
```

Replace with:
```typescript
    // Calendar toggle should have active (indigo) styling
    const calendarToggle = page.locator('button').filter({ hasText: '5 týdnů' }).first();
    await expect(calendarToggle).toHaveClass(/bg-indigo-600/);

    // List toggle should not be active
    const listToggle = page.locator('button').filter({ hasText: 'Seznam' }).first();
    await expect(listToggle).not.toHaveClass(/bg-indigo-600/);
```

### 2. `frontend/test/e2e/marketing/calendar-view.spec.ts`

**`beforeEach` block** (around lines 8–11)

Current:
```typescript
    // Ensure calendar view is active (it is the default, but be explicit)
    const calendarToggle = page.locator('button').filter({ hasText: 'Kalendář' }).first();
    await calendarToggle.click();
    await expect(calendarToggle).toHaveClass(/bg-indigo-600/);
```

Replace with:
```typescript
    // Ensure calendar view is active (it is the default, but be explicit)
    const calendarToggle = page.locator('button').filter({ hasText: '5 týdnů' }).first();
    await calendarToggle.click();
    await expect(calendarToggle).toHaveClass(/bg-indigo-600/);
```

### 3. `frontend/test/e2e/marketing/grid-view.spec.ts`

**Test: "should deactivate calendar toggle when switching to grid view"** (around lines 14–16)

Current:
```typescript
    const calendarToggle = page.locator('button').filter({ hasText: 'Kalendář' }).first();
    await expect(calendarToggle).not.toHaveClass(/bg-indigo-600/);
```

Replace with:
```typescript
    const calendarToggle = page.locator('button').filter({ hasText: '5 týdnů' }).first();
    await expect(calendarToggle).not.toHaveClass(/bg-indigo-600/);
```

### 4. `frontend/test/e2e/marketing/mobile-agenda.spec.ts`

**Test: "renders the mobile agenda view, not the desktop calendar grid"** (around line 12)

Current:
```typescript
    await expect(page.locator('h1').filter({ hasText: 'Kalendář' })).toBeVisible({ timeout: 10000 });
```

Replace with:
```typescript
    await expect(page.locator('h1').filter({ hasText: 'Marketingový kalendář' })).toBeVisible({ timeout: 10000 });
```

## Acceptance criteria
- No `button … hasText 'Kalendář'` selectors remain in any marketing spec file (grep should return zero results)
- `loading.spec.ts` now asserts "5 týdnů", "14 dní", and "Seznam" buttons are visible; also checks "5 týdnů" is active by default
- `calendar-view.spec.ts` `beforeEach` clicks "5 týdnů" and asserts `bg-indigo-600`
- `grid-view.spec.ts` deactivation test checks "5 týdnů" for absence of `bg-indigo-600`
- `mobile-agenda.spec.ts` asserts `h1` contains "Marketingový kalendář"
- `create-record.spec.ts` is NOT modified
- TypeScript compiles without errors (no type changes — all edits are string literal replacements)
