# Test Health Routine

A self-hosted, ReportPortal-driven routine that reads Anela Heblo's rolling
test history every morning, correlates it with GitHub Actions history, and
**files a GitHub issue for each new test-health finding** — a regression, a
flaky test, a shrinking suite, or missing test data.

It is the ReportPortal-driven sibling of `telemetry-anomaly`
(`docs/routines/telemetry-anomaly/`): telemetry reads runtime behaviour, this
one reads test outcomes. It follows the same shape deliberately — a
deterministic gathering script produces a Markdown digest, an agent reasons
over the digest, findings are fingerprinted and deduplicated against existing
issues, and issues are filed via a plain REST helper rather than an MCP
server.

On 2026-08-02, the E2E nightly had been failing at the Azure login step for
**eight consecutive nights** — an expired client secret killed the staging
deploy in 25–30 seconds, before a single test ran, so ReportPortal received
**zero E2E launches for eight days** while the workflow's own aggregation
step reported `GRAND_FAILED=0`. A routine that only inspected failing tests
would have seen a clean, quiet week. That is the case this routine exists to
catch: **absence of expected data is a first-class finding here, computed by
`test-health-digest.sh`, not left for the agent to notice.** Read this file
before doing anything else — it is where the flag/skip rules, the
fingerprint scheme, and the fix contract every filed issue must carry all
live, and it is where thresholds get tuned (see the caveat under
"Thresholds" — tuning a number here also means editing the script).

## Files in this folder

| File | Purpose |
|---|---|
| `rp-query.sh` | Authenticated ReportPortal v5 REST helper. Auth `Authorization: Bearer $RP_API_KEY`, base `$RP_ENDPOINT` (`/api/v1`, project prepended unless `--raw`). `--test` self-checks connectivity and credentials. `RP_FIXTURE_DIR` replays recorded JSON offline. Exit codes `0`/`1`/`3`/`4`/`5` (see Troubleshooting). |
| `rp-query.test.sh` | Offline unit tests for `rp-query.sh`: missing-variable errors, fixture replay, missing-fixture error, and that the API key is never printed. |
| `gh-api.sh` | Minimal authenticated GitHub REST helper, copied from `telemetry-anomaly/gh-api.sh` with the search predicate retargeted (`label:telemetry` → `label:test-health`, `telemetry-signal:` → `test-signal:`). `GET`/`POST`/`PATCH`/`DELETE`, `find-signal` (dedup search), `create-issue`. Retries transient 403/429. Token from `GIT_PAT` or `GITHUB_TOKEN`. |
| `gh-api.test.sh` | Offline tests asserting the retargeting: searches `label:test-health` and the `test-signal: ` prefix, no leftover `telemetry`/`telemetry-signal` predicate, keeps the `GITHUB_TOKEN` fallback, errors without a token. |
| `test-health-digest.sh` | The deterministic engine. Pulls launches + failed items from ReportPortal for a window, cross-checks GitHub Actions for the nightly workflow, computes every finding below, and prints a Markdown digest ending in a machine-readable `STATE:` line. Requires `jq` and `perl` on `PATH` (guarded at startup). |
| `test-health-digest.test.sh` | Offline tests of the digest against `fixtures/`: a clean week, RP-unreachable suppressing silence findings, the consecutive-error-day counter, and state determinism. |
| `fixtures/` | Recorded ReportPortal/GitHub JSON for offline replay (`RP_FIXTURE_DIR`, `GH_FIXTURE_DIR`): `clean`, `ci-broken`, `silent-module`, `gh-down`, `gh-down-partial`, `gh-malformed`, `shrank`, `regression`, `flaky`, `chronic`, `chronic-thin`, `self-healed`, `two-errors`, `big-numbers`. |
| `README.md` | This file — the routine definition. The agent reads it first; tuning happens here (with the caveat below). |

## Routine details

| Field | Value |
|---|---|
| Routine ID | Not yet created — the harness `processes/test-health.json` / `agents/test-health.json` pair is Task 7 |
| Schedule | `30 5 * * *` UTC (07:30 Europe/Prague), comfortably after the nightly's ~34-minute run starting at 04:00 UTC |
| Model | `sonnet` |
| Repo | `onpaj/Anela.Heblo` |
| Window | 7 days (`test-health-digest.sh` default; override with `--days N`) |

