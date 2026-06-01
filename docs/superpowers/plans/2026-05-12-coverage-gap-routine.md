# Coverage Gap Routine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Persist backend Cobertura and frontend LCOV coverage as 7-day GitHub Actions artifacts on every main-branch CI run, then run a weekly remote Claude Code routine that reads those artifacts, qualitatively inspects low-coverage files, and files high-signal `coverage-gap` GitHub issues for untested business logic.

**Architecture:** Three discrete changes — (1) two new `actions/upload-artifact@v4` steps in `.github/workflows/ci-main-branch.yml` placed directly after the existing CodeCov uploads, (2) a new `docs/routines/weekly-coverage-gap.md` containing the routine prompt as the single source of truth (per NFR-5 / arch-review amendment 7), and (3) a one-time out-of-tree cron registration via the `schedule` skill. The routine has only `Bash`, `Read`, `Glob`, `Grep` access and writes only to GitHub Issues.

**Tech Stack:** GitHub Actions (workflow YAML, `actions/upload-artifact@v4`), GitHub CLI (`gh`), Claude Code remote routine scheduler (`CronCreate` / `CronList` / `CronDelete`), Markdown documentation.

---

## Scope Check

This is a single coherent change: enable a weekly coverage-gap routine end-to-end. It is not multi-subsystem and does not need splitting.

## File Structure

| File | Status | Responsibility |
|---|---|---|
| `.github/workflows/ci-main-branch.yml` | Modified | Add two new `upload-artifact` steps + a code comment guarding the existing `sed` line that the routine depends on. |
| `docs/routines/weekly-coverage-gap.md` | Created | Single source of truth: purpose, schedule, allowed tools, full routine prompt (verbatim, embedded as a fenced block), qualification rubric, issue template, operating instructions. |
| Cron entry in Claude Code scheduler | Created out-of-tree | Weekly schedule registered manually via `CronCreate` after the PR merges and CI has produced its first set of artifacts. |

There are no application code changes, no tests in the .NET solution, and no React changes. The validation surface is YAML syntax, documentation completeness, and post-merge artifact download verification.

## Conventions and Constants

The following values are fixed by the spec and must match exactly across the workflow YAML, the routine prompt, and the doc:

| Name | Value | Used in |
|---|---|---|
| Backend artifact name | `coverage-backend` | Workflow step + routine `gh run download` |
| Frontend artifact name | `coverage-frontend` | Workflow step + routine `gh run download` |
| Backend artifact glob | `./coverage/**/coverage.cobertura.xml` | Workflow `path:` (tighter than spec FR-1 default per arch-review amendment 6) |
| Frontend artifact path | `./frontend/coverage/lcov.info` | Workflow `path:` |
| Retention | `7` days | Both workflow steps |
| Cron (UTC) | `0 4 * * 1` | Cron entry, doc, prompt |
| Coverage threshold | `60` (percent) | Prompt, doc |
| Per-run filing cap | `10` | Prompt, doc (arch-review amendment 5) |
| Model | `claude-sonnet-4-6` | Cron entry, doc |
| Allowed tools | `Bash`, `Read`, `Glob`, `Grep` | Cron entry, doc |
| Repo | `onpaj/Anela.Heblo` | All `gh` calls in prompt |
| Issue label (new) | `coverage-gap`, color `#e99695` | Prompt, doc, issue template |
| Issue label (existing) | `tech-debt` | Prompt, doc, issue template |

---

## Task 1: Add the frontend coverage upload-artifact step

**Goal:** After the existing frontend CodeCov upload, persist `frontend/coverage/lcov.info` as a `coverage-frontend` artifact with 7-day retention (FR-2).

**Files:**
- Modify: `.github/workflows/ci-main-branch.yml` — insert a new step in the `frontend-tests` job after the existing `📊 Upload coverage reports` step that ends at line 71.

- [ ] **Step 1: Open the workflow file and locate the insertion point**

Open `.github/workflows/ci-main-branch.yml`. Find the `frontend-tests` job. The existing step is:

```yaml
      - name: 📊 Upload coverage reports
        uses: codecov/codecov-action@v3
        continue-on-error: true
        with:
          token: ${{ secrets.CODECOV_TOKEN }}
          files: ./frontend/coverage/lcov.info
          flags: frontend
          name: frontend-coverage
          fail_ci_if_error: false
          verbose: true
```

This block ends at line 71. The new step is inserted immediately after it (before the `📋 Frontend Test Report` step that begins at line 73).

- [ ] **Step 2: Insert the upload-artifact step**

Add the following YAML directly after the closing `verbose: true` of the existing step, indented to match the other steps:

```yaml
      - name: 📦 Persist frontend coverage artifact
        uses: actions/upload-artifact@v4
        if: success() || failure()
        with:
          name: coverage-frontend
          path: ./frontend/coverage/lcov.info
          retention-days: 7
```

