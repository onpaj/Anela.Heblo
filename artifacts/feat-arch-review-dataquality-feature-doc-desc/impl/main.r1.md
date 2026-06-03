All tasks complete. Here is the output summary:

---

# Implementation: Correct DataQuality Feature Documentation Schedule Section

## What was implemented

Four surgical edits to `docs/features/data-quality-dqt.md` to align the Schedule, Overview, Architecture, and Known constraints sections with the actual `InvoiceDqtJob` implementation. The document previously described a weekly Monday 23:00 CEST job covering 7 days; it now accurately reflects the daily 05:00 Europe/Prague job covering the previous calendar day.

## Files created/modified

- `docs/features/data-quality-dqt.md` — four edits: Overview word change, Schedule bullet rewrite, Architecture word change, stale Known constraints bullet deleted

## Tests

No tests — documentation-only change. Verification performed via grep sweeps confirming zero stale terms and three positive `daily` assertions.

## How to verify

```bash
cd /path/to/worktree
grep -in "weekly\|monday\|23:00\|7 days\|CEST\|CET\|UTC\|server time" docs/features/data-quality-dqt.md
# → zero matches

grep -n "daily" docs/features/data-quality-dqt.md
# → 3 matches: line 5 (Overview), line 34 (Schedule), line 55 (Architecture)

awk '/^## Known constraints/,/^## /{print}' docs/features/data-quality-dqt.md | grep -c '^- '
# → 2
```

## Notes

No deviations from spec. Timezone stated as `Europe/Prague` (traced to `RecurringJobMetadata.DefaultTimeZoneId`). Known constraints bullet deleted per arch-review Decision 2 (not rewritten). No code, build, test, or deployment changes.

## PR Summary

Aligns the DataQuality feature doc with the actual `InvoiceDqtJob` schedule. The doc described a weekly Monday 23:00 CEST job covering the previous 7 days; the code runs daily at 05:00 Europe/Prague (cron `0 5 * * *`) covering only the previous calendar day. Stale wording in four locations (Overview, Schedule, Architecture, Known constraints) is corrected with minimal, surgical edits.

### Changes
- `docs/features/data-quality-dqt.md` — Overview: `weekly` → `daily`; Schedule bullet: rewritten with correct cadence, timezone, job name, and window; Architecture `InvoiceDqtJob` bullet: `weekly` → `daily`; Known constraints: stale "previous 7 days" bullet removed

## Status

DONE