## Thresholds

These are the numeric knobs `test-health-digest.sh` actually applies. **They
are hard-coded in the script, not read from this file** — this table exists
so a human can see and reason about every threshold in one place, but
changing a value here has no effect until the corresponding line in
`test-health-digest.sh` is edited to match.

| Threshold | Value | Enforced by |
|---|---|---|
| Window | 7 days by default | `DAYS=7`, `--days N` |
| E2E freshness horizon | 26h | `E2E_FRESH_MS=$(( 26 * 3600 * 1000 ))` |
| Suite-shrink trigger | newest run's total ≥20% below the median of the other runs, needs ≥3 runs to have a baseline | `SHRINK_PCT=20`; `[[ "$n" -ge 3 ]]` guard |
| Regression trigger | a test's two most-recently-held launches for its layer/module both fail, and the window holds no other failure for that test (exactly 2 total) | `recent_fails -eq 2 && k -eq 2` |
| Flaky trigger | ≥2 status flips **and** a pass rate in [20%, 80%], compared as integers without pre-dividing | `flips -ge 2` plus the `(n-k)*100` bounds vs. `20*n` / `80*n` |
| Chronic trigger | red in every held launch for that layer/module, minimum 3 launches | `k -eq n && n -ge 3` |
| Issues per run (cap) | ≤5, in priority order (see below) | `CAP=5` and the `rank` map |

**Chronic is deliberately not "≥7 days".** A launch count is a calendar week
only for the nightly E2E layer; backend and frontend report per push to
`main`, where several launches can be a single afternoon or a quiet week can
produce none at all. The chronic headline reports the *measured* span in
days and the run count it actually observed, and never asserts a duration it
hasn't verified.

**Priority order for the cap** (highest first — infrastructure faults are
never crowded out by a pile of flaky tests): `ci-broken` (0) → `schedule-broken`
(1) → `silence-unattributed` (2) → `rp-reporting-broken` (3) → `regression`
(4) → `suite-shrank` (5) → `flaky` (6) → `chronic` (7).

## What it flags

Eight categories. Findings are computed per `layer`/`module` pair, taken
from each ReportPortal launch's `layer`/`module` attributes (unknown/missing
attributes fall back to `"unknown"`/`"-"` rather than being dropped).

| Category | Rule as implemented |
|---|---|
| `ci-broken` | **Cascade case:** every E2E module that reported in the window is stale (see freshness horizon) *and* the `e2e-nightly-regression.yml` workflow's most recent run failed — filed **once**, naming the failing step, not once per module. Also covers the "whole layer stale, cause unknown" variant (GitHub couldn't be queried, or answered with a body that isn't shaped like a runs response) — that variant stays in `ci-broken` with fingerprint suffix `unattributed`, not `silence-unattributed`, because a whole-layer outage outranks one unattributable module. |
| `schedule-broken` | A single E2E module's launch is stale, the *whole* layer is not stale (so the cascade above didn't fire), and the workflow's most recent run reports no conclusion at all. |
| `silence-unattributed` | A single E2E module's launch is stale and GitHub's Actions API could not be queried, or answered with a body that isn't a valid runs response. The data really is absent; the cause is undetermined, and the finding must not claim one. |
| `rp-reporting-broken` | A single E2E module's launch is stale but the workflow's most recent run **succeeded** — the tests ran, ReportPortal just never heard about it. |
| `suite-shrank` | A layer/module has ≥3 launches in the window and the newest run's total test count fell ≥20% below the median of the rest, while that run still succeeded (skipped tests or an aborted fixture, not a failure). |
| `regression` | A test's two most-recently-held launches for its layer/module both failed, and the window holds no other failure for that test. This is applied **uniformly across all layers** — the script does not currently give backend/frontend a 1-run trigger; see the discrepancy note below. |
| `flaky` | A test has ≥2 status flips across the window and a pass rate landing in [20%, 80%] (floor-safe integer comparison, so a true rate of 80.99% still lands in the band). |
| `chronic` | A test is red in every held launch for its layer/module, with at least 3 launches to judge from. Headline states the measured span (days) and run count, per the "not ≥7 days" note above. |

