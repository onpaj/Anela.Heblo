# Implementation: duplicate-exports-config

## What was implemented
Added `"rules": { "duplicates": "off" }` to `frontend/knip.json` (knip 5.88.1). The `Name|default` dual-export convention (and the three `useXMutation|useX` hook aliases) is now intentional and no longer reported.

## Files created/modified
- `frontend/knip.json` -- added `rules.duplicates = off`

## Tests
None (config only).

## How to verify
`cd frontend && npx knip` -- the "Duplicate exports" section no longer appears (was 15). No source files changed.

## Notes
No suitable section exists in docs/architecture/development_guidelines.md (no export/knip content), so no docs note added; rationale lives in the commit/PR.

## PR Summary
Configured knip to stop reporting intentional named+default dual exports as duplicates (FR-5 of #3931).

### Changes
- `frontend/knip.json` -- `rules.duplicates: off`

## Status
DONE
