# Architecture Review: Coverage Gap Routine

## Skip Design: true

No UI/UX work. The feature is a CI workflow change (two new upload-artifact steps), an out-of-tree remote routine configuration, and one new documentation file. No frontend components, no screens, no visual decisions.

## Architectural Fit Assessment

The feature is a tooling addition that lives almost entirely **outside the application's Clean Architecture / Vertical Slice boundary**. It does not touch the .NET solution, React app, MediatR pipeline, DI registration, or persistence. It introduces no new modules, no migrations, no contracts, and no runtime dependencies in the codebase.

Integration points are all external:

1. **CI workflow** (`.github/workflows/ci-main-branch.yml`) — two new `actions/upload-artifact@v4` steps appended to the existing CodeCov upload sequence in the `frontend-tests` and `backend-tests` jobs.
2. **GitHub platform** — Actions artifacts, Issues, Labels. The routine consumes from and writes to these via `gh` CLI only.
3. **Remote Claude Code scheduler** — a cron entry registered via the schedule skill (`CronCreate`), referencing a prompt that lives in `docs/routines/`.
4. **Repository documentation tree** — a new `docs/routines/` directory (does not exist today; `docs/` contains `architecture/`, `features/`, `plans/`, etc., but no `routines/`).

Because the routine has only `Bash`, `Read`, `Glob`, `Grep` and runs against a clone of the repo on the remote runner, the architectural surface area inside the codebase is intentionally tiny. The risk profile is also low: a malformed routine produces noise on Issues, not broken builds or deploys.

One important constraint to surface for implementers: the existing backend CI step at `ci-main-branch.yml:119-122` **mutates the Cobertura XML before CodeCov upload**, prepending `backend/src/` to every `filename=` attribute. The persisted `coverage-backend` artifact therefore contains paths shaped like `backend/src/Anela.Heblo.Application/Features/<Module>/...`. The routine's module-parsing logic in FR-6 must match against this prefixed form, not the raw assembly-local path.

A second constraint: `Features/` directories exist in **five** backend projects, not just `Anela.Heblo.Application`:

- `backend/src/Anela.Heblo.Application/Features/...`
- `backend/src/Anela.Heblo.Domain/Features/...`
- `backend/src/Anela.Heblo.Persistence/Features/...`
- `backend/src/Anela.Heblo.API/Features/...`
- `backend/src/Adapters/Anela.Heblo.Adapters.*/Features/...`

The spec's "path segment immediately after `Features/`" rule handles all five cleanly, but the rubric in FR-8 must explicitly recognize that Domain/Application/Persistence/API/Adapter slices of the same module group together — otherwise a single feature could produce duplicate issues from different layers when each layer's class files appear separately in the Cobertura output.

## Proposed Architecture

### Component Overview

```
                ┌──────────────────────────────────────────┐
                │ ci-main-branch.yml (main push)           │
                │                                          │
                │  backend-tests ─► codecov ─► [NEW]       │
                │                              upload      │
                │                              "coverage-  │
                │                              backend"    │
                │                                          │
                │  frontend-tests ─► codecov ─► [NEW]      │
                │                               upload     │
                │                               "coverage- │
                │                               frontend"  │
                └─────────────────┬────────────────────────┘
                                  │ artifacts (7d retention)
                                  ▼
            ┌─────────────────────────────────────────────┐
            │  GitHub Actions artifact storage            │
            └─────────────────┬───────────────────────────┘
                              │ gh run download
                              ▼
   ┌──────────────────────────────────────────────────────────┐
   │ Remote Claude Code routine (Mon 06:00 Prague)            │
   │   model: claude-sonnet-4-6                               │
   │   tools: Bash, Read, Glob, Grep  (no Write/Edit)         │
   │                                                          │
   │   1. gh run list (latest main success)                   │
   │   2. gh run download (both artifacts)                    │
   │   3. Parse Cobertura XML + LCOV → Candidate[]            │
   │   4. For each: Read source → qualify (skip|file)         │
   │   5. gh issue list (dedup) → gh issue create             │
   │   6. Emit run summary to stdout                          │
   └─────────────────┬────────────────────────────────────────┘
                     │ gh issue create
                     ▼
        ┌─────────────────────────────────────┐
        │ GitHub Issues (labels: coverage-gap,│
        │                       tech-debt)    │
        └─────────────────────────────────────┘

   Prompt source-of-truth: docs/routines/weekly-coverage-gap.md
   Cron registration:      out-of-tree via CronCreate/CronList/CronDelete
```

### Key Design Decisions

#### Decision 1: Routine logic lives in the prompt, not in repo scripts