**Important gap in current coverage:** the presence/silence checks
(`ci-broken`, `schedule-broken`, `silence-unattributed`, `rp-reporting-broken`)
are computed **only for the `e2e` layer**. The script's staleness
computation hardcodes `stale: false` for every non-`e2e` layer, so a
backend or frontend module that stops reporting entirely is not detected by
this routine today — only `suite-shrank`, `regression`, `flaky`, and
`chronic` currently reach backend/frontend. See the discrepancy note in the
task report; this is worth a follow-up if backend/frontend silence turns out
to matter in practice.

## What it skips

- **RP status `SKIPPED`.** Only items with status `FAILED`/`INTERRUPTED` are
  fetched as failures, so a deliberate skip never enters the failure
  history. (`suite-shrank` is what catches skips that appear *without*
  explanation, via the total-count drop.)
- **Non-`main` branches** — a launch is kept only if its `branch` attribute
  is `main` **or** the attribute is missing (`unknown`); any other branch
  value is dropped before any finding is computed.
- **Layer/modules with no baseline.** The first launch(es) for a layer/module
  aren't enough to claim anything: `suite-shrank`/`chronic` need ≥3 launches,
  `regression` needs ≥2. Below that, the pair is silently skipped — no
  finding, not even a "not enough data" note.
- **The cascade rule.** When the whole E2E layer is stale and the workflow's
  last run failed or is unattributable, exactly **one** `ci-broken` finding
  is filed, not one `schedule-broken`/`rp-reporting-broken`/
  `silence-unattributed` per module. The cascade only fires for a *failed* or
  *unattributable* last run — if the workflow's last run **succeeded** while
  every module is stale, the per-module loop runs instead and each stale
  module gets its own `rp-reporting-broken` finding.
- **Fixes merged in-window, matching open issues, and issues closed
  `not_planned`.** These are not computed by `test-health-digest.sh` at
  all — it has no GitHub commit/PR correlation logic. They are the **agent's**
  responsibility, applied via `gh-api.sh find-signal` per the Dedup decision
  table below, using the rules in this README.

## Fingerprint table

The first line of every issue body carries one of these, prefixed as
described in "Issue contract" below.

| Category | Fingerprint |
|---|---|
| `ci-broken` (attributed) | `test-ci:e2e-nightly-regression.yml:<failing-step>` |
| `ci-broken` (cascade, cause unknown) | `test-ci:e2e-nightly-regression.yml:unattributed` |
| `schedule-broken` | `test-silence:<layer>:<module>:schedule` |
| `silence-unattributed` | `test-silence:<layer>:<module>:unattributed` |
| `rp-reporting-broken` | `test-silence:<layer>:<module>:reporting` |
| `suite-shrank` | `test-shrink:<layer>:<module>` |
| `regression` | `test-regress:<layer>:<module>:<error-hash>` |
| `flaky` | `test-flaky:<layer>:<module>:<test-path>` |
| `chronic` | `test-chronic:<layer>:<module>:<error-hash>` |

`<error-hash>` is the first 8 hex characters of the sha256 of the normalized
first error line (line/column numbers, hex ids of 8+ chars, timestamps, and
`Nms` durations stripped) — the same root cause fingerprints identically
across runs and across specs, and two genuinely different errors in the same
module never collide onto one hash.

## Dedup decision table

Inherited unchanged from `telemetry-anomaly`:

| Existing issue | Action |
|---|---|
| Matching issue **OPEN** | SKIP — already tracked |
| Matching issue **CLOSED** as `not_planned`, or closed with no linked merged PR | SKIP — previously dismissed |
| Matching issue **CLOSED** by a merged PR, but the finding is back | File a **NEW** issue, referencing the old one |
| No match | File |

When it's unclear whether two findings are the same, err toward SKIP and
record why.

## Issue contract

- **Title:** `[test-health] <layer>/<module>: <headline>`
- **Labels:** `test-health`, exactly one category label, and `harness:todo`.

  | Detection category | Label |
  |---|---|
  | `ci-broken`, `schedule-broken`, `silence-unattributed`, `rp-reporting-broken` | `test-infra` |
  | `regression`, `suite-shrank`, `chronic` | `test-regression` |
  | `flaky` | `test-flaky` |

