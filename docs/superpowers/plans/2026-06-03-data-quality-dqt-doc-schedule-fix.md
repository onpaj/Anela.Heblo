# DataQuality Doc Schedule Fix — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Align the Schedule, Overview, Architecture and Known-constraints wording in `docs/features/data-quality-dqt.md` with the actual `InvoiceDqtJob` implementation (daily 05:00 Europe/Prague, single-day window).

**Architecture:** Documentation-only, surgical edit to one Markdown file. No code, build, test, deployment, migration, secret, or config changes. Every factual claim traces to `InvoiceDqtJob.cs` (cadence, window, job name/description) and `RecurringJobMetadata.cs` (default timezone).

**Tech Stack:** Markdown. No tools beyond a text editor, `grep`, and `git`.

---

## Context

### Source of truth (read-only — do not modify)

- `backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/InvoiceDqtJob.cs`
  - Line 17 — `JobName = "daily-invoice-dqt"`
  - Line 18 — `DisplayName = "Daily Invoice Data Quality Test"`
  - Line 19 — `Description = "Compares issued invoices between Shoptet and ABRA Flexi for the previous day"`
  - Line 20 — `CronExpression = "0 5 * * *"` (daily at 05:00)
  - Line 44 — `var yesterday = DateOnly.FromDateTime(DateTime.Today.AddDays(-1));`
  - Line 48 — `DqtRun.Start(DqtTestType.IssuedInvoiceComparison, yesterday, yesterday, DqtTriggerType.Scheduled);` — window is `[yesterday, yesterday]`, i.e. one calendar day.
  - `Metadata` does **not** set `TimeZoneId`, so it inherits the default.
- `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobMetadata.cs`
  - Line 36 — `public const string DefaultTimeZoneId = "Europe/Prague";`
  - Line 41 — `public string TimeZoneId { get; init; } = DefaultTimeZoneId;`

### File to edit

- `docs/features/data-quality-dqt.md` — five edit points, enumerated below.

### Decisions locked by the architecture review

| Decision | Pinned value | Source |
|----------|--------------|--------|
| Timezone wording | `Europe/Prague` (not "UTC", not "server time", not "CET/CEST") | arch-review Decision 1 |
| Daily-window fact location | Schedule section only; **delete** the duplicate "Known constraints" bullet | arch-review Decision 2 + FR-4 option (b) |
| Scope | The doc describes the *scheduled* job's behavior; do not re-document the comparer's arbitrary-range capability | arch-review Decision 3 |

### Out of scope (do not touch)

- The Mismatch types table, the API table, the Architecture bullets other than the single `InvoiceDqtJob` line, the Flexi `Code` bullet, and the in-memory sizing bullet.
- `InvoiceDqtJob.cs` cron or window — this plan does **not** change behavior.
- Any new ADR, changelog, memory entry, or test asserting doc-vs-code.

---

## File Structure

Only one file changes. No new files. No deletions.

| Path | Action | Lines touched (pre-edit) | Responsibility after edit |
|------|--------|--------------------------|---------------------------|
| `docs/features/data-quality-dqt.md` | Modify | 5, 32–35, 55, 76 | Accurately describes the daily 05:00 Europe/Prague job covering yesterday |

---

## Task 1: Fix the Overview paragraph (FR-2)

**Files:**
- Modify: `docs/features/data-quality-dqt.md:5`

Removes the "weekly Hangfire schedule" phrase so the Overview matches what the scheduled job actually does. Wording stays terse and consistent with the Schedule section produced in Task 2.

- [ ] **Step 1: Apply the edit**

Replace the exact string:

```
The DQT feature runs automated data quality checks that compare issued invoices between Shoptet and Abra Flexi for a defined time period. It surfaces mismatches on a dashboard tile and a dedicated `/data-quality` page, and runs automatically on a weekly Hangfire schedule.
```

with:

```
The DQT feature runs automated data quality checks that compare issued invoices between Shoptet and Abra Flexi for a defined time period. It surfaces mismatches on a dashboard tile and a dedicated `/data-quality` page, and runs automatically on a daily Hangfire schedule.
```

(Single word change: `weekly` → `daily`. Nothing else on this line changes.)

- [ ] **Step 2: Verify the edit**

Run:

```bash
grep -n "weekly Hangfire schedule\|daily Hangfire schedule" docs/features/data-quality-dqt.md
```

Expected:

- Exactly **one** match, containing `daily Hangfire schedule`, on the Overview line.
- **No** match containing `weekly Hangfire schedule`.

- [ ] **Step 3: Stage the change (do not commit yet — Task 6 commits the full diff)**

```bash
git add docs/features/data-quality-dqt.md
```

---

## Task 2: Rewrite the Schedule section (FR-1)

**Files:**
- Modify: `docs/features/data-quality-dqt.md:32-35`

