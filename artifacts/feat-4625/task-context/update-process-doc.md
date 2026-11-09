### task: update-process-doc
**Files:** `docs/processes/sync-photobank-index.md`

- [ ] Flow step 2: add that a 410 Gone on a stored `DeltaLink` clears it and retries once as a full enumeration.
- [ ] Replace the "expired or invalid DeltaLink blocks the root" quirk: now auto-recovers (once per run); note the full walk reports no deletions so photos deleted while the token was stale stay indexed; note retry failure is logged as `Failed to index root`.
- [ ] Add to Runtime facts/history: Graph 410 on stored token observed in telemetry 2026-11-03..07 (issue #4625).
- [ ] Bump `verified_at` and run `python3 scripts/process-docs/check.py index`.
