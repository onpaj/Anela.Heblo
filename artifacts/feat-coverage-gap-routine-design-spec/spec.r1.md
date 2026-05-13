# Specification: Coverage Gap Routine

## Summary

A weekly remote Claude Code routine that consumes CodeCov coverage artifacts produced by the main-branch CI workflow, identifies files with low line coverage, qualitatively reads the source to determine whether the gap reflects untested *meaningful* logic, and files actionable GitHub issues only for gaps worth closing. Raw coverage numbers act as a candidate filter; Claude's qualitative judgment is the gate that decides what becomes an issue.

## Background

Line coverage is a noisy quality signal. A controller delegating to `mediator.Send()` and a handler with five branching business rules both contribute to the line-coverage denominator, but only one materially benefits from tests. A routine that mechanically flags "module X is at 54%" produces noise — coverage for coverage's sake — and trains contributors to ignore the alerts.

We want the opposite: a small number of high-signal issues per week, each pointing at a concrete piece of untested logic with a suggested test approach. Two enabling pieces are required: (1) CI must publish coverage reports as durable artifacts so a later, asynchronous routine can consume them, and (2) the routine itself must read source code and reason about whether the uncovered lines represent real risk.

## Functional Requirements

### FR-1: Persist backend coverage artifact in CI

The main-branch CI workflow must publish backend Cobertura XML coverage files as a downloadable GitHub Actions artifact named `coverage-backend`, immediately after the existing CodeCov upload step.

