# Architecture Review: Correct DataQuality Feature Documentation Schedule Section

## Skip Design: true

No UI/UX work — Markdown copy edits to an existing feature doc. No new components, screens, or visual decisions.

## Architectural Fit Assessment

The change aligns cleanly with the project's documentation conventions:

- `docs/features/data-quality-dqt.md` is the canonical per-feature reference for the DataQuality module (matches the `docs/features/` convention recorded in `CLAUDE.md`).
- The doc is reference-style (sections for Overview, Schedule, API, Architecture, Known constraints) — not a tutorial, not a changelog. The fix preserves that shape.
- The source of truth for runtime behavior is `InvoiceDqtJob.cs` and the surrounding Hangfire infrastructure under `Anela.Heblo.API/Infrastructure/Hangfire/` + `Anela.Heblo.Domain/Features/BackgroundJobs/`. No other doc, ADR, or memory file restates the cadence, so this is the single point where the drift must be repaired.
- Integration points are zero (no code, no migration, no API surface). The only "consumer" of the change is human readers of the doc.

The only architectural choice that matters here is **what to assert about timezone**, because both the brief ("05:00 UTC") and the spec ("server time") are imprecise vs. what the code actually does.

## Proposed Architecture

### Component Overview

```
docs/features/data-quality-dqt.md   ← single file edited
            ▲
            │ (factual claims must trace to)
            │
backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/
   InvoiceDqtJob.cs                 ← source of truth for cadence, window, names
backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobMetadata.cs
                                    ← source of truth for default TimeZoneId
```

No new components. No relationships change.

### Key Design Decisions

#### Decision 1: Timezone wording in the Schedule section

**Options considered:**
1. "daily at 05:00 UTC" — what the brief proposed.
2. "daily at 05:00 server time" — what the spec hedged to.
3. "daily at 05:00 Europe/Prague" — what the code actually does.

**Chosen approach:** Option 3. State **"daily at 05:00 Europe/Prague"**.

**Rationale:** `InvoiceDqtJob.Metadata` does not override `TimeZoneId`, so it inherits `RecurringJobMetadata.DefaultTimeZoneId = "Europe/Prague"` (`backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobMetadata.cs:36`). The Hangfire registration pipeline (`HangfireJobRegistrationHelper.cs:83`) passes that as a `TimeZoneInfo` to `RecurringJob.AddOrUpdate`, so the cron `0 5 * * *` fires at 05:00 Prague local time. "UTC" is wrong (would actually fire at 07:00 Prague in winter). "Server time" is ambiguous — the doc is for humans reasoning about operational windows and should name the timezone explicitly. "Europe/Prague" is unambiguous, matches the IANA ID used in code, and stays correct across DST.

#### Decision 2: Where the daily-window fact lives

**Options considered:**
1. State the window only in the Schedule section and drop the "Known constraints" bullet entirely (FR-4 option b).
2. Keep a constraint bullet that re-states the window.

**Chosen approach:** Option 1 — drop the bullet (FR-4 option b).

**Rationale:** The current "Known constraints" bullet about the comparison window is not a constraint — it is the scheduled job's normal operating envelope, which belongs in Schedule. The other two bullets in that section ("Flexi `Code` field…", "Large date ranges hold both sets in memory…") are genuine constraints that surprise readers. Re-stating the daily window there duplicates the Schedule section and re-creates the same drift risk the bug exposed. One fact, one home.

#### Decision 3: Treat the doc as describing the scheduled job, not the comparer

**Options considered:**
1. Edit Schedule/Overview/Architecture to describe the scheduled job's behavior (daily, one day).
2. Edit to describe the comparer's *capability* (arbitrary date range) and separately note what the schedule pins it to.

**Chosen approach:** Option 1, with a one-line acknowledgement that the manual trigger can use any range (already implied by `POST /api/data-quality/runs`, no rewording needed beyond what FR-1 already preserves).

**Rationale:** The bug was about the *scheduled* behavior. Expanding scope to re-document the comparer's range flexibility violates the "surgical change" rule (NFR-3) and the project's "touch only what the task requires" guidance. The spec already captured this correctly.

## Implementation Guidance

### Directory / Module Structure

Single file edited:

- `docs/features/data-quality-dqt.md`

No new files. No code, tests, migrations, or build configuration touched. Do not create a changelog, ADR, or memory entry for this fix — it is doc-debt repayment, not a decision.