Replaces the incorrect "every Monday at 23:00 CEST" bullet with the actual cadence (daily 05:00 Europe/Prague), the actual window (previous calendar day), and the actual job identity (`daily-invoice-dqt` / `InvoiceDqtJob`). Manual-trigger bullet is preserved verbatim.

- [ ] **Step 1: Apply the edit**

Replace the exact block:

```
## Schedule

- **Automatic**: every Monday at 23:00 CEST via Hangfire recurring job (`InvoiceDqtJob`)
- **Manual trigger**: `POST /api/data-quality/runs`
```

with:

```
## Schedule

- **Automatic**: daily at 05:00 Europe/Prague via Hangfire recurring job `daily-invoice-dqt` (`InvoiceDqtJob`). Each run covers the previous calendar day.
- **Manual trigger**: `POST /api/data-quality/runs`
```

Notes:

- The job name `daily-invoice-dqt` comes from `InvoiceDqtJob.Metadata.JobName` (line 17 of the source file).
- The timezone is `Europe/Prague` because `InvoiceDqtJob.Metadata` does not set `TimeZoneId`, so it inherits `RecurringJobMetadata.DefaultTimeZoneId`.
- Do not add a parenthetical like "(formerly weekly)" or any changelog-style remark — NFR-2 forbids changelog tone in the doc.

- [ ] **Step 2: Verify the edit**

Run:

```bash
grep -in "Monday\|23:00\|CEST\|CET\|UTC\|server time\|weekly" docs/features/data-quality-dqt.md
```

Expected: **zero** matches anywhere in the file.

Then run:

```bash
grep -n "daily at 05:00 Europe/Prague" docs/features/data-quality-dqt.md
grep -n "daily-invoice-dqt" docs/features/data-quality-dqt.md
grep -n "previous calendar day" docs/features/data-quality-dqt.md
```

Expected: each of the three greps returns exactly one match, all on the Schedule line.

- [ ] **Step 3: Stage the change**

```bash
git add docs/features/data-quality-dqt.md
```

---

## Task 3: Fix the Architecture bullet for `InvoiceDqtJob` (FR-3)

**Files:**
- Modify: `docs/features/data-quality-dqt.md:55`

Updates the single-line description of `InvoiceDqtJob` in the Architecture section so it no longer says "weekly". Other Architecture bullets are out of scope.

- [ ] **Step 1: Apply the edit**

Replace the exact line:

```
- `InvoiceDqtJob` — Hangfire `IRecurringJob` that schedules weekly runs
```

with:

```
- `InvoiceDqtJob` — Hangfire `IRecurringJob` that schedules daily runs
```

(Single word change: `weekly` → `daily`. Indentation, bullet style, and backticks unchanged.)

- [ ] **Step 2: Verify the edit**

Run:

```bash
grep -n "InvoiceDqtJob.*IRecurringJob" docs/features/data-quality-dqt.md
```

Expected: exactly one match, containing `daily runs`, not `weekly runs`.

- [ ] **Step 3: Stage the change**

```bash
git add docs/features/data-quality-dqt.md
```

---

## Task 4: Delete the stale "Known constraints" bullet (FR-4)

**Files:**
- Modify: `docs/features/data-quality-dqt.md:76`

Per the architecture review (Decision 2 + Specification Amendment 2), the "weekly job compares the previous 7 days" bullet is replaced by **removal**, not rewriting. The single-day fact lives in the Schedule section (Task 2); restating it here re-creates the duplication that caused the original drift. The bullet is also factually wrong on both cadence and window.

- [ ] **Step 1: Apply the edit**

Delete the exact line (including the leading `- ` and trailing newline):

```
- The weekly job compares the previous 7 days by default.
```

After the edit, the "Known constraints" section must contain exactly two bullets (in this order):

```
- Flexi item `Code` field is not reliably preserved on read-back; `PriceList` (`code:PRODUCT-CODE`) is used as the authoritative product identifier instead.
- Large date ranges (thousands of invoices) hold both full sets in memory simultaneously.
```

Do not reword either surviving bullet.

- [ ] **Step 2: Verify the edit**

Run:

```bash
grep -in "7 days\|seven days\|previous 7\|weekly job" docs/features/data-quality-dqt.md
```

Expected: **zero** matches.

Then run:

```bash
awk '/^## Known constraints/,/^## /{print}' docs/features/data-quality-dqt.md | grep -c '^- '
```

Expected output: `2` (exactly two bullets remain in the Known constraints section).

- [ ] **Step 3: Stage the change**

```bash
git add docs/features/data-quality-dqt.md
```

---

## Task 5: Full-file sweep for stale schedule/cadence terms (FR-5)

**Files:**
- Read-only audit: `docs/features/data-quality-dqt.md`

Architecture review extended FR-5's grep token list to also catch `CEST`, `CET`, `UTC`, and `server time`. This task is the final gate before commit: it confirms no residual phrasing still describes the scheduled job as anything other than daily 05:00 Europe/Prague covering yesterday.

