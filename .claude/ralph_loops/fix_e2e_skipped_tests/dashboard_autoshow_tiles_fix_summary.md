# Dashboard AutoShow Tiles Test Fix - Summary

## Test Information
- **File**: `frontend/test/e2e/dashboard.spec.ts`
- **Test Name**: "should display AutoShow tiles automatically"
- **Current Line**: 25
- **Status**: ✅ **FIXED - Test is passing**

## Problem Analysis

### Original Issue
The test was marked as skipped with the following reason:
```
SKIPPED: Application implementation issue - Missing dashboard tile elements or data-testid attributes.
Expected behavior: Test should verify that AutoShow tiles (like background-tasks) are displayed automatically.
Actual behavior: Timeout waiting for '[data-testid^="dashboard-tile-"]' selector, indicating that
dashboard tiles either don't have the data-testid attributes or the dashboard is not loading tiles properly.
Error: Timeout waiting for selector '[data-testid^="dashboard-tile-"]' (5000ms)
This is the same issue as previous test - missing data-testid attributes on dashboard tiles.
Recommendation: Add data-testid="dashboard-tile-{tileName}" to each dashboard tile component.
```

### Investigation Findings
Upon investigation, I found that:
1. ✅ **data-testid attributes ARE correctly implemented** in `DashboardTile.tsx` at line 55: `data-testid={`dashboard-tile-${tile.tileId}`}`
2. ❌ **The test was using the WRONG tile ID**
3. 🔍 **Root cause**: Misunderstanding of tile ID generation logic

### Understanding Tile ID Generation
The backend generates tile IDs from class names using `TileExtensions.cs`:
```csharp
public static string GetTileId(this Type tileType) =>
    tileType.Name.ToLowerInvariant().Replace("tile", "");
```

**Transformation example:**
- Backend class: `BackgroundTaskStatusTile`
- Generated TileId: `backgroundtaskstatus` (lowercase, "tile" removed)
- Final data-testid: `dashboard-tile-backgroundtaskstatus`

**The test was incorrectly looking for:**
- ❌ `dashboard-tile-background-tasks` (with hyphens)

**The correct tile ID is:**
- ✅ `dashboard-tile-backgroundtaskstatus` (all lowercase, no separators)

## Solution Implemented

### Test Updates (Lines 25-36)
The test was updated to use the correct tile ID:

**Before (incorrect):**
```typescript
test.skip('should display AutoShow tiles automatically', async ({ page }) => {
  // Wait for tiles to load
  await page.waitForSelector('[data-testid^="dashboard-tile-"]', { timeout: 5000 });

  // Check if background-tasks tile (AutoShow: true) is visible
  const backgroundTasksTile = page.locator('[data-testid="dashboard-tile-background-tasks"]');
  await expect(backgroundTasksTile).toBeVisible();

  // Verify the tile has content
  await expect(backgroundTasksTile.locator('.tile-title')).toContainText('Stav background tasků');
});
```

**After (correct):**
```typescript
test('should display AutoShow tiles automatically', async ({ page }) => {
  // Wait for tiles to load
  await page.waitForSelector('[data-testid^="dashboard-tile-"]', { timeout: 5000 });

  // Check if background task status tile (AutoShow: true) is visible
  // TileId is generated from class name: BackgroundTaskStatusTile -> backgroundtaskstatus
  const backgroundTasksTile = page.locator('[data-testid="dashboard-tile-backgroundtaskstatus"]');
  await expect(backgroundTasksTile).toBeVisible();

  // Verify the tile has content (using the actual class name from DashboardTile component)
  await expect(backgroundTasksTile.locator('.text-sm.font-medium')).toContainText('Stav background tasků');
});
```

### Key Changes
1. ✅ Removed `test.skip()` - test is now active
2. ✅ Removed all the skip comment documentation (lines 25-31 in original)
3. ✅ Changed tile selector from `dashboard-tile-background-tasks` to `dashboard-tile-backgroundtaskstatus`
4. ✅ Updated content selector from `.tile-title` to `.text-sm.font-medium` (actual class used in `DashboardTile.tsx`)
5. ✅ Added explanatory comment about tile ID generation

## Verification

### Test Execution Results
```bash
./scripts/run-playwright-tests.sh dashboard.spec.ts
```

**Output:**
```
✅ Tests completed successfully!
  ✓ Dashboard › should display dashboard tiles (5.3s)
  ✓ Dashboard › should display AutoShow tiles automatically (7.6s)
  ✓ Dashboard › should open dashboard settings (4.5s)
  ✓ Dashboard › should be able to enable/disable tiles (5.5s)
  ✓ Dashboard › should support drag and drop to reorder tiles (5.5s)
  - Dashboard › should display empty state for production tile with no orders (skipped)

1 skipped
5 passed (29.7s)
```

### Available Tiles on Dashboard
During debugging, I discovered there are **15 tiles** currently registered on the staging dashboard:
1. `dashboard-tile-materialinventorycount`
2. `dashboard-tile-productinventorycount`
3. `dashboard-tile-productinventorysummary`
4. `dashboard-tile-materialwithexpirationinventorysummary`
5. `dashboard-tile-materialwithoutexpirationinventorysummary`
6. `dashboard-tile-lowstockalert`
7. `dashboard-tile-lowstockefficiency`
8. `dashboard-tile-todayproduction`
9. `dashboard-tile-nextdayproduction`
10. `dashboard-tile-manualactionrequired`
11. `dashboard-tile-intransitboxes`
12. `dashboard-tile-receivedboxes`
13. `dashboard-tile-errorboxes`
14. `dashboard-tile-criticalgiftpackages`
15. `dashboard-tile-backgroundtaskstatus` ← The AutoShow tile tested

## Related Files Modified
- ✅ `/Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend/test/e2e/dashboard.spec.ts` (lines 25-36)

## Related Backend Code Reviewed
- `/Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend/src/Anela.Heblo.Xcc/Services/Dashboard/Tiles/BackgroundTaskStatusTile.cs`
  - Defines the tile with `AutoShow = true` (line 15)
  - Title: "Stav background tasků" (line 10)
- `/Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend/src/Anela.Heblo.Xcc/Services/Dashboard/TileExtensions.cs`
  - Defines tile ID generation logic (line 5)
- `/Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend/src/Anela.Heblo.Xcc/XccModule.cs`
  - Registers the tile (line 25): `services.RegisterTile<BackgroundTaskStatusTile>();`

## Related Frontend Code Reviewed
- `/Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend/src/components/dashboard/DashboardTile.tsx`
  - Confirms data-testid implementation (line 55): `data-testid={`dashboard-tile-${tile.tileId}`}`
  - Confirms content class (lines 58-60): Title uses `.text-sm.font-medium` class

## Conclusion

This test was incorrectly skipped due to a simple tile ID mismatch. The issue was NOT:
- ❌ Missing data-testid attributes (they exist and work correctly)
- ❌ Missing tile implementation (tile is properly registered and functional)
- ❌ Application bug (everything works as designed)

The issue WAS:
- ✅ **Test used wrong tile ID format** - used hyphenated format instead of the actual lowercase, concatenated format

The fix is simple, safe, and the test now correctly validates that AutoShow tiles (specifically the `BackgroundTaskStatusTile`) are automatically displayed on the dashboard.

**Test Status: ✅ PASSING**
