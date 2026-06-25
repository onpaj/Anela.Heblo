### task: fix-invalid-css-selector

#### Fix Invalid CSS Selector in loading.spec.ts

**Files:**
- Modify: `frontend/test/e2e/marketing/loading.spec.ts`

**Context:**  
Line 14 currently reads:
```ts
const calendarLink = page.locator('a[href="/marketing/calendar"], text="Kalendář"').first();
```
The comma-separated `a[href="..."], text="..."` syntax is not valid in Playwright. The `navigateToMarketingCalendar()` helper in `frontend/test/e2e/helpers/e2e-auth-helper.ts` (line 424) uses `page.locator('text="Kalendář"')` to find the same sidebar link, which is the confirmed working pattern.

- [ ] **Step 1: Open and read the current file**  
  Read `frontend/test/e2e/marketing/loading.spec.ts` and confirm line 14 still contains `'a[href="/marketing/calendar"], text="Kalendář"'`.

- [ ] **Step 2: Replace the broken selector**  
  On line 14, change:
  ```ts
  const calendarLink = page.locator('a[href="/marketing/calendar"], text="Kalendář"').first();
  ```
  to:
  ```ts
  const calendarLink = page.locator('text="Kalendář"').first();
  ```

- [ ] **Step 3: Verify only line 14 changed**  
  Run `git diff frontend/test/e2e/marketing/loading.spec.ts` and confirm exactly one line was modified.

- [ ] **Step 4: Commit**  
  ```
  git add frontend/test/e2e/marketing/loading.spec.ts
  git commit -m "fix(e2e): replace invalid mixed CSS selector in loading.spec.ts"
  ```
