## Summary

Add a new dashboard tile that shows the result of the previous day's DQT (Data Quality Test) run — specifically the failure count and mismatch summary. This gives a quick at-a-glance view every morning of whether yesterday's invoice data quality check found any problems.

The existing **Kvalita dat** tile (`dataqualitystatus`) shows the *most recent* DQT run regardless of when it ran. The new tile is scoped to *yesterday's date* so it always reflects a fixed, predictable period.

## Motivation

Users open the dashboard each morning. They need to know immediately whether yesterday's automated DQT run detected mismatches or failed to complete, without navigating to `/automation/data-quality` and searching for the correct run.

## Scope

### Backend — new tile class

- New file: `backend/src/.../Features/DataQuality/DashboardTiles/DqtYesterdayStatusTile.cs`
- Implements `ITile` (same pattern as `DataQualityStatusTile`)
- `GetTileId()` → `"dqtyesterdaystatus"`
- `Title` → `"DQT včera"` (or similar)
- Data loading: fetch DQT runs where the covered date range includes yesterday
  - Use `IDqtRunRepository.GetPaginatedAsync` filtered by date, or add a new `GetByDateAsync(DateOnly date)` method to `IDqtRunRepository` if date filtering is not already supported
  - If multiple runs exist for yesterday, surface the most recently completed one
- Returned payload shape (mirrors existing tile):
  ```json
  {
    "status": "success" | "warning" | "error" | "no_data",
    "data": {
      "runId": "...",
      "runStatus": "Completed" | "Failed" | "Running",
      "dateFrom": "YYYY-MM-DD",
      "dateTo": "YYYY-MM-DD",
      "totalChecked": 123,
      "totalMismatches": 4
    },
    "drillDown": { "href": "/automation/data-quality", "enabled": true }
  }
  ```
- Register in `DashboardModule.AddDashboardModule()` via `services.RegisterTile<DqtYesterdayStatusTile>()`

### Frontend — new tile component

- New file: `frontend/src/components/dashboard/tiles/DqtYesterdayStatusTile.tsx`
- Mirrors `DataQualityTile.tsx` but with visual emphasis on "yesterday" in the subtitle
- Shows: icon (AlertTriangle / ShieldCheck), mismatch count, date label
- Clickable → navigates to `/automation/data-quality`
- Register in `TileContent.tsx` under case `'dqtyesterdaystatus'`

### No new API endpoints required

The tile data is loaded through the existing `GET /api/dashboard/data` pipeline.

## Acceptance Criteria

- [ ] New tile appears in the dashboard settings panel and can be enabled/disabled
- [ ] Tile shows `no_data` state when no DQT run exists for yesterday
- [ ] Tile shows mismatch count and run status for yesterday's completed run
- [ ] Tile shows error state when the DQT run for yesterday has `status = Failed`
- [ ] Clicking the tile navigates to `/automation/data-quality`
- [ ] Unit tests cover `DqtYesterdayStatusTile.LoadDataAsync` (no data, success, warning, error)
- [ ] Frontend Jest tests cover the new tile component render states

## Related

- Existing tile: `DataQualityStatusTile` / `dataqualitystatus`
- Repository: `IDqtRunRepository.GetPaginatedAsync` (may need date-range overload)
- Frontend: `DataQualityTile.tsx`, `TileContent.tsx`