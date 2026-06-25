### task: fix-sidebar-section-label-and-ordering-test

**Goal:** Replace every reference to the non-existent "Personální" sidebar section
with the actual section label "Anela", and fix the ordering test so it validates
the real sidebar order (Anela → Sklad → Administrace) rather than the incorrect
order (Sklad → Personální → Automatizace).

**Files:**
- `frontend/test/e2e/core/sidebar-navigation.spec.ts` (only file modified)

**Steps:**

1. **Test 1 — display section with Struktura link (line 13–28)**

   Change the test description and the section locator:
   - Line 13: rename test description from
     `'should display Personální section with Struktura link'`
     to `'should display Anela section with Struktura link'`
   - Line 15: change locator name from `/Personální/i` to `/Anela/i`
   - Line 15: rename the variable from `personalniSection` to `anelaSection`
   - Update all uses of `personalniSection` in this test (lines 16, 19) to `anelaSection`

2. **Test 2 — open Struktura in new window (line 30–53)**

   Change only the section locator; everything else stays:
   - Line 32: change locator name from `/Personální/i` to `/Anela/i`
   - Line 32: rename the variable from `personalniSection` to `anelaSection`
   - Line 33: update the `click()` call from `personalniSection.click()` to `anelaSection.click()`

3. **Test 3 — ordering test (line 55–71)**

   This test must be rewritten to reflect the real sidebar order (Anela first,
   then Sklad, then Administrace):
   - Line 55: rename test description from
     `'should display Personální section between Sklad and Automatizace'`
     to `'should display Anela section before Sklad and Administrace'`
   - Line 57: change the filter regex from
     `/^(Sklad|Personální|Automatizace)$/`
     to `/^(Anela|Sklad|Administrace)$/`
   - Line 64: rename variable `personalniIndex` to `anelaIndex` and change the
     `includes` argument from `'Personální'` to `'Anela'`
   - Line 65: rename variable `automatizaceIndex` to `administraceIndex` and change
     the `includes` argument from `'Automatizace'` to `'Administrace'`
   - Line 68: update assertion to check `anelaIndex` is ≥ 0 (replacing
     `personalniIndex`)
   - Line 69: change assertion to `expect(skladIndex).toBeGreaterThan(anelaIndex)`
     (Sklad comes *after* Anela)
   - Line 70: change variable and assertion to
     `expect(administraceIndex).toBeGreaterThan(skladIndex)`
     (Administrace comes after Sklad)
   - Remove or do not reference `automatizaceIndex` / `personalniIndex` anywhere

**After editing, the complete test file should look like this:**

```typescript
import { test, expect } from '@playwright/test';
import { navigateToApp } from '../helpers/e2e-auth-helper';

test.describe('Sidebar Navigation', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to application with full authentication
    await navigateToApp(page);

    // Wait for page to be fully loaded
    await page.waitForLoadState('domcontentloaded');
  });

  test('should display Anela section with Struktura link', async ({ page }) => {
    // Find and click on Anela section
    const anelaSection = page.getByRole('button', { name: /Anela/i });
    await expect(anelaSection).toBeVisible();

    // Expand the section
    await anelaSection.click();

    // Check if Struktura link is visible with external link icon
    const strukturaLink = page.getByRole('button', { name: /Struktura/i });
    await expect(strukturaLink).toBeVisible();

    // Verify the external link icon is present (ExternalLink component)
    const externalIcon = strukturaLink.locator('svg').last();
    await expect(externalIcon).toBeVisible();
  });

  test('should open Struktura in new window', async ({ page, context }) => {
    // Find and expand Anela section
    const anelaSection = page.getByRole('button', { name: /Anela/i });
    await anelaSection.click();

    // Create a promise that resolves when a new page is opened
    const newPagePromise = context.waitForEvent('page');

    // Click on Struktura link
    const strukturaLink = page.getByRole('button', { name: /Struktura/i });
    await strukturaLink.click();

    // Wait for the new page to open
    const newPage = await newPagePromise;

    // Wait for the new page to load
    await newPage.waitForLoadState();

    // Verify the URL is correct
    expect(newPage.url()).toBe('https://orgchart.anela.cz/');

    // Close the new page
    await newPage.close();
  });

  test('should display Anela section before Sklad and Administrace', async ({ page }) => {
    // Get all section buttons in the sidebar
    const sectionButtons = page.locator('nav button:has(svg)').filter({ hasText: /^(Anela|Sklad|Administrace)$/ });

    // Get text content of all sections
    const sectionTexts = await sectionButtons.allTextContents();

    // Find indices
    const anelaIndex = sectionTexts.findIndex(text => text.includes('Anela'));
    const skladIndex = sectionTexts.findIndex(text => text.includes('Sklad'));
    const administraceIndex = sectionTexts.findIndex(text => text.includes('Administrace'));

    // Verify order: Anela first, then Sklad, then Administrace
    expect(anelaIndex).toBeGreaterThanOrEqual(0);
    expect(skladIndex).toBeGreaterThan(anelaIndex);
    expect(administraceIndex).toBeGreaterThan(skladIndex);
  });
});
```

**Success criteria:**
- `frontend/test/e2e/core/sidebar-navigation.spec.ts` contains zero occurrences of
  the strings `Personální` and `Automatizace`.
- The file contains exactly three occurrences of `/Anela/i` (one per test).
- The ordering test filter regex is `/^(Anela|Sklad|Administrace)$/`.
- The ordering assertions verify `anelaIndex ≥ 0`, `skladIndex > anelaIndex`,
  and `administraceIndex > skladIndex`.
- No other file in the repository is modified.
- Running `npx playwright test frontend/test/e2e/core/sidebar-navigation.spec.ts`
  against a live staging environment passes all three tests without errors.
