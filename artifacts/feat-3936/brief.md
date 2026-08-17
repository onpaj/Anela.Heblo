## Module / File
`backend/src/Anela.Heblo.Application/Features/MeetingTasks/Infrastructure/Jobs/PlaudPollingJob.cs`

## Coverage
Line coverage: 33.9% (filter threshold: 60%)

## What's not tested
1. **Enabled-check skip** — when the job is disabled via `IRecurringJobStatusChecker`, the method returns early. No test verifies this gate.
2. **Skipped vs NotGenerated branching** — the handler response distinguishes two skip reasons: `response.Skipped && response.NotGenerated` (transcript not yet generated) vs `response.Skipped && !response.NotGenerated` (already known). The two counters are incremented separately, but neither is tested.
3. **Per-item exception swallowing** — exceptions thrown by `IngestPlaudRecordingRequest` processing are caught and logged but do not abort the loop. No test verifies that a failure on one recording leaves the rest processed, and no test checks that the failed recording is truly skipped without corrupting global state.

## Why it matters
The job polls every 5 minutes. If the `notGenerated` counter branch is broken, the log summary misreports how many recordings were skipped due to missing transcripts vs duplicates, making operational diagnosis impossible. If the exception swallow is removed, one bad recording will abort the entire polling batch and silently drop all subsequent recordings.

## Suggested approach
Unit test with a mocked `IPlaudClient` and mediator:
- Case: job disabled → `IPlaudClient.ListRecentAsync` never called
- Case: one recording → response.Skipped=true, NotGenerated=true → `notGenerated` counter incremented
- Case: one recording → response.Skipped=true, NotGenerated=false → `skipped` counter incremented
- Case: mediator throws → exception swallowed, loop continues to next recording
~1.5 h effort.

---
_Filed by weekly coverage-gap routine on 2026-08-17. Based on CI run #31804633307 (6f781d410eb84616c8decb088d6d18cd1de01fb8)._