- [ ] **Step 1: Run the cadence-term sweep**

Run:

```bash
grep -in "weekly\|Monday\|23:00\|7 days\|seven days\|CEST\|CET\|UTC\|server time" docs/features/data-quality-dqt.md
```

Expected: **zero** matches.

If any line matches, inspect it:

- If it describes the **scheduled job's** clock, cadence, or window — it is a defect. Fix it (return to the relevant Task 1–4) before continuing.
- If it appears in unrelated explanatory text (none is expected in this file, but check) and is factually accurate — leave it. Note in the commit body why it survived.

- [ ] **Step 2: Confirm the positive assertions are still present**

Run:

```bash
grep -n "daily" docs/features/data-quality-dqt.md
```

Expected: at least three matches, covering:

1. The Overview paragraph (`daily Hangfire schedule`).
2. The Schedule bullet (`daily at 05:00 Europe/Prague`).
3. The Architecture bullet (`daily runs`).

If any of the three is missing, re-run the relevant Task 1–3 step.

- [ ] **Step 3: Visual render check**

Open `docs/features/data-quality-dqt.md` in your editor's Markdown preview (or run any local Markdown renderer) and confirm:

- Headings render at the same levels as before.
- The Schedule section has exactly two bullets.
- The Known constraints section has exactly two bullets.
- No stray blank lines were introduced between bullets in the edited sections.

No CLI command is asserted here because there is no project-standard Markdown renderer; this is a human-eyes scan per the architecture review's "Markdown render check" prerequisite.

---

## Task 6: Commit and push

**Files:**
- Commit: `docs/features/data-quality-dqt.md` (already staged across Tasks 1–4)

Single commit covering all five edit points. The project's standard validation gate (`dotnet build`, `dotnet format`, `npm run build`, `npm run lint`) does **not** apply — no code changed. The architecture review explicitly notes that a Markdown render check (done in Task 5 Step 3) is sufficient.

- [ ] **Step 1: Confirm the staged diff matches expectations**

Run:

```bash
git diff --cached docs/features/data-quality-dqt.md
```

Expected: a diff that contains exactly these changes and nothing else:

1. Line 5 — `weekly Hangfire schedule` → `daily Hangfire schedule`.
2. Lines 32–35 — Schedule section bullet rewritten (timezone, job name, window).
3. Line 55 — `schedules weekly runs` → `schedules daily runs`.
4. Line 76 — `- The weekly job compares the previous 7 days by default.` removed.

If the diff contains any other change (reflow, whitespace-only edits to unrelated lines, restyling), revert it before committing — NFR-3 mandates a surgical change.

- [ ] **Step 2: Confirm no other files are staged**

Run:

```bash
git status --short
```

Expected: a single line, `M  docs/features/data-quality-dqt.md`. If any other file appears as staged or modified, stop and investigate before committing.

- [ ] **Step 3: Commit**

Run:

```bash
git commit -m "docs(data-quality): align schedule section with daily InvoiceDqtJob

The Overview, Schedule, Architecture, and Known constraints sections of
docs/features/data-quality-dqt.md described a weekly Monday 23:00 CEST job
covering the previous 7 days. The implementation runs daily at 05:00
Europe/Prague (cron 0 5 * * *) and compares the previous calendar day
only. Update the doc to match the code; no behavior changes."
```

- [ ] **Step 4: Push the branch**

Run:

```bash
git push
```

Expected: push succeeds against the existing upstream branch (`feat-arch-review-dataquality-feature-doc-desc`). If the branch has no upstream, use `git push -u origin HEAD` instead.

---

## Self-Review

**Spec coverage:**

| Spec requirement | Task |
|------------------|------|
| FR-1 Schedule section rewrite | Task 2 |
| FR-2 Overview paragraph | Task 1 |
| FR-3 Architecture bullet | Task 3 |
| FR-4 Known constraints bullet (deletion path, per arch-review Amendment 2) | Task 4 |
| FR-5 Full-file sweep (extended grep tokens per arch-review Amendment 3) | Task 5 |
| NFR-1 Accuracy / traceability | Context block + arch-review Decision 1 (timezone source pinned to `RecurringJobMetadata.DefaultTimeZoneId`, per arch-review Amendment 4) |
| NFR-2 Style consistency | Task 2 Step 1 note ("no changelog-style remarks"); each edit preserves existing bullet style |
| NFR-3 Surgical change | Task 6 Step 1 ("revert any other diff") and Task 6 Step 2 (status must show only the one file) |

**Placeholder scan:** none — every step has an exact string to find, an exact replacement, and a verification command with expected output.

**Type / identifier consistency:** the job name (`daily-invoice-dqt`), class name (`InvoiceDqtJob`), timezone (`Europe/Prague`), cron (`0 5 * * *`), and window phrase (`previous calendar day`) are used identically across Tasks 2, 5, and 6.
