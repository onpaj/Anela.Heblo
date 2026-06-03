# Specification: Correct DataQuality Feature Documentation Schedule Section

## Summary
Update `docs/features/data-quality-dqt.md` so its schedule and constraints sections match the actual `InvoiceDqtJob` implementation. The doc currently describes a weekly Monday 23:00 CEST run covering the previous 7 days, but the code runs daily at 05:00 covering only the previous calendar day. This is a documentation-only change — no code is modified.

## Background
A daily architecture review (`brief.md`, filed 2026-06-02) flagged a contradiction between the feature documentation and the implementation:

- **Doc** (`docs/features/data-quality-dqt.md` lines 5, 34, 55, 76) claims a **weekly** Hangfire job running **Mondays at 23:00 CEST** with a **7-day default window**.
- **Code** (`backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/InvoiceDqtJob.cs` lines 15–22, 44, 48) defines a **daily** job at `0 5 * * *` (05:00 server time) named `daily-invoice-dqt` / "Daily Invoice Data Quality Test", and the job body invokes `DqtRun.Start(..., yesterday, yesterday, ...)`, covering exactly one day (the previous calendar day).

The drift suggests the schedule was changed from weekly to daily without updating the documentation. Stale docs in this area are operationally hazardous: anyone using the doc to debug missing results, reason about coverage windows, or schedule manual re-runs will reach wrong conclusions.

## Functional Requirements

### FR-1: Correct the "Schedule" section
Replace the contents of the **Schedule** section in `docs/features/data-quality-dqt.md` (lines 32–35) so it accurately describes the implemented behavior.

**Acceptance criteria:**
- Section states the job runs **daily at 05:00** (server time — see Open Questions on timezone phrasing).
- Section identifies the recurring job by its actual `JobName` (`daily-invoice-dqt`) and class (`InvoiceDqtJob`).
- Section states each run covers the **previous calendar day** (a single day).
- Manual trigger bullet (`POST /api/data-quality/runs`) is preserved.
- No mention of "weekly", "Monday", or "23:00 CEST" remains in this section.

### FR-2: Correct the "Overview" section
The Overview paragraph (line 5) currently ends with *"…runs automatically on a weekly Hangfire schedule."* Update it to reflect the daily schedule.

**Acceptance criteria:**
- The Overview paragraph no longer uses the word "weekly" for the Hangfire schedule.
- The wording stays consistent with the updated Schedule section (daily).

### FR-3: Correct the "Architecture" section bullet for `InvoiceDqtJob`
Line 55 currently reads: *"`InvoiceDqtJob` — Hangfire `IRecurringJob` that schedules weekly runs"*. Update so it does not say "weekly".

**Acceptance criteria:**
- Bullet describes `InvoiceDqtJob` as the Hangfire `IRecurringJob` that schedules **daily** runs (or equivalent wording matching FR-1).

### FR-4: Correct the "Known constraints" bullet about the default window
The constraint bullet (line 76) currently reads: *"The weekly job compares the previous 7 days by default."* This is wrong on both the cadence and the window size.

**Acceptance criteria:**
- The bullet either (a) is replaced with one stating the scheduled job compares only the previous calendar day, or (b) is removed if FR-1 already conveys the same information, to avoid duplication.
- No remaining "previous 7 days" wording in the document.

### FR-5: Sweep for any other "weekly" / "Monday" / "7 days" references
Ensure no other location in `docs/features/data-quality-dqt.md` still implies a weekly cadence or a multi-day default window.

**Acceptance criteria:**
- `grep -i -n "weekly\|monday\|23:00\|7 days\|seven days"` over the file returns no matches that describe the scheduled job's cadence or window.
- Any remaining matches (e.g. in unrelated explanatory text) are deliberate and accurate.

## Non-Functional Requirements

### NFR-1: Accuracy
Every factual claim in the updated sections must be traceable to either `InvoiceDqtJob.cs` (cadence, window, job name, description) or to the API surface that's already documented elsewhere in the same file. No new claims that are not backed by code.

### NFR-2: Style consistency
- Match the existing Markdown structure (heading levels, bullet style, code-fence usage) used elsewhere in the file.
- Keep the section terse — this is a reference doc, not a tutorial.
- Use the same tone as the surrounding sections; do not introduce changelog-style remarks (e.g. "previously this was weekly") in the doc itself.

### NFR-3: Surgical change
Per project coding behavior rules, touch only the lines required by FR-1 through FR-5. Do not rewrite or restyle unrelated sections, even if they could be improved.

## Data Model
Not applicable — documentation-only change.

## API / Interface Design
Not applicable — no API changes. The existing `POST /api/data-quality/runs` manual trigger is referenced by the doc but not modified.

## Dependencies
- Read-only reference: `backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/InvoiceDqtJob.cs` — source of truth for cadence, window, job name, and description.
- File to edit: `docs/features/data-quality-dqt.md`.
- No code, build, test, or deployment dependencies.

## Out of Scope
- Any change to `InvoiceDqtJob.cs` or its cron expression. If the *intent* was actually weekly and the code is the bug, that is a separate decision and a separate change — this spec only aligns the doc with current code.
- Any change to other DataQuality docs, comparison logic, or schema.
- Backfilling historical run data or revisiting the trigger types (`Scheduled` vs `Manual`).
- Reviewing or updating the manual trigger payload shape.
- Updating any unrelated "Known constraints" bullets (e.g. the Flexi `Code` field bullet, the in-memory sizing bullet).
- Translation or localization of the doc.

## Open Questions
None.

## Status: COMPLETE