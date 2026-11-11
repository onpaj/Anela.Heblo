# Implementation: process-doc

## What was implemented
Added a Known quirks bullet to `docs/processes/module-dashboard.md` documenting that the enable/disable tile endpoints return 400 with a `BaseResponse` error body when `TileId` is empty and 200 with the handler response otherwise. Bumped `verified_at`. Ran `python3 scripts/process-docs/check.py index` (exit 0, INDEX.md unchanged).

## Files created/modified
- `docs/processes/module-dashboard.md` - new quirk bullet, verified_at bump

## Tests
None (docs only).

## How to verify
`python3 scripts/process-docs/check.py index`

## Notes
Behaviour confirmed against `EnableTileHandler` (empty TileId check) and `BaseApiController.HandleResponse`.

## PR Summary
Documents the enable/disable tile endpoint status codes in the Dashboard module process doc.

### Changes
- `docs/processes/module-dashboard.md` - Known quirks bullet; verified_at bumped

## Status
DONE