**Acceptance criteria:**
- A new step using `actions/upload-artifact@v4` exists in `.github/workflows/ci-main-branch.yml` directly after the backend CodeCov upload step.
- The step uploads files matching `./coverage/**/*.cobertura.xml` under the artifact name `coverage-backend`.
- Retention is set to `7` days.
- The step runs on every successful main-branch CI run and does not block the workflow on upload failure (use `if: always()` if the existing CodeCov step does — otherwise match the surrounding step's failure semantics).
- After a successful main-branch run, `gh run download <run-id> --name coverage-backend` produces at least one `*.cobertura.xml` file.

### FR-2: Persist frontend coverage artifact in CI

The main-branch CI workflow must publish the frontend LCOV coverage file as a downloadable GitHub Actions artifact named `coverage-frontend`, immediately after the existing frontend CodeCov upload step.

**Acceptance criteria:**
- A new step using `actions/upload-artifact@v4` exists in `.github/workflows/ci-main-branch.yml` directly after the frontend CodeCov upload step.
- The step uploads `./frontend/coverage/lcov.info` under the artifact name `coverage-frontend`.
- Retention is set to `7` days.
- After a successful main-branch run, `gh run download <run-id> --name coverage-frontend` produces a non-empty `lcov.info` file.

### FR-3: Schedule weekly remote Claude Code routine

A scheduled remote Claude Code routine runs weekly on Monday at 06:00 Prague time, executing the coverage-gap workflow defined in FR-4 through FR-9.

**Acceptance criteria:**
- The routine is registered with cron expression `0 4 * * 1` (UTC), which corresponds to Monday 06:00 CEST and 05:00 CET.
- The routine targets repository `https://github.com/onpaj/Anela.Heblo`.
- The routine uses model `claude-sonnet-4-6`.
- The routine's allowed tool list is exactly: `Bash`, `Read`, `Glob`, `Grep`. No `Write`, `Edit`, `WebFetch`, or other tools are permitted.
- Cron registration, listing, and deletion is performed via the schedule skill (`CronCreate` / `CronList` / `CronDelete`).
- The routine prompt is stored as a versioned file in the repository (see FR-10) and referenced by the cron entry.

### FR-4: Locate latest successful main-branch CI run

The routine identifies the most recent successful run of the main-branch CI workflow as its data source.

**Acceptance criteria:**
- Uses `gh run list --workflow=ci-main-branch.yml --branch=main --status=success --limit 1 --json databaseId,headSha,createdAt`.
- If no successful run exists within the last 7 days, the routine logs the gap, files no issues, and exits cleanly with a non-error status.
- The selected run's `databaseId` and `headSha` are recorded for inclusion in any filed issue's footer.

### FR-5: Download both coverage artifacts

The routine downloads the backend and frontend coverage artifacts from the selected CI run.

**Acceptance criteria:**
- Downloads to working directories `/tmp/cov-be` and `/tmp/cov-fe` respectively (or per-run subdirectories under `/tmp` to avoid stale data).
- Uses `gh run download <run-id> --name coverage-backend --dir <path>` and the equivalent for frontend.
- If either artifact is missing or empty, the routine logs which side is unavailable, processes the other, and notes the gap in its summary output. It does not file an issue for the missing artifact.

### FR-6: Parse backend Cobertura XML and select candidates

The routine extracts per-class line-rate values from the Cobertura XML and selects classes whose line-rate is below the threshold.

**Acceptance criteria:**
- Parses every `<class>` node, reading `filename` and `line-rate` attributes.
- Computes line coverage as `line-rate * 100` (Cobertura emits `line-rate` as a fraction in `[0, 1]`).
- Selects all classes where `line-rate < 0.60`.
- Groups results by Feature module by parsing the path segment immediately after `Features/` in the file path (e.g. `backend/src/Anela.Heblo.Application/Features/Catalog/...` → module `Catalog`). Files outside `Features/` are grouped under module `Other`.
- Excludes classes with zero executable lines (`lines-valid` or equivalent count of zero).

### FR-7: Parse frontend LCOV and select candidates

The routine parses the frontend LCOV report and selects files whose line coverage is below the threshold.

**Acceptance criteria:**
- Parses `SF:` (source file), `LF:` (lines found), and `LH:` (lines hit) records per file block.
- Computes line coverage as `LH / LF * 100`. Files with `LF == 0` are excluded.
- Selects all files where line coverage `< 60%`.
- Groups results by the first subfolder under `frontend/src/` (e.g. `frontend/src/components/orders/...` → group `components`).

### FR-8: Qualify each candidate by reading the source

For each candidate file, the routine reads the source code and decides whether the uncovered logic is meaningful enough to file an issue.

**Acceptance criteria:**
- For each candidate, the routine uses `Read` to load the full source file (and `Grep`/`Glob` if needed to find related test files or callers for context).
- The routine **skips** (no issue filed) for files matching any of these shapes:
  - MVC controllers whose action methods only delegate to `_mediator.Send()` or `_mediator.Publish()` with no other logic.
  - DTO, request, and response classes containing only properties.
  - Startup, configuration, and DI registration code (`Program.cs`, `Startup.cs`, `*Module.cs`, `*ServiceCollectionExtensions.cs` patterns).
  - Files containing only auto-properties, auto-mapped fields, or trivial passthrough methods.
  - Generated code (e.g. files with `<auto-generated>` headers, OpenAPI-generated client code under known generated paths).
- The routine **files an issue** for files matching any of these shapes:
  - Handlers (MediatR `IRequestHandler` / `INotificationHandler` implementations) with two or more conditional branches covering validation or business rules.
  - Domain logic with state transitions, status flows, or invariants.
  - Business calculations (financial totals, margins, stock quantities, pricing, discounts).
  - Error or exception paths whose failure shape (exception type, error code, response payload) is never asserted by an existing test.
  - Cross-module service contracts where the integration surface is untested.
- The skip/file decision and a one-line rationale are recorded in the routine's summary output for every candidate, even those skipped.

### FR-9: File deduplicated GitHub issues for qualifying gaps

For every candidate that passes qualification, the routine files a GitHub issue using the standard format, after checking that no equivalent open issue already exists.

**Acceptance criteria:**
- Before filing, the routine runs `gh issue list --repo onpaj/Anela.Heblo --label coverage-gap --state open --search "<file-or-module> in:title" --limit 20` and skips if a matching open issue exists.
- The match check compares the file path (or module name when no file path applies) against the existing issue titles, allowing for the `[coverage-gap]` prefix.
- The issue is created with title format `[coverage-gap] <Module>/<File>: <specific untested logic description>`.
- Issue body uses the template:
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
- Labels applied: `coverage-gap`, `tech-debt`.
- The `coverage-gap` label is created (color `#e99695`, description `"Filed by weekly coverage-gap routine"`) on first run if it does not exist; the routine checks via `gh label list` and creates with `gh label create` only when missing.
- The `tech-debt` label is assumed to exist in the repository and is not created by the routine.
- All `gh` invocations target `--repo onpaj/Anela.Heblo` explicitly so the routine works from any working directory.

### FR-10: Document the routine

A new documentation file describes the routine for human maintainers.

**Acceptance criteria:**
- File `docs/routines/weekly-coverage-gap.md` exists and includes: purpose, schedule, allowed tools, the full prompt, the qualification rubric (skip vs. file lists from FR-8), the issue template, and operating instructions for pausing or rerunning the routine.
- The file is referenced from any existing routines index, or — if none exists — sits as a self-contained document under `docs/routines/`.

### FR-11: Per-run summary output

Every run of the routine emits a structured summary as its final output, regardless of how many issues were filed.

**Acceptance criteria:**
- Summary lists: source CI run id and date, total backend candidates, total frontend candidates, count skipped (with top-level reason breakdown), count filed, count deduplicated, list of issue numbers/URLs filed, and any artifact-availability gaps from FR-5.
- Summary is printed to the routine's standard output so it is captured by the remote run log.

## Non-Functional Requirements

### NFR-1: Performance

- A single routine run completes within 15 minutes of wall-clock time under typical conditions (≤ 100 candidate files combined backend + frontend).
- Source-file reads happen sequentially per candidate; no parallel `Bash` jobs are required. The routine is bounded by Claude reasoning time, not CLI throughput.
- Total `gh` API calls per run stay below GitHub's authenticated REST limit (5,000/hr), with substantial headroom — expected ≤ 200 calls (one issue list + one issue create per filed gap, plus run/artifact metadata).

### NFR-2: Security

- The routine has read-only access to source code and write access only to GitHub Issues (label create, issue create, issue list/search). It cannot push code, modify workflows, or alter repository settings.
- Allowed tools are restricted to `Bash`, `Read`, `Glob`, `Grep`. `Write`, `Edit`, `WebFetch`, and any MCP write tools are explicitly disallowed via the routine configuration.
- All `gh` operations rely on the remote runner's existing GitHub credentials. No new secrets are introduced.
- Issue bodies must not include source-code excerpts containing secrets. Since the source files in scope are application code (not configuration), this is a low-risk concern; the prompt explicitly instructs the routine to describe untested behavior rather than paste source.

### NFR-3: Reliability

- A failure in the routine (missing artifact, parse error, `gh` API failure) must not file partial or malformed issues. On any unrecoverable error, the routine emits an error summary and exits without filing anything for the failing branch (backend or frontend can fail independently per FR-5).
- Deduplication (FR-9) prevents repeated filing across weekly runs as long as the original issue remains open.
- The routine is idempotent within a single CI-run window: rerunning against the same CI run produces the same set of issues (assuming no concurrent state change in GitHub Issues).

### NFR-4: Observability

- The structured summary in FR-11 serves as the primary observability surface.
- A failed routine run surfaces via the standard remote-routine failure notification path; no custom alerting is built.

### NFR-5: Maintainability

- The qualification rubric (FR-8 skip/file lists) lives in both the routine prompt and `docs/routines/weekly-coverage-gap.md` and must be kept in sync. The doc is the source of truth; the prompt is regenerated from it on changes.
- The 60% threshold and the cron schedule are the two tunable knobs. Both live as named values near the top of the prompt for easy adjustment.

## Data Model

The routine is stateless — it owns no persistent data of its own. It operates on three external data sources:

| Source | Shape | Owner | Lifetime |
|---|---|---|---|
| `coverage-backend` artifact | One or more Cobertura XML files (`*.cobertura.xml`) with per-class `line-rate` attributes | GitHub Actions | 7 days post-CI run (per FR-1) |
| `coverage-frontend` artifact | Single LCOV file (`lcov.info`) with `SF:`/`LF:`/`LH:` records per file | GitHub Actions | 7 days post-CI run (per FR-2) |
| GitHub Issues with label `coverage-gap` | Standard GitHub issue: title, body, labels, state | GitHub | Until manually closed |

In-memory data structures used during a run (not persisted):

- **Candidate** — `{ side: "backend"|"frontend", path: string, module: string, lineCoveragePercent: number }`
- **Qualified candidate** — `Candidate & { decision: "file"|"skip", rationale: string, issueTitle?: string, issueBody?: string }`

## API / Interface Design

### CI workflow (FR-1, FR-2)

Two new YAML steps in `.github/workflows/ci-main-branch.yml`. Insertion points: directly after the existing backend and frontend `codecov-action` steps.

```yaml
# After backend CodeCov upload
- name: 📦 Persist backend coverage artifact
  uses: actions/upload-artifact@v4
  with:
    name: coverage-backend
    path: ./coverage/**/*.cobertura.xml
    retention-days: 7

# After frontend CodeCov upload
- name: 📦 Persist frontend coverage artifact
  uses: actions/upload-artifact@v4
  with:
    name: coverage-frontend
    path: ./frontend/coverage/lcov.info
    retention-days: 7
```

### Remote Claude Code routine

| Property | Value |
|---|---|
| Schedule (UTC cron) | `0 4 * * 1` |
| Local equivalent | Monday 06:00 Prague (CEST) / 05:00 (CET) |
| Model | `claude-sonnet-4-6` |
| Repo | `onpaj/Anela.Heblo` |
| Allowed tools | `Bash`, `Read`, `Glob`, `Grep` |
| Prompt source | `docs/routines/weekly-coverage-gap.md` (or a sibling prompt file referenced from it) |

The routine's external interface is GitHub itself: it reads from GitHub Actions (artifacts, run metadata) via `gh`, reads source files from a freshly cloned/checkout repository (per the remote runner's standard setup), and writes by creating GitHub issues via `gh issue create`. There are no HTTP endpoints, no UI, and no internal services to integrate with.

