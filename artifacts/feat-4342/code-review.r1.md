## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `frontend/src/components/catalog/detail/tabs/MarginsTab/MarginsChart.utils.ts:44` — the eight parallel Maps/arrays could be collapsed into a loop over metric keys; left as-is since this is a behavior-preserving extraction.

Extraction is byte-equivalent to the removed inline function (now injectable, defaulting to new Date()). 7 new tests pass and cover January year-boundary, mid-year split, current-month exclusion, and zero fallback.
