### task: process-docs-update

**Files:**
- Modify: `docs/processes/sync-plaud-recordings.md`

Depends on: `polling-job-tolerance-and-tracker`.

- [ ] Add `MeetingTasks:UpstreamFailureAlertThreshold` (default 12, <=0 disables) to the Configuration table.
- [ ] In Known quirks, correct the "Failure counters undercount" bullet (a failure of `plaud recent` due to Plaud upstream 5xx no longer fails the run) and add a bullet: upstream `API error: 5xx` -> `PlaudUpstreamUnavailableException`, Warning log, run Succeeded, retried next tick; Error log once per threshold multiple of consecutive failing runs; counter is in-memory and resets on restart; auth and other CLI errors still fail the run.
- [ ] Add a Runtime facts line: 2026-08-29/30 Plaud API returned HTTP 500 to `plaud recent` for ~31 hours (issue #4002).
- [ ] Bump `verified_at` to the current HEAD short SHA (`git rev-parse --short=9 HEAD`) per the doc's convention.
- [ ] Run `python3 scripts/process-docs/check.py index`; expect pass.
- [ ] Commit: `docs(processes): document Plaud upstream 5xx handling`.