### GitHub issue interface

Title pattern: `[coverage-gap] <Module>/<File>: <specific untested logic description>`

Body template: see FR-9.

Labels: `coverage-gap` (created on first run if missing, color `#e99695`), `tech-debt` (pre-existing).

## Dependencies

- **GitHub Actions** — for CI execution and artifact hosting. The two new `actions/upload-artifact@v4` steps inherit GitHub Actions' availability and retention semantics.
- **GitHub CLI (`gh`)** — used inside the routine for run discovery, artifact download, label and issue management. Must be present on the remote runner image (standard for Claude Code remote runners).
- **CodeCov action (existing)** — must continue to produce coverage files at `./coverage/**/*.cobertura.xml` (backend) and `./frontend/coverage/lcov.info` (frontend). Any change to those paths requires updating the upload steps in lockstep.
- **Claude Code remote routine infrastructure** — `CronCreate` / `CronList` / `CronDelete` skill for scheduling; standard remote runner with checkout of `onpaj/Anela.Heblo`.
- **Existing labels** — `tech-debt` label must already exist in the repo (verified at routine setup time).

No new packages, services, or runtime dependencies are introduced in the application codebase.

## Out of Scope

- **Running tests or computing coverage.** The routine consumes what CI produces; it never executes `dotnet test` or `npm test`.
- **Hard coverage gates.** The 60% threshold is a candidate filter for Claude to read, not a build-blocking enforcement. CI is unchanged in its pass/fail behavior.
- **Code changes.** The routine has no `Write` or `Edit` tool access; it cannot author tests, change source, or open pull requests.
- **Per-PR analysis.** Only main-branch runs are consumed. PR coverage is not analyzed by this routine.
- **E2E or integration coverage.** Only unit-test coverage as reported by CodeCov from the existing CI is in scope. E2E coverage is not measured and not consumed.
- **Per-feature dashboards or rollups.** No dashboards, reports, or aggregated metrics beyond the per-run summary in FR-11.
- **Auto-closing issues when coverage improves.** Issues remain open until manually closed by a human; the routine only files, never closes.
- **Cross-repo support.** Hard-coded to `onpaj/Anela.Heblo`.
- **Notification fan-out.** No Slack, email, or chat integration. Output is the routine's run log and the filed GitHub issues.

## Open Questions

None.

## Status: COMPLETE