### Interfaces and Contracts

No code interfaces change. The doc-to-code contract that must hold after the edit:

| Doc claim | Authoritative source |
|-----------|---------------------|
| Cadence: daily at 05:00 Europe/Prague | `InvoiceDqtJob.Metadata.CronExpression` + `RecurringJobMetadata.DefaultTimeZoneId` |
| Job identity: `daily-invoice-dqt` / `InvoiceDqtJob` | `InvoiceDqtJob.Metadata.JobName`, class name |
| Window: previous calendar day (single day) | `InvoiceDqtJob.ExecuteAsync` lines 44, 48 |
| Manual trigger: `POST /api/data-quality/runs` | `DataQualityController` (already in the doc's API table) |

Every other factual claim in the file is out of scope.

### Data Flow

Unchanged. No data, no requests, no jobs are altered.

For the reader-flow the doc serves (what FR-1…FR-5 actually fix):

```
Reader debugging "where are today's DQT results?"
    │
    ▼
Reads Schedule section
    │
    ├── Before fix: looks for results on Monday → wrong day, wrong window → false alarm
    │
    └── After fix: knows job ran ~05:00 Prague, covers yesterday only → checks correct run
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Future schedule change re-introduces the same drift (cron edited without doc edit) | MEDIUM | Out of scope to enforce here; flag to user as a follow-up candidate (e.g. a test or PR-check that asserts doc claims against `InvoiceDqtJob.Metadata`). Do **not** add such a test as part of this change. |
| Writing "CEST" or "UTC" instead of "Europe/Prague" reintroduces a subtler inaccuracy | HIGH | Decision 1 above pins the wording. Reviewer should grep the diff for `CEST`, `UTC`, `CET`, `server time` and reject any of them. |
| Scope creep: editing the comparer narrative, the in-memory constraint, or the Flexi `Code` bullet | MEDIUM | NFR-3 ("surgical change") plus FR-5's grep check. Diff should be limited to the five edit points enumerated in FR-1…FR-4. |
| Reader assumes "previous calendar day" means Europe/Prague calendar day | LOW | True (the job uses `DateTime.Today` which is server-local, and the container TZ is Europe/Prague). No extra wording needed; the timezone in the Schedule line carries enough context. |

## Specification Amendments

1. **FR-1 / FR-2 timezone wording.** Replace the spec's phrasing "*05:00 (server time — see Open Questions on timezone phrasing)*" with **"05:00 Europe/Prague"**, and remove the now-stale "Open Questions" hedge. Justification: `InvoiceDqtJob` does not override `RecurringJobMetadata.TimeZoneId`, which defaults to `Europe/Prague` (`RecurringJobMetadata.cs:36`). The brief's suggested "05:00 UTC" is also wrong and should not be carried over.

2. **FR-4 resolution.** Adopt option (b) — **delete** the "*The weekly job compares the previous 7 days by default.*" bullet rather than rewriting it. The Schedule section already carries the single-day fact after FR-1; a constraint bullet duplicating it is the same anti-pattern that produced the original drift. The spec leaves this as an "either/or"; pin it.

3. **FR-5 grep list.** Extend the check tokens to include `CEST`, `CET`, `UTC` (in the schedule context only — the API table or unrelated prose may legitimately use UTC elsewhere if present). Any survivor of those terms describing the scheduled job's clock is a defect.

4. **NFR-1 traceability addition.** Add `RecurringJobMetadata.DefaultTimeZoneId` to the list of authoritative sources, alongside `InvoiceDqtJob.cs`. The timezone claim does not live in `InvoiceDqtJob` and the spec should not pretend otherwise.

5. **No new sections.** The spec is otherwise complete. Do not add an ADR, decision log, or memory entry — this is doc-debt repayment, not a design decision.

## Prerequisites

None. This change is fully self-contained:

- No migration, config, secret, or infrastructure change.
- No build, test, or deployment step required beyond the project's standard "doc-only PR" path.
- `dotnet build` / `dotnet format` / `npm run build` / `npm run lint` from `CLAUDE.md`'s validation gate are not applicable (no code touched); a Markdown render check (visual scan of the rendered file) is sufficient.
- No coordination with other in-flight work — the surrounding DataQuality commits on this branch's history (drill-down route unification, scheduled run persistence, `InvoiceDqtComparer` decoupling) are independent and do not affect cadence or window semantics.