Notes:
- `if: success() || failure()` matches the surrounding step behavior: upload the artifact whenever `npm test --coverage` produced a file, even if the CodeCov upload itself was flaky. It does NOT upload when the test step is skipped or cancelled (we don't want empty artifacts).
- The path is exact (single file), so no glob is needed.

- [ ] **Step 3: Validate the YAML syntax**

Run:

```bash
python3 -c "import yaml,sys; yaml.safe_load(open('.github/workflows/ci-main-branch.yml'))" && echo OK
```

Expected: `OK`.

If `python3` lacks PyYAML, use:

```bash
gh workflow view ci-main-branch.yml --repo onpaj/Anela.Heblo >/dev/null 2>&1 || true
yamllint .github/workflows/ci-main-branch.yml 2>/dev/null || true
```

A non-zero exit from `python3 -c` means the indentation drifted — fix it before continuing.

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/ci-main-branch.yml
git commit -m "ci: persist frontend coverage as coverage-frontend artifact

Adds an actions/upload-artifact@v4 step immediately after the existing
CodeCov upload in the frontend-tests job. The artifact (lcov.info) is
retained for 7 days and consumed by the weekly coverage-gap routine."
```

---

## Task 2: Add the backend coverage upload-artifact step

**Goal:** After the existing backend CodeCov upload, persist `coverage/**/coverage.cobertura.xml` as a `coverage-backend` artifact with 7-day retention (FR-1).

**Files:**
- Modify: `.github/workflows/ci-main-branch.yml` — insert a new step in the `backend-tests` job after the existing `📊 Upload coverage reports` step that ends at line 146.

- [ ] **Step 1: Open the workflow file and locate the insertion point**

Open `.github/workflows/ci-main-branch.yml`. Find the `backend-tests` job. The existing step is:

```yaml
      - name: 📊 Upload coverage reports
        uses: codecov/codecov-action@v3
        continue-on-error: true
        with:
          token: ${{ secrets.CODECOV_TOKEN }}
          files: ${{ steps.coverage-files.outputs.files }}
          flags: backend
          name: backend-coverage
          fail_ci_if_error: false
          verbose: true
```

This block ends at line 146. The new step is inserted immediately after it (before the `📋 Backend Test Report` step that begins at line 148).

- [ ] **Step 2: Insert the upload-artifact step**

Add the following YAML directly after the closing `verbose: true` of the existing step, indented to match the other steps:

```yaml
      - name: 📦 Persist backend coverage artifact
        uses: actions/upload-artifact@v4
        if: success() || failure()
        with:
          name: coverage-backend
          path: ./coverage/**/coverage.cobertura.xml
          retention-days: 7
```

Notes:
- The glob is tightened from spec FR-1's `*.cobertura.xml` to `coverage.cobertura.xml` (arch-review amendment 6) — this is the exact filename produced by `dotnet test --collect:"XPlat Code Coverage"` and avoids sweeping in unrelated `.cobertura.xml` siblings.
- The artifact preserves the `backend/src/`-prefixed filename attributes that the preceding `📊 Process coverage files for CodeCov` step (line 117–129) writes into the XML. The routine depends on this prefix.

- [ ] **Step 3: Add a guard comment to the sed step**

The routine's path parser will silently misclassify modules if the `sed` rewrite at lines 119–122 is ever removed. Add a comment line directly above the `find ./coverage -name "coverage.cobertura.xml" -type f | while read file; do` line inside the `📊 Process coverage files for CodeCov` step.

The existing block is:

```yaml
      - name: 📊 Process coverage files for CodeCov
        run: |
          # Find all coverage.cobertura.xml files and fix paths for CodeCov
          find ./coverage -name "coverage.cobertura.xml" -type f | while read file; do
            sed -i 's|filename="|filename="backend/src/|g' "$file"
          done
```

Change the existing single comment line to a two-line block:

```yaml
      - name: 📊 Process coverage files for CodeCov
        run: |
          # Find all coverage.cobertura.xml files and fix paths for CodeCov.
          # DO NOT REMOVE: the weekly coverage-gap routine relies on the
          # 'backend/src/' filename prefix added by the sed below.
          # See docs/routines/weekly-coverage-gap.md.
          find ./coverage -name "coverage.cobertura.xml" -type f | while read file; do
            sed -i 's|filename="|filename="backend/src/|g' "$file"
          done
```

- [ ] **Step 4: Validate the YAML syntax**

Run:

```bash
python3 -c "import yaml,sys; yaml.safe_load(open('.github/workflows/ci-main-branch.yml'))" && echo OK
```

Expected: `OK`.

- [ ] **Step 5: Commit**

```bash
git add .github/workflows/ci-main-branch.yml
git commit -m "ci: persist backend coverage as coverage-backend artifact

Adds an actions/upload-artifact@v4 step immediately after the existing
CodeCov upload in the backend-tests job. The artifact (coverage.cobertura.xml
files) is retained for 7 days and consumed by the weekly coverage-gap
routine. Adds a guard comment on the path-rewriting sed step that the
routine relies on."
```

---

## Task 3: Create the routine documentation with embedded prompt

**Goal:** Produce `docs/routines/weekly-coverage-gap.md` — the single source of truth for the routine (FR-10). The doc contains the full routine prompt as a fenced code block; the cron registration references this section directly so there is no drift between prompt and rubric (arch-review amendment 7).

**Files:**
- Create: `docs/routines/weekly-coverage-gap.md`

The `docs/routines/` directory does not yet exist; creating the file with this path also creates the directory.

- [ ] **Step 1: Create the file with the complete content below**

Create `docs/routines/weekly-coverage-gap.md` with exactly the following content:

````markdown
# Weekly Coverage Gap Routine

A remote Claude Code routine that runs every Monday at 06:00 Prague time, reads the latest main-branch CI coverage artifacts, qualitatively assesses low-coverage files, and files GitHub issues for **meaningful** untested logic only.

This document is the **source of truth** for the routine. The prompt is embedded below as a fenced code block; when the cron entry is created or updated, copy the prompt verbatim from this section. Edits to the rubric land here first, then the prompt is refreshed.

## Purpose

Raw line-coverage is a noisy signal — controllers delegating to `_mediator.Send()` and handlers with branching business rules both count equally. This routine filters out the noise by qualitatively reading every candidate file: it skips trivial / passthrough code and files only when uncovered lines represent meaningful business logic worth testing.

## Schedule

| Property | Value |
|---|---|
| Cron (UTC) | `0 4 * * 1` |
| Local equivalent | Monday 06:00 Europe/Prague (CEST in summer, 05:00 CET in winter — UTC fixed) |
| Cadence | Once weekly |
| Repository | `onpaj/Anela.Heblo` |
| Model | `claude-sonnet-4-6` |
| Allowed tools | `Bash`, `Read`, `Glob`, `Grep` |

`Write`, `Edit`, `WebFetch`, and any other write-capable tools are intentionally disallowed. The routine cannot push code, modify the workflow, or change repository settings — it can only read source, download artifacts, and create issues.

## Coupling and dependencies

The routine depends on these load-bearing details elsewhere in the repo. Changing any of them without updating this doc and the prompt will break the routine silently or noisily.

- **`.github/workflows/ci-main-branch.yml` → backend `sed` step (around line 119–123).** Rewrites Cobertura `filename=` attributes to be prefixed with `backend/src/`. The routine validates this prefix and refuses to parse if it is missing. A guard comment on the sed step references this doc.
- **`.github/workflows/ci-main-branch.yml` → upload-artifact steps `coverage-backend` and `coverage-frontend`.** Artifact names are load-bearing; the routine downloads by exact name.
- **Existing label `tech-debt`.** Applied to every filed issue. If renamed, every new issue loses the label until the prompt is updated.
- **Existing five backend project layouts using `Features/<Module>/...`** in `Anela.Heblo.Application`, `Anela.Heblo.Domain`, `Anela.Heblo.Persistence`, `Anela.Heblo.API`, and `backend/src/Adapters/Anela.Heblo.Adapters.*`. The routine groups all five layers by `<Module>` and treats them as one logical module.
- **Frontend layout `frontend/src/<group>/...`.** First-segment-after-`frontend/src/` defines the group label in summaries.

## Configuration knobs

Two tunable values live near the top of the prompt below. Adjust either by editing the prompt block and updating the cron entry.

- `COVERAGE_THRESHOLD = 60` — file-level line coverage percent; candidates below this are read and qualified.
- `FILE_CAP = 10` — maximum issues filed per run. If more candidates qualify, the surplus is listed in the summary as `truncated_due_to_cap`.

A third toggle, `DRY_RUN`, is read from the environment at run start. Setting `DRY_RUN=1` parses everything and prints would-be filings without invoking `gh issue create`. Use this for first-run smoke testing.

## Qualification rubric

This rubric is referenced by the prompt below. Edit here first, then mirror into the prompt block.

### Skip (no issue filed)

- MVC controllers whose action methods only delegate to `_mediator.Send()` or `_mediator.Publish()` with no other logic.
- DTO, request, and response classes containing only properties.
- Startup, configuration, and DI registration code: `Program.cs`, `Startup.cs`, files matching `*Module.cs`, files matching `*ServiceCollectionExtensions.cs`.
- Files containing only auto-properties, auto-mapped fields, or trivial passthrough methods.
- Generated code: files containing an `<auto-generated>` header, files under known OpenAPI generated-client paths.

### File (issue created)

- MediatR `IRequestHandler` / `INotificationHandler` implementations with **two or more** conditional branches covering validation or business rules.
- Domain logic with state transitions, status flows, or invariants.
- Business calculations: financial totals, margins, stock quantities, pricing, discounts.
- Error or exception paths whose failure shape (exception type, error code, response payload) is never asserted by an existing test.
- Cross-module service contracts where the integration surface is untested.

A one-line rationale is recorded for every candidate, even those skipped, and appears in the per-run summary.

## Issue template

Title: `[coverage-gap] <Module>/<File>: <specific untested logic description>`

Body:

```
## Module / File
<path>

## Coverage
Line coverage: X% (filter threshold: 60%)

## What's not tested
<Specific description — which branches, which conditions, which error paths>

## Why it matters
<What could silently break if this logic regresses>

## Suggested approach
<Concrete: what type of test (unit/integration), what scenario to cover, ~effort estimate>

---
_Filed by weekly coverage-gap routine on <YYYY-MM-DD>. Based on CI run #<run-id>._
```

Labels applied: `coverage-gap`, `tech-debt`.

The `coverage-gap` label is created idempotently on first run if missing (color `#e99695`, description "Filed by weekly coverage-gap routine"). The `tech-debt` label must already exist (verified at setup).

## Dedup rule

Before filing, search open `coverage-gap` issues for a `<Module>/<basename>` title token:

```
gh issue list --repo onpaj/Anela.Heblo --label coverage-gap --state open \
  --search "<Module>/<basename> in:title" --limit 20
```

If any match is found, skip the filing. The search uses `<Module>/<basename>` (not the bare basename) to avoid false positives on common filenames like `Index.tsx` or `Handler.cs`.

## Operating instructions

### View the registered cron

```bash
# From a session with schedule-skill access
CronList
```

### Pause (disable) the routine

```bash
# CronDelete removes the schedule entirely. Re-register with CronCreate to resume.
CronDelete <cron-id>
```

### Rerun on-demand

The scheduler's manual-trigger interface (where available) runs the routine immediately against the latest successful main run. To inspect what the routine would do without filing, set `DRY_RUN=1` before triggering.

### Update the prompt

1. Edit this document — rubric / template / config.
2. Copy the updated prompt block verbatim into a new `CronCreate` registration.
3. Delete the old cron entry with `CronDelete`.

### Verify CI artifacts manually

After a successful main-branch run:

```bash
gh run list --workflow=ci-main-branch.yml --branch=main --status=success --limit 1 \
  --json databaseId --repo onpaj/Anela.Heblo
gh run download <run-id> --name coverage-backend  --dir /tmp/cov-be --repo onpaj/Anela.Heblo
gh run download <run-id> --name coverage-frontend --dir /tmp/cov-fe --repo onpaj/Anela.Heblo
ls /tmp/cov-be /tmp/cov-fe
```

Expected: at least one `coverage.cobertura.xml` under `/tmp/cov-be/...` and a non-empty `lcov.info` at `/tmp/cov-fe/lcov.info`.

## Routine prompt (verbatim)

The block below is what gets registered in the cron entry. Copy it as-is — do not paraphrase.

```text
You are the weekly coverage-gap routine for onpaj/Anela.Heblo.

CONFIGURATION (load-bearing constants — must match docs/routines/weekly-coverage-gap.md)
- COVERAGE_THRESHOLD = 60   (line coverage percent; files below are inspected)
- FILE_CAP            = 10  (max issues filed per run)
- DRY_RUN             = ${DRY_RUN:-0}  (when 1, print would-be filings instead of creating)

GOAL
Read the latest main-branch CI coverage artifacts (coverage-backend Cobertura XML
and coverage-frontend LCOV), select files with file-level line coverage below
COVERAGE_THRESHOLD, qualitatively read each candidate's source, and file at most
FILE_CAP GitHub issues for files whose uncovered lines represent meaningful
untested business logic. Skip trivial / passthrough / generated code without
filing.

STEP 1 — Locate the latest successful main-branch CI run

  gh run list --workflow=ci-main-branch.yml --branch=main --status=success \
    --limit 1 --json databaseId,headSha,createdAt --repo onpaj/Anela.Heblo

If no successful run exists in the last 7 days, log the gap, file no issues,
emit a summary (see STEP 7), and exit cleanly. Record the selected run's
databaseId as RUN_ID and headSha as RUN_SHA — both appear in the summary and
in every filed issue's footer.

STEP 2 — Download both artifacts

  mkdir -p /tmp/cov-be/${RUN_ID} /tmp/cov-fe/${RUN_ID}
  gh run download ${RUN_ID} --name coverage-backend  --dir /tmp/cov-be/${RUN_ID} --repo onpaj/Anela.Heblo
  gh run download ${RUN_ID} --name coverage-frontend --dir /tmp/cov-fe/${RUN_ID} --repo onpaj/Anela.Heblo

If either download fails or produces no files, note which side is unavailable
in the summary and proceed with the other side only. Do not file any issue for
the missing artifact.

STEP 3 — Parse backend Cobertura XML and select candidates

For every coverage.cobertura.xml found under /tmp/cov-be/${RUN_ID}:
  - Validate that <class filename="..."> attributes begin with "backend/src/".
    If the prefix is missing, abort the backend branch with an error in the
    summary — the CI sed step that adds the prefix has likely been removed.
  - Iterate every <class> node; key by filename. Where multiple <class> nodes
    share a filename (.NET emits per-type Cobertura), aggregate lines-valid and
    lines-covered across them to compute a file-level line-coverage percentage.
  - Skip files with aggregated lines-valid == 0.
  - Select files where file-level coverage < COVERAGE_THRESHOLD.
  - Group selected files by the path segment immediately after "Features/"
    (e.g. backend/src/Anela.Heblo.Application/Features/Catalog/X.cs → Catalog).
    Files outside any Features/ directory → group "Other".
  - Where the same Features/<Module>/<basename> appears in two or more backend
    projects (Domain / Application / Persistence / API / Adapters), merge them
    into a single candidate keyed by <Module>/<basename> — the qualifier
    produces at most one issue per logical file.

STEP 4 — Parse frontend LCOV and select candidates

Read /tmp/cov-fe/${RUN_ID}/lcov.info:
  - For each block, capture SF: (source file), LF: (lines found), LH: (lines hit).
  - Skip blocks where LF == 0.
  - Compute coverage = LH / LF * 100; select blocks where coverage < COVERAGE_THRESHOLD.
  - Group by the first path segment after "frontend/src/" (e.g.
    frontend/src/components/orders/X.tsx → group "components"; files at
    frontend/src/X.tsx → group "Other").

STEP 5 — Ensure the coverage-gap label exists

  gh label list --repo onpaj/Anela.Heblo --search coverage-gap

If no row contains "coverage-gap", create it:

  gh label create coverage-gap \
    --color e99695 \
    --description "Filed by weekly coverage-gap routine" \
    --repo onpaj/Anela.Heblo

STEP 6 — Qualify each candidate and file deduplicated issues

Iterate candidates sequentially (backend first, then frontend). Stop filing
once filed_count == FILE_CAP — continue qualifying remaining candidates only
to count them as truncated_due_to_cap in the summary.

For each candidate:

  a. Use Read to load the full source file. If the file cannot be read,
     record decision="skip", rationale="source not found in checkout",
     and continue.

  b. Apply the SKIP rubric — record decision="skip" with rationale if any
     of these match:
       - MVC controller whose action methods only delegate to
         _mediator.Send() or _mediator.Publish() with no other logic.
       - DTO / request / response class containing only properties.
       - Startup / configuration / DI registration: Program.cs, Startup.cs,
         files matching *Module.cs, files matching *ServiceCollectionExtensions.cs.
       - Files containing only auto-properties, auto-mapped fields, or
         trivial passthrough methods.
       - Generated code: <auto-generated> header, or OpenAPI-generated
         client code paths.

  c. Otherwise apply the FILE rubric — record decision="file" with
     rationale if any of these match:
       - MediatR IRequestHandler / INotificationHandler implementations with
         two or more conditional branches covering validation or business rules.
       - Domain logic with state transitions, status flows, or invariants.
       - Business calculations: financial totals, margins, stock quantities,
         pricing, discounts.
       - Error or exception paths whose failure shape (exception type,
         error code, response payload) is never asserted by an existing test.
         Use Grep / Glob to find related test files when assessing this.
       - Cross-module service contracts where the integration surface is
         untested.

  d. If decision == "file" and filed_count < FILE_CAP, dedup-check:

       gh issue list --repo onpaj/Anela.Heblo --label coverage-gap \
         --state open --search "<Module>/<basename> in:title" --limit 20

     If any open issue's title contains "<Module>/<basename>", record
     decision="dedup" with a pointer to that issue's URL and continue.

  e. Otherwise construct the issue title and body per the template below,
     then either:
       - If DRY_RUN == 1, print "WOULD FILE: <title>" followed by the body.
       - Else:
           gh issue create --repo onpaj/Anela.Heblo \
             --label coverage-gap --label tech-debt \
             --title "<title>" --body "<body>"
         Record the resulting issue URL.
     Increment filed_count.

  f. If decision == "file" and filed_count == FILE_CAP, mark this and all
     subsequent qualifying candidates as truncated_due_to_cap and continue
     qualifying only to count them. Do not call gh issue create after the cap.

ISSUE TITLE
  [coverage-gap] <Module>/<basename>: <specific untested logic description>

ISSUE BODY
  ## Module / File
  <full path>

  ## Coverage
  Line coverage: <X>% (filter threshold: 60%)

  ## What's not tested
  <Specific description — which branches, which conditions, which error paths.
   Describe behavior, do not paste source.>

  ## Why it matters
  <What could silently break if this logic regresses.>

  ## Suggested approach
  <Concrete: what type of test (unit/integration), what scenario to cover,
   rough effort estimate.>

  ---
  _Filed by weekly coverage-gap routine on <YYYY-MM-DD>. Based on CI run #<RUN_ID>._

STEP 7 — Emit a structured summary to stdout

Print, in this order, on the routine's stdout:

  source_ci_run_id:        <RUN_ID>
  source_ci_run_sha:       <RUN_SHA>
  source_ci_run_createdAt: <ISO timestamp>
  configuration:           threshold=60%, cap=10, dry_run=<0|1>
  backend_candidates:      <N>
  frontend_candidates:     <N>
  skipped:                 <N>   (top reasons: trivial=A, dto=B, dirig=C, generated=D, source-not-found=E, other=F)
  deduplicated:            <N>
  filed:                   <N>
    - <issue url 1>
    - <issue url 2>
    ...
  truncated_due_to_cap:    <N>   (with top files listed)
  artifact_gaps:           <none | backend-missing | frontend-missing>

FAILURE ISOLATION
  - Missing or empty backend artifact → process frontend only, note in summary.
  - Missing or empty frontend artifact → process backend only, note in summary.
  - gh failure on a single candidate's issue create → log and continue. The
    routine never leaves issues in a half-created state because each issue
    is independent.
  - On any unrecoverable parse error in a single artifact, abort that side
    only with an error line in the summary.

OUTPUT DISCIPLINE
  - Never paste source code into an issue body. Describe behavior.
  - Never claim a file is generated or trivial without naming the specific
    rubric clause that applies.
  - Every candidate appears in the summary's counts; rationale per candidate
    is logged in the routine output (not the summary lines).
```

## First-run smoke test

Before letting the cron fire for real, run the routine once with `DRY_RUN=1` via the scheduler's manual-trigger path. Confirm:

- A `RUN_ID` is selected.
- Both artifacts download.
- Backend `<class filename=` values are prefixed with `backend/src/`.
- LCOV `SF:` paths begin with `frontend/src/`.
- The label-check / create step does not error.
- The dry-run summary prints, listing N would-be filings.

If the smoke test prints clean output, register the real cron with `DRY_RUN` unset (or set to `0`). If it fails, fix and re-smoke before scheduling.
````

- [ ] **Step 2: Verify the file renders cleanly**

```bash
ls -la docs/routines/weekly-coverage-gap.md
wc -l docs/routines/weekly-coverage-gap.md
```

Expected: file exists, length is roughly 250–320 lines depending on whitespace.

- [ ] **Step 3: Commit**

```bash
git add docs/routines/weekly-coverage-gap.md
git commit -m "docs: add weekly coverage-gap routine documentation

Single-source-of-truth doc for the remote Claude Code routine that
inspects low-coverage files weekly and files coverage-gap issues.
Contains the verbatim routine prompt as a fenced block so the cron
registration always copies from one place. Documents coupling to the
ci-main-branch.yml sed step and the two upload-artifact steps."
```

---

## Task 4: Open a PR and verify CI produces both artifacts

**Goal:** Get the workflow changes onto `main` and confirm that the very first main-branch CI run after merge actually produces downloadable `coverage-backend` and `coverage-frontend` artifacts. The routine cannot be scheduled until this is verified.

- [ ] **Step 1: Push the branch and open the PR**

```bash
git push -u origin feat-coverage-gap-routine-design-spec
gh pr create --base main --title "feat: weekly coverage-gap routine — CI artifacts + routine doc" --body "$(cat <<'EOF'
## Summary

- Adds two new `actions/upload-artifact@v4` steps to `.github/workflows/ci-main-branch.yml`, immediately after the existing CodeCov uploads — one for the backend Cobertura XML (`coverage-backend`) and one for the frontend LCOV (`coverage-frontend`). Both retain artifacts for 7 days.
- Adds a guard comment on the existing backend `sed` step that rewrites Cobertura `filename=` paths to be `backend/src/`-prefixed. The weekly routine depends on this prefix; the comment exists so future cleanup doesn't silently break it.
- Adds `docs/routines/weekly-coverage-gap.md` with the routine's purpose, schedule, allowed tools, qualification rubric, issue template, dedup rule, operating instructions, and the verbatim routine prompt as a fenced code block. This file is the single source of truth — the cron entry copies the prompt from here.

No application code changes, no runtime dependencies added.

## Test plan

- [ ] Workflow YAML parses cleanly (`python3 -c "import yaml; yaml.safe_load(open('.github/workflows/ci-main-branch.yml'))"`).
- [ ] After merge, the next main-branch CI run completes successfully.
- [ ] `gh run list --workflow=ci-main-branch.yml --branch=main --status=success --limit 1 --repo onpaj/Anela.Heblo --json databaseId` returns the run id.
- [ ] `gh run download <run-id> --name coverage-backend --dir /tmp/cov-be --repo onpaj/Anela.Heblo` produces at least one `coverage.cobertura.xml`.
- [ ] `gh run download <run-id> --name coverage-frontend --dir /tmp/cov-fe --repo onpaj/Anela.Heblo` produces a non-empty `lcov.info`.
- [ ] First backend Cobertura `<class filename="..."` value begins with `backend/src/`.
- [ ] First frontend LCOV `SF:` value begins with `frontend/src/`.
EOF
)"
```

- [ ] **Step 2: Wait for the PR to merge to `main`**

Operator action. PR review may add comments; address them in the same branch. Do not proceed to Task 5 until the PR is merged.

- [ ] **Step 3: Locate the post-merge CI run**

```bash
gh run list --workflow=ci-main-branch.yml --branch=main --status=success --limit 1 \
  --repo onpaj/Anela.Heblo --json databaseId,headSha,createdAt
```

Expected: JSON containing the merge commit's `headSha` and a `databaseId`. Record the `databaseId` as `RUN_ID`.

- [ ] **Step 4: Download both artifacts and verify shape**

```bash
mkdir -p /tmp/cov-be /tmp/cov-fe
gh run download "$RUN_ID" --name coverage-backend  --dir /tmp/cov-be --repo onpaj/Anela.Heblo
gh run download "$RUN_ID" --name coverage-frontend --dir /tmp/cov-fe --repo onpaj/Anela.Heblo

find /tmp/cov-be -name 'coverage.cobertura.xml' -print
test -s /tmp/cov-fe/lcov.info && echo "lcov.info ok ($(wc -c </tmp/cov-fe/lcov.info) bytes)" || echo "lcov.info missing or empty"

grep -hom1 'filename="[^"]*"' /tmp/cov-be/**/coverage.cobertura.xml | head -1
head -1 /tmp/cov-fe/lcov.info
```

Expected:
- At least one path under `/tmp/cov-be/.../coverage.cobertura.xml`.
- `lcov.info ok (N bytes)` with N > 0.
- The first `filename="..."` line is prefixed with `backend/src/`.
- LCOV first line starts with `TN:` or `SF:frontend/src/...`.

If any of these checks fails, stop and diagnose before proceeding. Do not register the cron yet.

- [ ] **Step 5: Record the verified RUN_ID and SHA**

Note the `RUN_ID` and `headSha` from Step 3 in your scheduling notes for use in Task 5's smoke test.

---

## Task 5: Register the cron entry with a dry-run smoke test

**Goal:** Schedule the routine via `CronCreate` and prove end-to-end safety before the first real fire. This task is performed manually by an operator with scheduler access; it is out-of-tree and produces no repository changes.

- [ ] **Step 1: Open the routine doc and copy the prompt block**

Open `docs/routines/weekly-coverage-gap.md` on the merged `main` branch. Locate the `## Routine prompt (verbatim)` section and copy the entire fenced `text` block to the clipboard.

- [ ] **Step 2: First-run dry-run smoke test (DRY_RUN=1)**

Trigger the routine on-demand with `DRY_RUN=1` set in the environment so it parses everything and prints would-be filings without calling `gh issue create`. Use the schedule skill's manual-trigger path (or, if unavailable, register a temporary one-shot cron with the prompt prefixed by `DRY_RUN=1` exported in the bash environment).

Expected stdout shape:

```
source_ci_run_id:        <number>
source_ci_run_sha:       <40-char sha>
source_ci_run_createdAt: 2026-...
configuration:           threshold=60%, cap=10, dry_run=1
backend_candidates:      <N>
frontend_candidates:     <N>
skipped:                 <N>   (top reasons: ...)
deduplicated:            0
filed:                   0
truncated_due_to_cap:    0
artifact_gaps:           none
```

Plus, before the summary, a stream of `WOULD FILE: [coverage-gap] <Module>/<File>: ...` lines for each candidate that passed qualification.

If the routine reports `artifact_gaps: backend-missing` or `frontend-missing`, recheck Task 4 — the artifact names or paths are drifted. Fix and re-smoke.

If the routine emits zero `WOULD FILE` lines but `backend_candidates + frontend_candidates > 0`, the qualification rubric is rejecting everything — review the rationale lines for tuning before scheduling.

- [ ] **Step 3: Register the weekly cron**

Once the dry-run summary looks reasonable, register the real schedule:

```
CronCreate
  cron:        "0 4 * * 1"
  repo:        "onpaj/Anela.Heblo"
  model:       "claude-sonnet-4-6"
  allowedTools: ["Bash", "Read", "Glob", "Grep"]
  prompt:      <verbatim prompt copied from docs/routines/weekly-coverage-gap.md ## Routine prompt (verbatim)>
```

(Use the `schedule` skill's exact field names; the above is conceptual.)

- [ ] **Step 4: Confirm registration**

```
CronList
```

Expected: an entry with cron `0 4 * * 1`, repo `onpaj/Anela.Heblo`, model `claude-sonnet-4-6`, allowed tools exactly `Bash, Read, Glob, Grep`.

- [ ] **Step 5: Record the cron id**

Record the returned cron id in your operations notes so the routine can be paused (`CronDelete <id>`) or rotated when the prompt is updated.

- [ ] **Step 6: Verify the `coverage-gap` label appears after the first real run**

After the first Monday fire (or after triggering an on-demand non-dry-run run for verification), check:

```bash
gh label list --repo onpaj/Anela.Heblo --search coverage-gap
gh issue list --repo onpaj/Anela.Heblo --label coverage-gap --state open --limit 20
```

Expected:
- `coverage-gap` label exists with color `e99695`.
- Issue list shows the filed issues (between 0 and 10).
- Each issue has labels `coverage-gap`, `tech-debt`.
- Each issue body has the five-section template.

---

## Self-Review

**Spec coverage:**

| Spec section | Task(s) |
|---|---|
| FR-1 (backend artifact in CI) | Task 2 |
| FR-2 (frontend artifact in CI) | Task 1 |
| FR-3 (weekly cron, model, allowed tools, repo, prompt source) | Task 3 (prompt source) + Task 5 (CronCreate) |
| FR-4 (locate latest CI run) | Task 3 (STEP 1 of prompt) |
| FR-5 (download both artifacts, isolate failure) | Task 3 (STEP 2 of prompt) + Task 4 (verify) |
| FR-6 (parse Cobertura, candidates, modules) | Task 3 (STEP 3 of prompt) |
| FR-7 (parse LCOV, candidates, groups) | Task 3 (STEP 4 of prompt) |
| FR-8 (qualify by reading source) | Task 3 (STEP 6 b/c of prompt + Skip/File rubric in doc) |
| FR-9 (dedup + file issues + label create + template) | Task 3 (STEP 5 + 6 d–e of prompt + template in doc) |
| FR-10 (documentation) | Task 3 |
| FR-11 (per-run summary) | Task 3 (STEP 7 of prompt) |
| NFR-1 (perf, sequential, ≤200 gh calls) | Task 3 (prompt: sequential iteration, cap=10) |
| NFR-2 (security, allowed tools, no source pasting) | Task 3 (allowed tools constraint + OUTPUT DISCIPLINE) + Task 5 (cron registration matches) |
| NFR-3 (reliability, failure isolation, idempotency) | Task 3 (FAILURE ISOLATION + dedup rule) |
| NFR-4 (observability via summary) | Task 3 (STEP 7) |
| NFR-5 (maintainability, single-source rubric) | Task 3 (rubric in doc + verbatim prompt block in same file) |
| Arch-review amendment 1 (path prefix note) | Task 2 (guard comment) + Task 3 (prompt STEP 3 validates prefix) |
| Arch-review amendment 2 (per-file Cobertura aggregation) | Task 3 (prompt STEP 3 aggregates by filename) |
| Arch-review amendment 3 (cross-layer dedup) | Task 3 (prompt STEP 3 merges duplicate `<Module>/<basename>` across layers) |
| Arch-review amendment 4 (dedup search token) | Task 3 (prompt STEP 6 d uses `<Module>/<basename>`) |
| Arch-review amendment 5 (per-run filing cap) | Task 3 (FILE_CAP=10 + STEP 6 f + summary `truncated_due_to_cap`) |
| Arch-review amendment 6 (tighter artifact glob) | Task 2 (Step 2 uses `coverage.cobertura.xml`) |
| Arch-review amendment 7 (single-source prompt in doc) | Task 3 (prompt embedded verbatim in doc) |
| Arch-review amendment 8 (summary includes threshold/cap) | Task 3 (STEP 7 prints `configuration:` line) |
| Arch-review prerequisite 7 (dry-run smoke test) | Task 5 (Step 2) |

No spec requirement is unaccounted for.

**Placeholder scan:** Plan contains no "TBD" / "TODO" / "implement later" / vague "add appropriate handling" phrases. Every code block is complete and copy-pasteable. The only operator-dependent step is the PR merge (Task 4, Step 2) — that is unavoidable and explicit.

**Type consistency:** Constant names match across all tasks — `coverage-backend`, `coverage-frontend`, `coverage-gap`, `tech-debt`, `COVERAGE_THRESHOLD=60`, `FILE_CAP=10`, cron `0 4 * * 1`, model `claude-sonnet-4-6`, allowed tools `Bash, Read, Glob, Grep`. The artifact glob is `./coverage/**/coverage.cobertura.xml` in both Task 2 step 2 and the doc's coupling section. Issue title prefix `[coverage-gap]` matches between issue template, dedup search, and the FR-9 references.
