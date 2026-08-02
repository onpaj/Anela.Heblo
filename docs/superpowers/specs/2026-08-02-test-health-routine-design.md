# `test-health` — a scheduled, ReportPortal-driven test-regression routine

**Date:** 2026-08-02
**Status:** draft, awaiting review
**Scope:** a new harness_v2 Process that reads ReportPortal test history daily,
detects regressions, flakiness, and *missing* test data, and files a clustered
GitHub issue per finding.

---

## Purpose

ReportPortal has been collecting Heblo test history since 2026-07-19 across all
three layers (backend xUnit, frontend Jest, Playwright E2E). Nothing reads it.
`docs/testing/reportportal.md` always framed it as "the prerequisite that the E2E
auto-healing and triage tracks build on" — this routine is that consumer.

Every morning it reads a rolling 7-day window of ReportPortal launches, correlates
them with GitHub Actions history and recent commits, and files a GitHub issue for
each **new** test-health finding. Findings carry `harness:todo`, so each one flows
into the existing `development` workflow and is fixed automatically.

**Finding nothing is a correct and expected outcome.** A week where the suite is
green and complete should cost one agent call and file zero issues.

## The failure this is built to catch

On 2026-08-02, the E2E nightly had failed in 25–30 seconds every night since
2026-07-26 — eight consecutive nights. The cause was not a test:

```
##[error]AADSTS7000222: The provided client secret keys for app '***' are expired.
##[error]Login failed with Error: The process '/usr/bin/az' failed with exit code 1.
```

The staging deploy failed, so no tests ran, so ReportPortal received **no E2E data
for eight days**. The workflow's own aggregation step reported `GRAND_FAILED=0`.

The lesson is load-bearing for this design: **the failure mode that actually cost a
week was silence, not a red test.** A routine that only inspects failing tests would
have seen a clean, quiet week. Absence of data is therefore a first-class finding
here, computed by script rather than left for a model to notice.

## Prior art this must fit

This is a sibling of `telemetry-anomaly` (`docs/routines/telemetry-anomaly/`) and
follows its structure deliberately: a deterministic gathering script produces a
Markdown digest, an agent reasons over the digest, findings are fingerprinted and
deduplicated against existing issues, and issues are filed via a plain REST helper
rather than an MCP server.

Two departures, both responses to observed problems:

1. **Silence is computed, not judged.** `test-health-digest.sh` asserts expected-launch
   presence itself and emits explicit `MISSING:` findings. The agent is not asked to
   notice an absence.
2. **A misconfigured run escalates instead of going quiet.** `telemetry-anomaly` ran
   for the first time on 2026-08-02 and aborted — `APPINSIGHTS_APP_ID`,
   `APPINSIGHTS_API_KEY` and `GIT_PAT` are absent from `harness-root/secrets.env` —
   and reported outcome `done`, so nothing escalated. This routine emits a state key
   containing a consecutive-failure day counter so that `dedup: per-state` surfaces a
   persistent misconfiguration louder each day.

## Prerequisites (human action, blocking)

`~/harness-root/secrets.env` is sourced `set -a` by `harness-run.sh` and reaches every
task step. It currently contains only `CLAUDE_CODE_OAUTH_TOKEN`. This routine needs:

| Variable | Source | Needed for |
|---|---|---|
| `RP_API_KEY` | minted in the ReportPortal UI (Profile → API keys) | reading launches |
| `RP_ENDPOINT` | `http://nas.tail0cdb23.ts.net:8080/api/v1` | reading launches |
| `RP_PROJECT` | `heblo` (default if unset) | reading launches |
| `GIT_PAT` *or* `GITHUB_TOKEN` | GitHub PAT, or the gh keyring | filing issues |

`gh-api.sh` is extended to fall back to `GITHUB_TOKEN` when `GIT_PAT` is unset, since
`harness-run.sh` already derives `GITHUB_TOKEN` from `gh auth token`. That removes one
of the three variables that silently disabled `telemetry-anomaly`.

ReportPortal is tailnet-only (`nas.tail0cdb23.ts.net:8080`). The harness host `hermes`
is on the tailnet and reaches it directly — verified 2026-08-02, the API answers `401`
without a key. No Tailscale hop is needed on this side, unlike CI.

