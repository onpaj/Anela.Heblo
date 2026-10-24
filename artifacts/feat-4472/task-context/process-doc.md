### task: process-doc
**Files:** Modify `docs/processes/module-dashboard.md`

- [ ] Add a note (Known quirks / behavior section) that enable/disable tile endpoints return 400 with a `BaseResponse` error body when `TileId` is empty and 200 with the response otherwise; bump `verified_at`.
- [ ] Run `python3 scripts/process-docs/check.py index` and fix any reported issue (the doc owns `DashboardController.cs`, so CI requires this).