**Options considered:**
- (A) Implement parsing + qualification as a Python/Bash script committed to `scripts/`, executed by the routine.
- (B) Keep the entire workflow inside the Claude Code prompt; routine uses only `Bash`/`Read`/`Glob`/`Grep`.

**Chosen approach:** (B). The routine reads the prompt, runs `gh` and lightweight shell pipelines for parsing, and uses Claude's reasoning for the qualification step.

**Rationale:** The qualification step (FR-8) is the entire value proposition — it requires Claude reading source. Anything that could be encoded as a static script would just be a coverage threshold filter, which is what FR-6/FR-7 already are. Splitting parsing into a committed script would add a dependency that the routine would have to re-install on every run and a moving piece the team would maintain. The 7-day artifact retention plus weekly cadence means the routine is recreated from scratch every week — no advantage to pre-baking logic.

#### Decision 2: Module identity is path-based, not project-based

**Options considered:**
- (A) Module = .NET assembly name (e.g. `Anela.Heblo.Application`).
- (B) Module = first path segment after `Features/` (e.g. `Catalog`, `Manufacture`).
- (C) Module = composite of (assembly, feature folder).

**Chosen approach:** (B), as the spec already specifies.

**Rationale:** Verified against the actual filesystem: every backend project follows `<ProjectRoot>/Features/<Module>/<File>` for its slice contents. Grouping by `Features/<Module>/` produces business-relevant module names (`Catalog`, `Manufacture`, `Logistics`) that match how the team thinks about the system. Option (A) would split a single feature across multiple "modules". Option (C) is over-specified — when the routine files an issue for, say, "Catalog/GetProductHandler.cs", the layer is obvious from the file path in the issue body.

Files outside `Features/` (`Program.cs`, `Anela.Heblo.Xcc/*`, shared utilities) fall into the `Other` bucket per FR-6, which is correct.

#### Decision 3: `coverage-gap` label is self-managed by the routine

**Options considered:**
- (A) Create the label manually before first run.
- (B) Routine creates it idempotently on every run via "check then create".

**Chosen approach:** (B), as in FR-9.

**Rationale:** Verified that the `coverage-gap` label does **not** currently exist in `onpaj/Anela.Heblo` (existing labels include `tech-debt`, `agent`, `agent-solved`, etc.). Self-creation removes a manual setup step and makes the routine portable. The check via `gh label list` plus conditional `gh label create` is one extra API call per run — negligible.

#### Decision 4: Dedup search uses a path token, not full title match

**Options considered:**
- (A) Exact title match.
- (B) Substring match on file path or module name within issue titles.
- (C) Hash the candidate identity into a hidden marker in the body.

**Chosen approach:** Refined version of (B): search for the **file basename** (or `<Module>/<File>` token) inside titles filtered by label `coverage-gap`, state `open`.

**Rationale:** Exact match is brittle when humans rename the issue or when the routine's title generator phrases the "specific untested logic description" slightly differently between runs. A hidden body marker (C) works but adds machinery for no benefit since GitHub's title search is sufficient. **Amendment recommended (see below):** narrow the search token to the file path segment (e.g. `Catalog/GetProductHandler.cs` rather than just `GetProductHandler.cs`) to avoid false matches on common file names like `Index.tsx` or `Handler.cs`.

## Implementation Guidance

### Directory / Module Structure

New files / directories to create:

```
docs/
  routines/                                ← NEW (directory does not exist)
    weekly-coverage-gap.md                 ← NEW: per FR-10
      ├─ Purpose
      ├─ Schedule (0 4 * * 1 UTC)
      ├─ Allowed tools
      ├─ Full routine prompt (verbatim)
      ├─ Qualification rubric (skip/file lists from FR-8)
      ├─ Issue template (from FR-9)
      └─ Operating instructions (pause/rerun via CronList/CronDelete)
```

Modified files:

```
.github/workflows/ci-main-branch.yml
  ├─ Insert after line 71  (frontend codecov):  upload-artifact "coverage-frontend"
  └─ Insert after line 146 (backend codecov):   upload-artifact "coverage-backend"
```

Out-of-tree (not committed):

- Cron registration in the Claude Code scheduling system, pointing the prompt at the committed `docs/routines/weekly-coverage-gap.md` URL.

### Interfaces and Contracts

The only durable contracts that other systems depend on:

1. **CI artifact names** — `coverage-backend` and `coverage-frontend`. Renaming either breaks the routine. Document this coupling at the top of `weekly-coverage-gap.md`.
2. **CI artifact contents** — paths inside `coverage.cobertura.xml` are prefixed with `backend/src/` by the existing `sed` step (workflow line 119-122). If that step is ever removed or modified, the routine's module parser breaks. Document this coupling.
3. **GitHub label `coverage-gap`** — name, not color, is load-bearing. The routine searches by label name.
4. **GitHub issue title prefix `[coverage-gap]`** — used by dedup search.
5. **Issue body template** — humans read these; the routine itself does not parse old issues, so format drift is safe across versions.