## Architecture

### Repo side — `docs/routines/test-health/`

| File | Purpose |
|---|---|
| `rp-query.sh` | Authenticated ReportPortal REST helper. Auth `Authorization: Bearer $RP_API_KEY`, base `$RP_ENDPOINT`. `--test` self-checks connectivity and credentials. |
| `test-health-digest.sh` | The deterministic engine. Runs the curated RP query set over a window, cross-checks GitHub Actions runs, emits a Markdown digest plus a machine-readable state line. |
| `gh-api.sh` | Copied from `telemetry-anomaly/`, with the `GITHUB_TOKEN` fallback added. Kept co-located so the routine folder is self-contained. |
| `README.md` | Routine definition: flag/skip rules, fingerprint scheme, dedup rules, caps. The agent reads this first; tuning happens here. |
| `fixtures/` | Recorded ReportPortal JSON responses for offline tests. |
| `test-health-digest.test.sh` | Offline test of the digest logic against `fixtures/`. |

### Harness side — `~/harness-root/`

`processes/test-health.json`:

```json
{
  "trigger": { "cron": "30 5 * * *" },
  "action": {
    "check": "command",
    "params": {
      "command": "git -C /Users/rem/Anela.Heblo pull --ff-only -q && /Users/rem/Anela.Heblo/docs/routines/test-health/test-health-digest.sh --state-only",
      "timeout": 300
    }
  },
  "target": { "step": "test-health" },
  "repository": "Anela.Heblo",
  "dedup": "per-state",
  "sink": { "kind": "none" }
}
```

`05:30` UTC is `07:30` Europe/Prague, comfortably after the nightly's ~34-minute run
starting at `04:00` UTC.

`agents/test-health.json` — prompt as specified below, `model: sonnet`,
`allowed_tools: [Read, Write, Bash, Grep, Glob]`, `allowed_outcomes: [done]`.

## Data flow

```
cron 05:30 UTC
  └─ test-health-digest.sh --state-only        (cheap; emits state key only)
       └─ harness dedup:per-state → new task only when state changed
            └─ agent step "test-health"
                 ├─ 1. test-health-digest.sh          → full Markdown digest
                 ├─ 2. gh-api.sh GET commits/PRs      → correlation context
                 ├─ 3. apply README flag/skip rules
                 ├─ 4. cluster → fingerprint → gh-api.sh find-signal (dedup)
                 └─ 5. gh-api.sh create-issue  (≤5 per run)
```

The digest is read-only and runs standalone. `./test-health-digest.sh` in a terminal
shows exactly what the agent will see, with no agent and no token spend.

### What the digest computes

Per layer (`backend`, `frontend`, `e2e`) and, for E2E, per module — the 11 modules in
the nightly matrix: `catalog`, `issued-invoices`, `stock-operations`, `transport`,
`manufacturing`, `core`, `marketing`, `finance`, `baleni`, `leaflet-generator`,
`terminal`.

1. **Launch inventory** — every launch in the window with name, attributes
   (`layer`, `module`, `ci`, `branch`), start time, status, and totals.
2. **Expected-vs-actual presence** — for each layer/module that reported at any point
   in the prior 7 days, assert a launch exists in the expected interval (26h for E2E,
   since it is nightly). Emit `MISSING:` for each gap.
3. **CI cross-check** — for every gap, query the GitHub Actions runs for the same
   window and classify the cause (see categories below). This is what distinguishes
   "tests were not run" from "tests ran but reporting failed".
4. **Suite-size trend** — total test count per layer/module against the 7-day median.
5. **Per-test outcome history** — pass/fail/skip per test across the window, which
   yields new failures, flip counts, and chronic failures.
6. **State line** — `STATE: <sha256 of the finding set + consecutive-error-day count>`,
   consumed by `--state-only` for harness dedup.

## Detection rules

### Categories flagged

