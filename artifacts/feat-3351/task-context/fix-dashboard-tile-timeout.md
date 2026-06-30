### task: fix-dashboard-tile-timeout

**Files:**
- Modify: `frontend/test/e2e/core/dashboard.spec.ts`

**Failing test:** `'should display AutoShow tiles automatically'` — line 27 times out in 5 s waiting for `[data-testid^="dashboard-tile-"]`.

**Root cause:** The 5 s `waitForSelector` on line 27 is too short. The dashboard container wait in `beforeEach` uses 10 s (line 10), but the per-test wait is half that. On slower staging environments the tile render takes up to ~12 s after auth.

**Before (line 27):**
```typescript
    await page.waitForSelector('[data-testid^="dashboard-tile-"]', { timeout: 5000 });
```

**After:**
```typescript
    await page.waitForSelector('[data-testid^="dashboard-tile-"]', { timeout: 15000 });
```

- [ ] Open `frontend/test/e2e/core/dashboard.spec.ts`.
- [ ] On line 27, change `{ timeout: 5000 }` to `{ timeout: 15000 }`.
- [ ] Verify the surrounding test block still looks correct — the line reads:
  ```typescript
  test('should display AutoShow tiles automatically', async ({ page }) => {
    // Wait for tiles to load
    await page.waitForSelector('[data-testid^="dashboard-tile-"]', { timeout: 15000 });
  ```
- [ ] Commit:
  ```
  test(e2e): increase dashboard tile waitForSelector timeout to 15s

  The 5s timeout was too short on staging; the tile renders after ~12s
  post-auth. Aligns with the beforeEach container wait (10s) + margin.
  ```