In-memory shapes (used inside one routine run, not exported anywhere):

```
Candidate           = { side, path, module, lineCoveragePercent }
QualifiedCandidate  = Candidate + { decision, rationale, issueTitle?, issueBody? }
```

### Data Flow

**Weekly run, happy path:**

```
1. routine starts (Mon 06:00 Prague)
2. gh run list --workflow=ci-main-branch.yml --branch=main --status=success
     → run-id, headSha, createdAt
3. gh run download <run-id> --name coverage-backend  --dir /tmp/cov-be/<run-id>/
   gh run download <run-id> --name coverage-frontend --dir /tmp/cov-fe/<run-id>/
4. Parse backend Cobertura with grep/xmlstarlet/inline awk:
     for each <class filename="backend/src/.../Features/<M>/<File>" line-rate="0.X">
       if line-rate < 0.60 and lines-valid > 0:
         append Candidate{ side="backend", path, module=M, pct=line-rate*100 }
5. Parse frontend LCOV:
     scan SF: blocks
     for each (LF, LH):
       if LF > 0 and LH/LF < 0.60:
         module = first segment of path after "frontend/src/"
         append Candidate{ side="frontend", path, module, pct=LH/LF*100 }
6. For each Candidate (sequential, per NFR-1):
     Read full file content
     [Claude qualification using FR-8 rubric]
     Record decision + rationale
     If decision == "file":
       gh issue list --repo onpaj/Anela.Heblo --label coverage-gap --state open \
                     --search "<Module>/<basename> in:title" --limit 20
       if no match:
         gh issue create with FR-9 template + labels coverage-gap, tech-debt
         record issue url
7. emit summary to stdout (FR-11)
```

**Failure isolations (per FR-5 / NFR-3):**

- Missing backend artifact → process frontend only, note gap in summary.
- Missing frontend artifact → process backend only, note gap in summary.
- No successful main run in 7 days → exit cleanly, file nothing, log gap.
- Any `gh issue create` failure → log error, continue to next candidate (no partial state corruption since each issue is independent).

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Existing `sed` step in `ci-main-branch.yml:119-122` mutates `filename=` paths. If anyone removes it (e.g. as "cleanup"), the routine's module parser silently breaks. | High | Document the coupling at the top of `weekly-coverage-gap.md` and add a code comment to the `sed` line in the workflow referencing the routine doc. Routine should also validate path prefix and abort with a clear error if it doesn't match the expected shape. |
| Backend Cobertura `<class>` granularity differs by collector — sometimes per type, sometimes per file. Same file with multiple classes can produce multiple `<class>` nodes. | Medium | Group candidate `<class>` entries by `filename` attribute before threshold check; compute file-level `lines-covered / lines-valid` so partial-class noise doesn't produce duplicate candidates. Update FR-6 acceptance criteria to make this explicit. |
| Dedup search using a generic basename (`Index.tsx`, `Handler.cs`) matches unrelated open issues, suppressing legitimate filings. | Medium | Use `<Module>/<basename>` as the search token (not just basename). Verified there's no existing `[coverage-gap]` issue today, so first run cannot collide. |
| Issue spam: a low-quality qualification step could file 50 issues in one run. | Medium | Add a per-run cap (suggest 10 filed issues max). Emit a summary line if the cap is hit so the threshold or rubric can be tuned. |
| The 60% threshold + qualitative rubric is the only filter. Over time, a backlog of open `coverage-gap` issues accumulates; routine keeps surfacing the same files (deduplicated) but never closes them, hiding new gaps behind a backlog. | Medium | Out of scope per spec, but recommend tracking weekly counts in the summary so the trend is visible. Future enhancement: sort candidates by `(uncovered_lines * meaningfulness)` and bias toward not-yet-filed modules. |
| `actions/upload-artifact@v4` runs only when the prior step succeeds, by default. If `dotnet test` fails on main, no Cobertura is produced and no artifact is uploaded — but that already breaks the build, so it's acceptable. The Codecov step has `continue-on-error: true`; the new upload step should NOT inherit `if: always()` because uploading partial/missing coverage muddies signal. | Low | Match the surrounding `continue-on-error` semantics but do NOT use `if: always()`. Spec FR-1 already calls this out — confirmed appropriate. |
| Remote runner clone may not include the file the Cobertura references (rare path drift between runner workspace and committed paths). | Low | Routine validates `Read` succeeds before reasoning; missing files are logged with `decision=skip, rationale="source not found in checkout"` and counted in the summary. |
| `tech-debt` label assumed pre-existing. Verified it does exist today. Renaming or deleting it would silently change issue taxonomy. | Low | Document the dependency in `weekly-coverage-gap.md`. |
| Routine prompt and `weekly-coverage-gap.md` rubric can drift if updated separately. NFR-5 names the doc as source-of-truth but the routine itself reads from the prompt, not the doc. | Low | Operational rule: edits to the rubric land in the doc first, then the prompt is regenerated from it. Consider keeping the prompt as a code block inside the doc so they're physically one file. |