| Category | Rule |
|---|---|
| `ci-broken` | An expected launch is missing **and** the corresponding GitHub Actions run failed before the test step. Body names the failing step and error. |
| `schedule-broken` | An expected launch is missing **and** no workflow run exists at all for the window. |
| `rp-reporting-broken` | An expected launch is missing **and** the workflow run *succeeded*. Reporting is broken, not the tests. |
| `suite-shrank` | Test count for a layer/module fell ≥20% below the 7-day median while the launch still succeeded. Catches silent skips and fixtures aborting a spec early. |
| `regression` | A test that passed within the window now fails in the latest **2 consecutive** E2E runs, or the latest **1** backend/frontend run (those are push-triggered and deterministic). |
| `flaky` | A test both passed and failed within the window with ≥2 status flips and a pass rate between 20% and 80%. |
| `chronic` | A test failing in every run for ≥7 days. Filed once, then suppressed by dedup. |

### Skipped deliberately

- Tests with RP status `SKIPPED` — a deliberate skip is not a failure. (`suite-shrank`
  is what catches skips that appear *without* explanation.)
- Launches whose `branch` attribute is not `main`.
- Any failure whose fix merged inside the window, per the GitHub commit/PR cross-check.
- Findings with a matching open issue, or one closed as `not_planned`.
- The first run against any layer/module with no 7-day baseline — no baseline, no
  regression claim.
- **Cascade suppression:** when a whole layer is missing because CI infrastructure
  broke, exactly **one** `ci-broken` issue is filed — not 11 per-module silence issues.

### Clustering

Failing tests are grouped by root signature: `(layer, module, normalized first line of
the error, failing step)`. Normalization strips line/column numbers, UUIDs, timestamps,
ports, and numeric suffixes in selectors. One broken shared fixture across fifteen specs
becomes one issue, not fifteen competing PRs.

### Volume cap

At most **5 issues per run**, in priority order: `ci-broken` / `schedule-broken` /
`rp-reporting-broken` → `regression` → `suite-shrank` → `flaky` → `chronic`. Everything
considered — filed or skipped, and why — is recorded in the step artifact, so a capped
run is auditable rather than silently truncated.

## Fingerprint and dedup scheme

The first line of every issue body is its fingerprint, matching the
`telemetry-signal:` convention:

| Category | Fingerprint |
|---|---|
| ci-broken | `test-ci:<workflow>:<failing-step>` |
| schedule-broken | `test-silence:<layer>:<module>:schedule` |
| rp-reporting-broken | `test-silence:<layer>:<module>:reporting` |
| suite-shrank | `test-shrink:<layer>:<module>` |
| regression | `test-regress:<layer>:<module>:<error-hash>` |
| flaky | `test-flaky:<layer>:<module>:<test-path>` |
| chronic | `test-chronic:<layer>:<module>:<error-hash>` |

`<error-hash>` is the first 8 hex chars of the sha256 of the normalized error line, so
the same root cause fingerprints identically across runs and across specs.

Dedup rules are inherited unchanged from `telemetry-anomaly`:

| Existing issue | Action |
|---|---|
| matching OPEN issue | SKIP — already tracked |
| matching issue CLOSED as `not_planned`, or closed with no linked merged PR | SKIP — previously dismissed |
| matching issue CLOSED by a merged PR, but the finding is back | file a NEW issue referencing the old |
| no match | file |

When it is unclear whether two findings are the same, err toward SKIP and record it.

## Issue contract

- **Title:** `[test-health] <layer>/<module>: <headline>`
- **Labels:** `test-health`, exactly one category label, and `harness:todo`. The
  category mapping is fixed:

  | Detection category | Label |
  |---|---|
  | `ci-broken`, `schedule-broken`, `rp-reporting-broken` | `test-infra` |
  | `regression`, `suite-shrank`, `chronic` | `test-regression` |
  | `flaky` | `test-flaky` |

- **Body, first line:** the `test-…:` fingerprint.
- **Body:** concrete numbers (runs affected, dates, pass-rate before/after), the
  affected spec paths, a trimmed failure excerpt, ReportPortal launch links, a
  correlation hypothesis where commits or PRs in the window explain it, and a minimal
  next step.

### The fix contract

