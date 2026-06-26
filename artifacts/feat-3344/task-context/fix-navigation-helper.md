# Task Context: fix-navigation-helper

## Goal
Add a readiness wait to `navigateToMarketingCalendar` in `e2e-auth-helper.ts` so the function only returns after the marketing calendar page `h1` heading is confirmed visible.

## Context

**Root cause:** The marketing calendar view-toggle was redesigned. The old "Kalendář" toggle button no longer exists. Tests time out (35s) waiting for it. The page now shows "5 týdnů" / "14 dní" / "Seznam" buttons. The `navigateToMarketingCalendar` helper does not explicitly wait for the page to be ready after navigation, so callers immediately try to find stale selectors.

**Fix:** Add a `waitFor` on `h1 "Marketingový kalendář"` at the end of both navigation paths (sidebar and direct). This is the most stable landmark — present on all viewports, all view states.

**Important:** The sidebar navigation uses `text="Kalendář"` which correctly targets the sidebar `<a>` link element (not a button). Do NOT change this selector. Only add the readiness wait.

## File to modify

`frontend/test/e2e/helpers/e2e-auth-helper.ts`

Current `navigateToMarketingCalendar` function (lines 409–443):

```typescript
export async function navigateToMarketingCalendar(page: any): Promise<void> {
  await navigateToApp(page);

  await waitForLoadingComplete(page);

  const baseUrl = process.env.PLAYWRIGHT_FRONTEND_URL || process.env.PLAYWRIGHT_BASE_URL || 'https://heblo.stg.anela.cz';

  // Try sidebar navigation first
  try {
    console.log('🧭 Attempting UI navigation to marketing calendar via sidebar...');
    const marketingSection = page.locator('button').filter({ hasText: 'Marketing' }).first();
    if (await marketingSection.isVisible({ timeout: 5000 })) {
      await marketingSection.click();
      await waitForLoadingComplete(page);

      const calendarLink = page.locator('text="Kalendář"').first();
      if (await calendarLink.isVisible({ timeout: 5000 })) {
        await calendarLink.click();
        await page.waitForLoadState('domcontentloaded');
        await waitForLoadingComplete(page);
        console.log('✅ UI navigation to marketing calendar successful');
        return;
      }
    }
  } catch (e) {
    console.log('❌ UI navigation failed:', (e as Error).message);
  }

  // Fall back to direct navigation
  console.log('🔄 Trying direct navigation to marketing calendar...');
  await page.goto(`${baseUrl}/marketing/calendar`);
  await page.waitForLoadState('domcontentloaded');
  await waitForLoadingComplete(page);

  console.log('✅ Direct navigation to marketing calendar completed');
}
```

## Implementation steps

1. In the sidebar-navigation success path (before the `return`), add a readiness wait and a clarifying comment:

```typescript
      // Note: 'Kalendář' here is the sidebar <a> link text, not the view-toggle button
      const calendarLink = page.locator('text="Kalendář"').first();
      if (await calendarLink.isVisible({ timeout: 5000 })) {
        await calendarLink.click();
        await page.waitForLoadState('domcontentloaded');
        await waitForLoadingComplete(page);
        // Wait for the page heading to confirm calendar page is loaded
        await page.locator('h1').filter({ hasText: 'Marketingový kalendář' }).waitFor({ timeout: 15000 });
        console.log('✅ UI navigation to marketing calendar successful');
        return;
      }
```

2. In the direct-navigation path (at the end of the function), add a readiness wait:

```typescript
  await page.goto(`${baseUrl}/marketing/calendar`);
  await page.waitForLoadState('domcontentloaded');
  await waitForLoadingComplete(page);
  // Wait for the page heading to confirm calendar page is loaded
  await page.locator('h1').filter({ hasText: 'Marketingový kalendář' }).waitFor({ timeout: 15000 });
  console.log('✅ Direct navigation to marketing calendar completed');
```

## Acceptance criteria
- Both navigation paths (sidebar and direct) wait for `h1 "Marketingový kalendář"` before returning
- The sidebar `text="Kalendář"` selector is unchanged (it is a link, not a button)
- No 35-second timeout occurs for callers of `navigateToMarketingCalendar`
- TypeScript compiles without errors (no new imports needed — `waitFor` is a standard Playwright locator method)