- **Body, first line:** `test-signal: <fingerprint>` — the literal
  `test-signal: ` prefix, then the fingerprint from the table above. This is
  not optional styling: `gh-api.sh find-signal` searches for the exact
  string `label:test-health in:body "test-signal: <fingerprint>"`, so a body
  whose first line is only the bare fingerprint (no `test-signal: ` prefix)
  will never be found by a later dedup search.
- **Body:** concrete numbers (runs affected, dates, pass-rate before/after),
  the affected spec paths, a trimmed failure excerpt, ReportPortal launch
  links, a correlation hypothesis where commits or PRs in the window explain
  it, and a minimal next step.
- **For the `test-infra` label** (`ci-broken`, `schedule-broken`,
  `silence-unattributed`, `rp-reporting-broken`), the body additionally
  states that the resolution is a credential or configuration change
  **outside the repository**, requires a human, and that the agent should
  report this rather than invent a code workaround. These issues are still
  labelled `harness:todo` per the operator's decision — the block exists so
  the pickup produces a clear "needs you" PR comment rather than a
  speculative code change. *(The expired Azure client secret is exactly this
  case — no agent can rotate it.)*

## The fix contract

`harness:todo` means the `development` workflow picks the issue up within 30
seconds and opens a PR, and automerge is armed on this repository (dry_run
false, 0.8 confidence, squash) with no required status checks. The most
tempting "fix" for a failing test is to weaken or delete it, and nothing
downstream would stop that. Every issue body therefore ends with a mandatory
block instructing the implementing agent:

> **Fix contract — this issue is about test *health*. You must not:**
> - add `.skip`, `.fixme`, `test.skip`, `xit`, `Skip=` or any equivalent to any test;
> - delete a test, a spec file, or an assertion;
> - widen a timeout or add a retry as the *sole* change;
> - weaken an assertion to make it pass.
>
> If the correct resolution is genuinely to remove or disable a test, **stop and say so
> in the PR description instead of doing it.** A PR that reduces the total test count
> for this module is wrong by construction.

For the `test-infra` category (`ci-broken`, `schedule-broken`,
`silence-unattributed`, `rp-reporting-broken`) the body additionally states
that the resolution is a credential or configuration change **outside the
repository**, requires a human, and that the agent should report this rather
than invent a code workaround. These issues are still labelled `harness:todo`
per the operator's decision; the block exists so the pickup produces a clear
"needs you" PR comment rather than a speculative code change. *(The expired
Azure client secret is exactly this case — no agent can rotate it.)*

## Running it by hand

```bash
export RP_API_KEY=...            # from the ReportPortal UI
export RP_ENDPOINT=http://nas.tail0cdb23.ts.net:8080/api/v1
export RP_PROJECT=heblo
./docs/routines/test-health/rp-query.sh --test
./docs/routines/test-health/test-health-digest.sh --days 7
```

`./test-health-digest.sh` in a terminal shows exactly what the agent will
see, with no agent and no token spend. Add `--state-only` to print just the
`STATE:` line the harness uses for dedup.

For an offline dry run against recorded fixtures instead of the live
server:

```bash
RP_FIXTURE_DIR=docs/routines/test-health/fixtures/clean \
  ./docs/routines/test-health/test-health-digest.sh --days 7
```

## Troubleshooting

`test-health-digest.sh` requires both `jq` and `perl` on `PATH`; either
missing aborts at startup naming the tool (exit `1`).

| Exit code | Meaning |
|---|---|
| `0` | ok — digest produced, findings computed (possibly zero) |
| `1` | config error — a required tool (`jq`/`perl`), `rp-query.sh` itself, or an argument is missing/invalid |
| `3` | ReportPortal unreachable |
| `4` | ReportPortal rejected the API key (401/403) |
| `5` | ReportPortal returned an unexpected HTTP status (404/429/5xx) |

Two things to never get backwards:

- **Exit `3` means ReportPortal is unreachable — never read it as "the tests
  did not run."** The script suppresses every silence finding in this case
  precisely so a tailnet outage cannot masquerade as, or mass-file, missing-
  test-data issues. It records a consecutive-error-day count instead, so a
  persistent outage gets louder each day rather than going quiet.
- **Exit `1` means your configuration is wrong** — a missing variable, a
  missing tool, a bad argument. Server-side faults use exit `5` instead,
  deliberately, so an operator responding to `1` goes looking for a
  misconfigured variable, and one responding to `5` goes looking at
  ReportPortal itself, never the other way around.