`harness:todo` means the `development` workflow picks the issue up within 30 seconds
and opens a PR, and automerge is armed on this repository (dry_run false, 0.8
confidence, squash) with no required status checks. The most tempting "fix" for a
failing test is to weaken or delete it, and nothing downstream would stop that. Every
issue body therefore ends with a mandatory block instructing the implementing agent:

> **Fix contract — this issue is about test *health*. You must not:**
> - add `.skip`, `.fixme`, `test.skip`, `xit`, `Skip=` or any equivalent to any test;
> - delete a test, a spec file, or an assertion;
> - widen a timeout or add a retry as the *sole* change;
> - weaken an assertion to make it pass.
>
> If the correct resolution is genuinely to remove or disable a test, **stop and say so
> in the PR description instead of doing it.** A PR that reduces the total test count
> for this module is wrong by construction.

For the `test-infra` category (`ci-broken`, `schedule-broken`, `rp-reporting-broken`)
the body additionally states that the resolution is a credential or configuration
change **outside the repository**, requires a human, and that the agent should report
this rather than invent a code workaround. These issues are still labelled
`harness:todo` per the operator's decision; the block exists so the pickup produces a
clear "needs you" PR comment rather than a speculative code change. *(The expired
Azure client secret is exactly this case — no agent can rotate it.)*

## Error handling

| Condition | Behaviour |
|---|---|
| `RP_API_KEY` / `RP_ENDPOINT` missing | digest exits non-zero naming the variable; agent reports the abort plainly and finishes `done` (a `failed` verdict would route a config problem to the self-healer, which cannot fix it) |
| ReportPortal unreachable (connect failure/timeout) | **distinguished from "no data"** — the digest emits `RP_UNREACHABLE` and suppresses every silence finding, so a tailnet outage cannot mass-file false silence issues |
| ReportPortal returns 401/403 | treated as a configuration abort, not as absence of data |
| GitHub API 403/429 | `gh-api.sh` already retries transient search-API throttling |
| Persistent abort | the state line carries a consecutive-error-day counter, so `dedup: per-state` produces a fresh, louder task each day instead of an identical silent one |
| Partial RP data (some layers reachable, some queries fail) | findings are emitted only for layers whose queries succeeded; degraded coverage is stated explicitly in the digest header |

The routine never changes code, never opens a PR, never commits. Its only writes are
the step artifact and the GitHub issues it files.

## Testing

1. **`rp-query.sh --test`** — connectivity and credential self-check, runnable by hand.
2. **`test-health-digest.test.sh`** — offline tests of the digest logic against recorded
   ReportPortal JSON in `fixtures/`, with `RP_FIXTURE_DIR` short-circuiting the network.
   Cases: a clean week (zero findings); a missing E2E module; a whole layer missing with
   a failed CI run (must yield exactly one `ci-broken`, not eleven silence findings); a
   suite that shrank; a genuine regression; a flaky test at 50% pass rate; RP unreachable
   (must yield zero silence findings).
3. **Fingerprint stability** — the same recorded failure fingerprints identically across
   two runs, and two different failures do not collide.
4. **Dry-run calibration** — `TEST_HEALTH_DRY_RUN=1` makes `gh-api.sh create-issue` print
   the issue instead of POSTing it. The routine is run this way for its first few days so
   the filed set can be judged before any PR is generated.

## Open questions for review

1. **Thresholds are first guesses**, chosen to be conservative: 20% suite shrink, 2
   consecutive E2E failures for a regression, 2 flips and a 20–80% pass rate for flaky,
   5 issues per run. They are all in `README.md` so they can be tuned without touching
   code — but they have not been validated against real ReportPortal data, because no
   API key exists on this machine yet.
2. **ReportPortal API shapes are unverified.** The endpoints (`/{project}/launch`,
   `/{project}/item`) follow the documented v5 REST API, but no call has been made
   against the live instance. First implementation step is `rp-query.sh --test` against
   the real server; query shapes may need adjustment.
3. **RP may hold less history than assumed.** Reporting was enabled 2026-07-19 and E2E
   died 2026-07-26, so there may be only ~6 nights of E2E data, and the backend/frontend
   layers depend on how often `main` was pushed. The 7-day baseline rules degrade safely
   (no baseline → no finding), but the first useful run may be thin.