## Specification Amendments

The spec is in good shape. The following clarifications should be folded in:

1. **FR-1 / FR-6 — backend path prefix.** Add an explicit note: "The existing step at `ci-main-branch.yml:119-122` rewrites Cobertura `filename=` attributes to be prefixed with `backend/src/`. The routine's module parser MUST expect this prefix and MUST validate it on parse. The persisted artifact preserves this rewritten form." Without this note, an implementer reading only the spec will write a parser that fails against real artifact contents.

2. **FR-6 — Cobertura class granularity.** Clarify the per-file aggregation rule: "Where multiple `<class>` nodes share a `filename`, aggregate `lines-valid` and `lines-covered` across them and compute file-level line coverage. Threshold is applied to the file-level aggregate, not the per-class `line-rate`." This makes the parser robust to .NET's per-type Cobertura emission and prevents duplicate candidates.

3. **FR-6 / Cross-layer dedup.** Add: "If the same `Features/<Module>/<Class>.cs` filename appears in multiple project directories (Domain, Application, Persistence, API, Adapters) — which is rare but possible for shared abstractions — group those candidates together so the qualifier produces at most one issue per logical file."

4. **FR-9 — dedup search token.** Tighten: "Search token must be `<Module>/<basename>` (e.g. `Catalog/GetProductHandler.cs`), not just `<basename>`. The bare basename produces false positives for common names like `Index.tsx`."

5. **FR-9 — per-run filing cap.** Add: "The routine files at most 10 issues per run. If more candidates qualify, the surplus is listed in the summary (FR-11) as `truncated_due_to_cap: N` with the top reasons. This prevents unbounded issue creation if the rubric drifts permissive." Tuneable alongside the threshold and cron, per NFR-5.

6. **FR-1 — artifact path glob precision.** The current glob `./coverage/**/*.cobertura.xml` is correct but loose. The actual filename produced by `dotnet test --collect:"XPlat Code Coverage"` is exactly `coverage.cobertura.xml`. Tightening to `./coverage/**/coverage.cobertura.xml` is more explicit and protects against any future `.cobertura.xml` siblings being accidentally swept in. Either form passes the acceptance criteria; recommend the tighter form.

7. **FR-10 — single-source rubric.** Per NFR-5, the doc is source-of-truth. Recommend physically embedding the prompt inside `weekly-coverage-gap.md` as a fenced code block, with the cron registration referencing a specific section of the doc rather than a separate prompt file. This removes the synchronization risk entirely.

8. **FR-11 — summary should include the threshold and cap values.** So that when humans review the run log, the configuration that produced the result is on record.

## Prerequisites

Before this feature can be turned on:

1. **CI workflow change merged to `main`.** FR-1 and FR-2 must land and produce at least one successful run before any cron registration. Verified by `gh run list --workflow=ci-main-branch.yml --branch=main --status=success --limit 1` finding a run AND `gh run download <run-id> --name coverage-backend` producing a non-empty file.
2. **`docs/routines/` directory created** with `weekly-coverage-gap.md` committed and reachable at a stable URL.
3. **`tech-debt` label exists** in `onpaj/Anela.Heblo`. Verified ✓ (already present).
4. **`coverage-gap` label** — NOT required pre-existing; the routine creates it on first run. Verified NOT present today ✓.
5. **Remote runner image has `gh` CLI with authenticated access** to `onpaj/Anela.Heblo` (read code, write issues, write labels, read Actions artifacts). This is the standard Claude Code remote routine environment; no new secrets to provision.
6. **Cron registered** via `CronCreate` with expression `0 4 * * 1`, model `claude-sonnet-4-6`, repo `onpaj/Anela.Heblo`, allowed tools `Bash,Read,Glob,Grep`, prompt sourced from `docs/routines/weekly-coverage-gap.md`. This step is performed manually (one-time) by an operator with scheduler access; cron creation is out-of-tree and not part of the PR.
7. **First-run smoke test.** Recommend running the routine on-demand once (via the scheduler's manual trigger) before letting the cron fire, to verify artifact discovery, parsing, and label creation work end-to-end without filing real issues. The routine has no `--dry-run` flag in the spec; suggest adding one in the prompt as `DRY_RUN=1` (parses everything, prints what it would file, skips `gh issue create`) for safe first-run verification.