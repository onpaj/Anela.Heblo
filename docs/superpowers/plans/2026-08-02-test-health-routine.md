# test-health Routine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a scheduled harness routine that reads ReportPortal test history daily, detects regressions, flakiness and *missing* test data, and files clustered `harness:todo` GitHub issues.

**Architecture:** A deterministic bash digest (`test-health-digest.sh`) queries the self-hosted ReportPortal v5 REST API and the GitHub Actions API, then prints a Markdown digest plus a machine-readable state line. A harness Process runs the digest in `--state-only` mode on cron and, when the state changes, dispatches an agent step that reasons over the full digest, deduplicates findings by fingerprint, and files issues. Detection of *absence* is computed by the script, never left to the agent.

**Tech Stack:** bash 3.2 (macOS system bash), `curl`, `jq`, ReportPortal v5 REST API, GitHub REST API, harness_v2 Processes/Agents.

## Global Constraints

- **Spec:** `docs/superpowers/specs/2026-08-02-test-health-routine-design.md`. Every rule, threshold and fingerprint comes from there verbatim.
- **Branch:** all repo work on `feat/test-health-routine` in `/Users/rem/Anela.Heblo`.
- **Host bash is 3.2** (macOS 12). No associative arrays (`declare -A`), no `${var^^}`, no `mapfile`. Use `awk`/`jq`/temp files instead.
- **Host `date` is BSD.** Use `date -u -v-7d +%s`, never `date -d`. Any date helper must try BSD first and fall back to GNU.
- **`jq` is at `/Users/rem/.local/bin/jq`**, which is on `harness-run.sh`'s PATH. Scripts must check for it and fail with a clear message.
- **The digest's notion of "now" is overridable via `TEST_HEALTH_NOW_MS`** (epoch milliseconds), defaulting to wall-clock now. Fixtures carry absolute timestamps, so every test must pin this — otherwise the rolling window slides past the fixtures within days and the suite rots into a false pass: launches filtered out, zero findings, everything apparently green. All fixture-driven tests in Tasks 3, 4 and 5 export `TEST_HEALTH_NOW_MS=1785030000000`.
- **Every script is read-only** with respect to the repo and the app: no code changes, no commits, no PRs. The only writes are the step artifact, the state file, and filed GitHub issues.
- **Never print or log `RP_API_KEY`, `GIT_PAT` or `GITHUB_TOKEN`.**
- **Exit-code contract**, shared by `rp-query.sh` and the digest: `0` ok, `1` usage/config error (named variable), `3` ReportPortal unreachable (network), `4` ReportPortal auth rejected (401/403), `5` ReportPortal returned an unexpected HTTP status (404/429/5xx). `1` must mean "your configuration is wrong" and nothing else — the agent prompt tells the operator to go fix a variable when it sees `1`. The digest treats `3`, `4` and `5` identically: the gather failed, so nothing is known about test presence and no silence finding may be emitted.
- **Repo rule (`CLAUDE.md`): GitHub access via `gh` CLI or the REST helper only — never MCP GitHub tools.**
- **Validation:** this change touches only shell scripts, docs and JSON. No `dotnet build` / `npm run build` is required.

## File Structure

| Path | Responsibility |
|---|---|
| `docs/routines/test-health/rp-query.sh` | Authenticated ReportPortal REST access + fixture replay. Nothing else. |
| `docs/routines/test-health/gh-api.sh` | GitHub REST helper (copied from telemetry-anomaly, fingerprint predicate retargeted). |
| `docs/routines/test-health/test-health-digest.sh` | All detection logic; emits Markdown digest + state line. |
| `docs/routines/test-health/test-health-digest.test.sh` | Offline tests driving the digest from `fixtures/`. |
| `docs/routines/test-health/fixtures/` | Recorded/hand-authored ReportPortal + GitHub JSON responses. |
| `docs/routines/test-health/README.md` | Routine definition: flag/skip rules, fingerprints, dedup, caps, thresholds. Tuning happens here. |
| `docs/routines/test-health/harness/test-health.process.json` | Versioned copy of the harness Process config. |
| `docs/routines/test-health/harness/test-health.agent.json` | Versioned copy of the harness Agent config (the prompt). |
| `docs/routines/test-health/harness/install.sh` | Copies the two JSON files into `~/harness-root/`. |

`~/harness-root` is not a git repository, so the JSON lives in the repo and is installed by copying. That is deliberate: the existing `heblo-arch-review` config exists only in `~/harness-root` and is therefore unversioned and unrecoverable.

---

### Task 1: `rp-query.sh` — authenticated ReportPortal access

**Files:**
- Create: `docs/routines/test-health/rp-query.sh`
- Create: `docs/routines/test-health/rp-query.test.sh`

**Interfaces:**
- Consumes: nothing.
- Produces: `rp-query.sh [--raw] <path>` prints the JSON body on stdout and exits per the exit-code contract. `RP_FIXTURE_DIR=<dir>` replays `<dir>/<sanitized-path>.json` instead of calling the network. Sanitization: strip a leading `/`, then replace every character not in `[A-Za-z0-9._-]` with `_`. Later tasks depend on exactly this sanitization to name fixture files.

- [ ] **Step 1: Write the failing test**

Create `docs/routines/test-health/rp-query.test.sh`:

```bash
#!/usr/bin/env bash
# Offline tests for rp-query.sh. No network, no credentials.
set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
Q="${HERE}/rp-query.sh"
pass=0; fail=0
check() { # check <name> <expected> <actual>
  if [[ "$2" == "$3" ]]; then pass=$((pass+1)); echo "ok   - $1"
  else fail=$((fail+1)); echo "FAIL - $1: expected [$2] got [$3]"; fi
}

# 1. Missing RP_API_KEY is a config error (exit 1) naming the variable.
out="$(env -u RP_API_KEY RP_ENDPOINT=http://x/api/v1 "$Q" '/launch' 2>&1)"; rc=$?
check "missing key exits 1" "1" "$rc"
case "$out" in *RP_API_KEY*) r=yes ;; *) r=no ;; esac
check "missing key names the variable" "yes" "$r"

# 2. Missing RP_ENDPOINT is a config error (exit 1) naming the variable.
out="$(env -u RP_ENDPOINT RP_API_KEY=x "$Q" '/launch' 2>&1)"; rc=$?
check "missing endpoint exits 1" "1" "$rc"
case "$out" in *RP_ENDPOINT*) r=yes ;; *) r=no ;; esac
check "missing endpoint names the variable" "yes" "$r"

# 3. Fixture mode replays from disk and never touches the network.
tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
printf '{"content":[]}' > "${tmp}/launch_page.size_1.json"
out="$(RP_FIXTURE_DIR="$tmp" RP_API_KEY=x RP_ENDPOINT=http://unreachable.invalid/api/v1 \
       "$Q" '/launch?page.size=1' 2>&1)"; rc=$?
check "fixture mode exits 0" "0" "$rc"
check "fixture mode returns the file" '{"content":[]}' "$out"

# 4. A missing fixture is an explicit error, not an empty success.
out="$(RP_FIXTURE_DIR="$tmp" RP_API_KEY=x RP_ENDPOINT=http://x/api/v1 \
       "$Q" '/nope' 2>&1)"; rc=$?
check "missing fixture exits 1" "1" "$rc"

# 5. The key must never appear in output.
out="$(RP_FIXTURE_DIR="$tmp" RP_API_KEY=SUPERSECRET RP_ENDPOINT=http://x/api/v1 \
       "$Q" '/nope' 2>&1)"
case "$out" in *SUPERSECRET*) r=leaked ;; *) r=clean ;; esac
check "key is never printed" "clean" "$r"

echo "---"; echo "passed: $pass  failed: $fail"
[[ $fail -eq 0 ]]
```

- [ ] **Step 2: Run test to verify it fails**

```bash
chmod +x docs/routines/test-health/rp-query.test.sh
./docs/routines/test-health/rp-query.test.sh
```

Expected: FAIL — the harness cannot execute `rp-query.sh` (No such file or directory), every check fails.

- [ ] **Step 3: Write minimal implementation**

Create `docs/routines/test-health/rp-query.sh`:

```bash
#!/usr/bin/env bash
#
# rp-query.sh — query the self-hosted ReportPortal v5 REST API.
#
# Credentials come from the environment (never hardcode, never print):
#   RP_API_KEY   API key minted in the ReportPortal UI (Profile -> API keys)
#   RP_ENDPOINT  REST base INCLUDING /api/v1, e.g. http://nas.tail0cdb23.ts.net:8080/api/v1
#   RP_PROJECT   target project (default: heblo)
#
# Paths are relative to the project (/{project} is prepended) unless --raw.
#
# Offline replay: set RP_FIXTURE_DIR to a directory of recorded responses.
# The fixture file name is the path with a leading '/' stripped and every
# character outside [A-Za-z0-9._-] replaced by '_', plus '.json'.
#
# Exit codes: 0 ok | 1 config/usage error | 3 unreachable | 4 auth rejected
#             5 unexpected HTTP status (404/429/5xx)
#
set -uo pipefail

RP_PROJECT="${RP_PROJECT:-heblo}"

err()  { echo "Error: $*" >&2; exit 1; }
errc() { local c="$1"; shift; echo "Error: $*" >&2; exit "$c"; }

RAW=0
if [[ "${1:-}" == "--raw" ]]; then RAW=1; shift; fi

if [[ "${1:-}" == "--test" ]]; then
  [[ -n "${RP_API_KEY:-}" ]]  || err "RP_API_KEY is not set."
  [[ -n "${RP_ENDPOINT:-}" ]] || err "RP_ENDPOINT is not set."
  echo "Testing ${RP_ENDPOINT} (project ${RP_PROJECT})..."
  body="$("$0" '/launch?page.size=1')"; rc=$?
  case "$rc" in
    0) echo "OK — authenticated and reachable."
       printf '%s' "$body" | head -c 400; echo; exit 0 ;;
    3) errc 3 "ReportPortal unreachable — is the tailnet up and nas:8080 serving?" ;;
    4) errc 4 "ReportPortal rejected the API key (401/403)." ;;
    *) errc "$rc" "ReportPortal query failed." ;;
  esac
fi

PATH_ARG="${1:-}"
[[ -n "$PATH_ARG" ]] || err "no path given. Usage: rp-query.sh [--raw] '/launch?page.size=10'"

# Fixture replay short-circuits everything, including credential checks beyond
# presence, so offline tests need no live server.
if [[ -n "${RP_FIXTURE_DIR:-}" ]]; then
  name="$(printf '%s' "${PATH_ARG#/}" | sed 's/[^A-Za-z0-9._-]/_/g')"
  file="${RP_FIXTURE_DIR}/${name}.json"
  [[ -f "$file" ]] || err "no fixture for path '${PATH_ARG}' (expected ${file})."
  cat "$file"
  exit 0
fi

[[ -n "${RP_API_KEY:-}" ]]  || err "RP_API_KEY is not set."
[[ -n "${RP_ENDPOINT:-}" ]] || err "RP_ENDPOINT is not set."

base="${RP_ENDPOINT%/}"
if [[ "$RAW" -eq 1 ]]; then url="${base}${PATH_ARG}"; else url="${base}/${RP_PROJECT}${PATH_ARG}"; fi

out="$(curl -sS --max-time 45 \
        -H "Authorization: Bearer ${RP_API_KEY}" \
        -H "Accept: application/json" \
        -w $'\n__HTTP_CODE__%{http_code}' \
        "$url" 2>/dev/null)"
curl_rc=$?

# curl 6 (DNS), 7 (connect refused), 28 (timeout), 35 (TLS) all mean "cannot
# reach RP" — which must NEVER be reported as "the tests did not run".
if [[ $curl_rc -ne 0 ]]; then
  errc 3 "cannot reach ReportPortal at ${base} (curl exit ${curl_rc})."
fi

code="${out##*__HTTP_CODE__}"
body="${out%__HTTP_CODE__*}"

case "$code" in
  2*)      printf '%s' "$body"; exit 0 ;;
  401|403) errc 4 "ReportPortal returned HTTP ${code} — API key invalid or lacks project access." ;;
  # Exit 5, NOT 1: a 404/429/5xx means the server said no, which is a wholly
  # different problem from "you forgot to set RP_ENDPOINT". Collapsing the two
  # would send the operator hunting for a missing variable during an RP outage.
  *)       printf '%s' "$body" >&2; errc 5 "ReportPortal returned HTTP ${code}." ;;
esac
```

- [ ] **Step 4: Run test to verify it passes**

```bash
chmod +x docs/routines/test-health/rp-query.sh
./docs/routines/test-health/rp-query.test.sh
```

Expected: `passed: 6  failed: 0`, exit 0.

- [ ] **Step 5: Commit**

```bash
git add docs/routines/test-health/rp-query.sh docs/routines/test-health/rp-query.test.sh
git commit -m "feat(test-health): add ReportPortal REST helper with fixture replay"
```

---

### Task 2: `gh-api.sh` — GitHub helper retargeted to test-signal

**Files:**
- Create: `docs/routines/test-health/gh-api.sh` (copied from `docs/routines/telemetry-anomaly/gh-api.sh`)
- Create: `docs/routines/test-health/gh-api.test.sh`

**Interfaces:**
- Consumes: nothing.
- Produces: `gh-api.sh find-signal '<fingerprint>'` → `{total, matches:[{number,state,state_reason,title,html_url,closed_at}]}`; `gh-api.sh create-issue "<title>" "<labels-csv>" -` reads the body from stdin and prints `{number, html_url, state}`; raw `GET/POST/PATCH/DELETE`. Token resolves as `${GIT_PAT:-${GITHUB_TOKEN:-}}` — unchanged from the original.

- [ ] **Step 1: Write the failing test**

Create `docs/routines/test-health/gh-api.test.sh`:

```bash
#!/usr/bin/env bash
# Offline tests for gh-api.sh. Asserts the search predicate was retargeted from
# telemetry to test-health; does not call GitHub.
set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
G="${HERE}/gh-api.sh"
pass=0; fail=0
check() { if [[ "$2" == "$3" ]]; then pass=$((pass+1)); echo "ok   - $1";
          else fail=$((fail+1)); echo "FAIL - $1: expected [$2] got [$3]"; fi; }

check "script exists" "yes" "$([[ -x "$G" ]] && echo yes || echo no)"

# find-signal must search label:test-health and the test-signal: prefix.
grep -q 'label:test-health' "$G" && r=yes || r=no
check "searches label:test-health" "yes" "$r"
grep -q 'test-signal: ' "$G" && r=yes || r=no
check "searches the test-signal prefix" "yes" "$r"
grep -q 'label:telemetry' "$G" && r=stale || r=clean
check "no leftover telemetry predicate" "clean" "$r"
grep -q 'telemetry-signal' "$G" && r=stale || r=clean
check "no leftover telemetry-signal prefix" "clean" "$r"

# Token fallback must be preserved verbatim.
grep -q 'GIT_PAT:-${GITHUB_TOKEN:-}' "$G" && r=yes || r=no
check "keeps GITHUB_TOKEN fallback" "yes" "$r"

# No token at all is an explicit error.
out="$(env -u GIT_PAT -u GITHUB_TOKEN "$G" GET '/rate_limit' 2>&1)"; rc=$?
check "no token exits nonzero" "1" "$rc"
case "$out" in *token*) r=yes ;; *) r=no ;; esac
check "no token says so" "yes" "$r"

echo "---"; echo "passed: $pass  failed: $fail"
[[ $fail -eq 0 ]]
```

- [ ] **Step 2: Run test to verify it fails**

```bash
chmod +x docs/routines/test-health/gh-api.test.sh
./docs/routines/test-health/gh-api.test.sh
```

Expected: FAIL — `script exists` returns `no`, and every `grep` fails because the file is absent.

- [ ] **Step 3: Write minimal implementation**

```bash
cp docs/routines/telemetry-anomaly/gh-api.sh docs/routines/test-health/gh-api.sh
chmod +x docs/routines/test-health/gh-api.sh
```

Then edit `docs/routines/test-health/gh-api.sh`, replacing the `find-signal` case body so it searches this routine's label and prefix:

```bash
  find-signal)
    # Search issues (any state) whose body carries the exact fingerprint line.
    sig="${1:?fingerprint required, e.g. test-regress:e2e:catalog:1a2b3c4d}"
    q="repo:${REPO} label:test-health in:body \"test-signal: ${sig}\""
    enc="$(jq -rn --arg q "$q" '$q|@uri')"
    emit "$(req GET "/search/issues?q=${enc}&per_page=20")" \
      | jq '{total: .total_count,
             matches: [ .items[] | {number, state,
                state_reason: (.state_reason // null),
                title, html_url, closed_at} ]}'
    ;;
```

Also update the header comment block: replace the three `docs/routines/telemetry-anomaly/gh-api.sh` usage paths with `docs/routines/test-health/gh-api.sh`, change the example fingerprint on the `find-signal` line to `test-regress:e2e:catalog:1a2b3c4d`, change the `create-issue` example labels from `"telemetry,reliability"` / `"telemetry,risk"` to `"test-health,test-regression,harness:todo"`, and change "for the telemetry routine" to "for the test-health routine".

- [ ] **Step 4: Run test to verify it passes**

```bash
./docs/routines/test-health/gh-api.test.sh
```

Expected: `passed: 8  failed: 0`, exit 0.

- [ ] **Step 5: Commit**

```bash
git add docs/routines/test-health/gh-api.sh docs/routines/test-health/gh-api.test.sh
git commit -m "feat(test-health): add GitHub REST helper targeting test-signal fingerprints"
```

---

### Task 3: Digest skeleton — launch inventory and unreachability

**Files:**
- Create: `docs/routines/test-health/test-health-digest.sh`
- Create: `docs/routines/test-health/test-health-digest.test.sh`
- Create: `docs/routines/test-health/fixtures/clean/launch_page.size_300_page.sort_startTime_DESC.json`
- Create: `docs/routines/test-health/fixtures/clean/item_filter.eq.launchId_1_filter.in.status_FAILED_INTERRUPTED_page.size_300.json`

**Interfaces:**
- Consumes: `rp-query.sh` (Task 1), including its fixture-path sanitization.
- Produces: `test-health-digest.sh [--days N] [--state-only]`. Default `--days 7`. Full mode prints a Markdown digest to stdout whose last line is `STATE: <token>`; `--state-only` prints only that line. Exit codes follow the Global Constraints contract. Environment: `RP_FIXTURE_DIR` (replay), `GH_FIXTURE_DIR` (replay GitHub Actions responses, used in Task 4), `TEST_HEALTH_STATE_FILE` (default `$HOME/.cache/test-health/state`).
- Fixture naming for the two RP calls this task makes, exactly as `rp-query.sh` sanitizes them:
  - launches: `launch_page.size_300_page.sort_startTime_DESC.json`
  - failed items of launch `<id>`: `item_filter.eq.launchId_<id>_filter.in.status_FAILED_INTERRUPTED_page.size_300.json`

- [ ] **Step 1: Write the failing test**

Create the clean-week fixtures first. `fixtures/clean/launch_page.size_300_page.sort_startTime_DESC.json` — one E2E launch per module for two consecutive nights is the realistic shape, but two modules is enough to prove the logic; the remaining modules are absent from the baseline and therefore correctly produce no findings:

```json
{
  "content": [
    { "id": 1, "name": "heblo-e2e", "number": 42, "status": "PASSED",
      "startTime": 1785000000000,
      "attributes": [ {"key":"layer","value":"e2e"}, {"key":"module","value":"catalog"},
                      {"key":"branch","value":"main"}, {"key":"ci","value":"1001"} ],
      "statistics": { "executions": { "total": 20, "passed": 20, "failed": 0, "skipped": 0 } } },
    { "id": 2, "name": "heblo-e2e", "number": 41, "status": "PASSED",
      "startTime": 1784913600000,
      "attributes": [ {"key":"layer","value":"e2e"}, {"key":"module","value":"catalog"},
                      {"key":"branch","value":"main"}, {"key":"ci","value":"1000"} ],
      "statistics": { "executions": { "total": 20, "passed": 20, "failed": 0, "skipped": 0 } } }
  ],
  "page": { "number": 1, "size": 300, "totalElements": 2, "totalPages": 1 }
}
```

`fixtures/clean/item_filter.eq.launchId_1_filter.in.status_FAILED_INTERRUPTED_page.size_300.json` and the `_2_` variant are both:

```json
{ "content": [], "page": { "number": 1, "size": 300, "totalElements": 0, "totalPages": 1 } }
```

Create `docs/routines/test-health/test-health-digest.test.sh`:

```bash
#!/usr/bin/env bash
# Offline tests for test-health-digest.sh, driven entirely from fixtures/.
set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
D="${HERE}/test-health-digest.sh"
FIX="${HERE}/fixtures"
pass=0; fail=0
check() { if [[ "$2" == "$3" ]]; then pass=$((pass+1)); echo "ok   - $1";
          else fail=$((fail+1)); echo "FAIL - $1: expected [$2] got [$3]"; fi; }
contains() { case "$2" in *"$1"*) echo yes ;; *) echo no ;; esac; }

export TEST_HEALTH_STATE_FILE="$(mktemp)"
trap 'rm -f "$TEST_HEALTH_STATE_FILE"' EXIT
export RP_API_KEY=dummy RP_ENDPOINT=http://fixture.invalid/api/v1 RP_PROJECT=heblo

# Pin "now" so the fixtures' absolute timestamps stay inside the rolling window
# forever. 1785030000000 sits ~8h after the newest clean-week launch, which also
# keeps it inside the 26h E2E freshness horizon Task 4 checks.
export TEST_HEALTH_NOW_MS=1785030000000

# --- clean week: launches present, nothing failing -> zero findings ---
out="$(RP_FIXTURE_DIR="${FIX}/clean" "$D" --days 7 2>&1)"; rc=$?
check "clean week exits 0" "0" "$rc"
check "clean week reports the inventory" "yes" "$(contains 'heblo-e2e' "$out")"
check "clean week has no findings" "yes" "$(contains 'FINDINGS: 0' "$out")"
check "clean week emits a state line" "yes" "$(contains 'STATE: ' "$out")"

# --- --state-only prints the state line and nothing else ---
out="$(RP_FIXTURE_DIR="${FIX}/clean" "$D" --days 7 --state-only 2>&1)"; rc=$?
check "state-only exits 0" "0" "$rc"
lines="$(printf '%s\n' "$out" | grep -c .)"
check "state-only prints one line" "1" "$lines"
check "state-only line is the state" "yes" "$(contains 'STATE: ' "$out")"

# --- state is stable across runs on identical input ---
a="$(RP_FIXTURE_DIR="${FIX}/clean" "$D" --days 7 --state-only 2>&1)"
b="$(RP_FIXTURE_DIR="${FIX}/clean" "$D" --days 7 --state-only 2>&1)"
check "state is deterministic" "$a" "$b"

# --- RP unreachable must NOT be reported as missing test data ---
out="$(RP_FIXTURE_DIR=/nonexistent-fixture-dir "$D" --days 7 2>&1)"; rc=$?
check "unreachable/failed fetch is nonzero" "yes" "$([[ $rc -ne 0 ]] && echo yes || echo no)"
check "unreachable files no silence findings" "no" "$(contains 'test-silence:' "$out")"

# --- consecutive-error-day counter escalates; success clears it ---
esf="$(mktemp)"; rm -f "$esf"
out="$(TEST_HEALTH_STATE_FILE="$esf" RP_FIXTURE_DIR=/nonexistent "$D" --days 7 2>&1)"
check "first error day counts 1" "yes" "$(contains 'errdays=1' "$out")"

out="$(TEST_HEALTH_STATE_FILE="$esf" RP_FIXTURE_DIR=/nonexistent "$D" --days 7 2>&1)"
check "same-day rerun does not double count" "yes" "$(contains 'errdays=1' "$out")"

y="$(date -u -v-1d +%Y-%m-%d 2>/dev/null || date -u -d 'yesterday' +%Y-%m-%d)"
printf 'lastErrorDate=%s\nerrDays=4\n' "$y" > "$esf"
out="$(TEST_HEALTH_STATE_FILE="$esf" RP_FIXTURE_DIR=/nonexistent "$D" --days 7 2>&1)"
check "a consecutive day escalates the count" "yes" "$(contains 'errdays=5' "$out")"

out="$(TEST_HEALTH_STATE_FILE="$esf" RP_FIXTURE_DIR="${FIX}/clean" "$D" --days 7 2>&1)"
check "a successful run reports errdays=0" "yes" "$(contains 'errdays=0' "$out")"
check "a successful run clears the state file" "0" "$(sed -n 's/^errDays=//p' "$esf" | head -1)"
rm -f "$esf"

# --- partial outage: launches readable, per-launch item fetch fails ---
# The realistic shape of a half-up ReportPortal. This must take the SAME error
# path as a total outage: a STATE line, an incremented day counter, and above
# all no silence findings — never "the tests did not run".
partial="$(mktemp -d)"
cp "${FIX}/clean/launch_page.size_300_page.sort_startTime_DESC.json" "$partial/"
# deliberately omit the item_* fixtures so every per-launch fetch fails
esf2="$(mktemp)"; rm -f "$esf2"
out="$(TEST_HEALTH_STATE_FILE="$esf2" RP_FIXTURE_DIR="$partial" "$D" --days 7 2>&1)"; rc=$?
check "item-fetch failure exits nonzero" "yes" "$([[ $rc -ne 0 ]] && echo yes || echo no)"
check "item-fetch failure emits a state line" "yes" "$(contains 'STATE: error=' "$out")"
check "item-fetch failure counts an error day" "yes" "$(contains 'errdays=1' "$out")"
check "item-fetch failure files no silence findings" "no" "$(contains 'test-silence:' "$out")"
rm -rf "$partial"; rm -f "$esf2"

echo "---"; echo "passed: $pass  failed: $fail"
[[ $fail -eq 0 ]]
```

- [ ] **Step 2: Run test to verify it fails**

```bash
chmod +x docs/routines/test-health/test-health-digest.test.sh
./docs/routines/test-health/test-health-digest.test.sh
```

Expected: FAIL on every check — `test-health-digest.sh` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `docs/routines/test-health/test-health-digest.sh`:

```bash
#!/usr/bin/env bash
#
# test-health-digest.sh — deterministic test-health digest from ReportPortal.
#
# The gathering half of the test-health routine: queries ReportPortal for the
# launches in a window plus their failed items, computes findings, and prints a
# Markdown digest ending in a machine-readable STATE line.
#
# Absence of expected data is computed HERE, not left to the agent: a nightly
# that never ran is the failure mode this routine exists to catch.
#
# Usage:
#   test-health-digest.sh [--days N] [--state-only]
#
# Env: RP_API_KEY / RP_ENDPOINT / RP_PROJECT (see rp-query.sh),
#      RP_FIXTURE_DIR, GH_FIXTURE_DIR (offline replay),
#      TEST_HEALTH_STATE_FILE (default $HOME/.cache/test-health/state)
#
# Exit codes: 0 ok | 1 config error | 3 RP unreachable | 4 RP auth rejected
#
set -uo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RP="${HERE}/rp-query.sh"
DAYS=7
STATE_ONLY=0
STATE_FILE="${TEST_HEALTH_STATE_FILE:-$HOME/.cache/test-health/state}"

err()  { echo "Error: $*" >&2; exit 1; }
errc() { local c="$1"; shift; echo "Error: $*" >&2; exit "$c"; }

command -v jq >/dev/null || err "jq is required (expected on PATH, e.g. /Users/rem/.local/bin)."
[[ -x "$RP" ]] || err "${RP} not found or not executable."

while [[ $# -gt 0 ]]; do
  case "$1" in
    --days)       DAYS="${2:?--days requires a number}"; shift 2 ;;
    --state-only) STATE_ONLY=1; shift ;;
    # Print the leading comment block, whatever length it has grown to — a
    # hardcoded line range silently starts printing code as the header changes.
    -h|--help)    awk 'NR>1 && /^#/ {sub(/^# ?/,""); print; next} NR>1 {exit}' "$0"; exit 0 ;;
    *)            err "unknown argument '$1'." ;;
  esac
done

# BSD date first (macOS host), GNU date as fallback. Note the two flavours
# disagree on -r: BSD reads it as an epoch, GNU as a file mtime, so a bare
# `date -r <epoch>` fails silently on Linux rather than falling through.
fmt_epoch() { # fmt_epoch <epoch-seconds> -> ISO-8601 UTC
  date -u -r "$1" +%Y-%m-%dT%H:%M:%SZ 2>/dev/null \
    || date -u -d "@$1" +%Y-%m-%dT%H:%M:%SZ 2>/dev/null \
    || echo "epoch:$1"
}
day_offset() { # day_offset N -> the UTC date N days ago as YYYY-MM-DD
  date -u -v-"${1}"d +%Y-%m-%d 2>/dev/null || date -u -d "${1} days ago" +%Y-%m-%d 2>/dev/null \
    || err "neither BSD nor GNU date available."
}

TODAY="$(date -u +%Y-%m-%d)"
mkdir -p "$(dirname "$STATE_FILE")" 2>/dev/null || true

state_field() { # state_field <key> -> value from the state file, or empty
  [[ -f "$STATE_FILE" ]] || return 0
  sed -n "s/^$1=//p" "$STATE_FILE" | head -1
}

# A routine that cannot run must get LOUDER, not quieter. telemetry-anomaly
# reported "done" every day for weeks while entirely broken, because an
# unchanged state produced an identical, deduplicated non-event. Counting
# consecutive error days makes the state change daily, so harness dispatches
# a fresh, escalating task instead of swallowing it.
record_error_and_exit() { # record_error_and_exit <exit-code> <message>
  local code="$1" msg="$2" prev_date prev_n yesterday n
  prev_date="$(state_field lastErrorDate)"
  prev_n="$(state_field errDays)"; [[ -n "$prev_n" ]] || prev_n=0
  yesterday="$(day_offset 1)"
  if [[ "$prev_date" == "$TODAY" ]]; then n="$prev_n"          # re-run same day
  elif [[ "$prev_date" == "$yesterday" ]]; then n=$((prev_n + 1))
  else n=1; fi
  printf 'lastErrorDate=%s\nerrDays=%s\n' "$TODAY" "$n" > "$STATE_FILE" 2>/dev/null || true
  echo "STATE: error=${code}:errdays=${n}"
  echo "Error: ${msg} (exit ${code}, consecutive error days: ${n})" >&2
  exit "$code"
}

clear_error_days() {
  printf 'lastErrorDate=\nerrDays=0\n' > "$STATE_FILE" 2>/dev/null || true
}

# "Now" is overridable so that fixtures can pin it. Fixtures carry absolute
# timestamps; a window computed from wall-clock now would slide past them within
# days and the suite would rot into a false pass — launches silently filtered
# out, zero findings, everything "green". Tests set TEST_HEALTH_NOW_MS.
NOW_MS="${TEST_HEALTH_NOW_MS:-$(( $(date -u +%s) * 1000 ))}"
WINDOW_START_MS=$(( NOW_MS - DAYS * 86400 * 1000 ))

# ---------------------------------------------------------------- gather ----
# One page of 300 launches, newest first, then filter to the window with jq.
# Filtering client-side keeps us off RP's filter-parameter grammar, which is
# version-sensitive and unverified against this instance.
raw_launches="$("$RP" '/launch?page.size=300&page.sort=startTime,DESC')"; rc=$?
if [[ $rc -ne 0 ]]; then
  # 3 = unreachable, 4 = auth. Either way we know NOTHING about test presence,
  # so we must not emit silence findings. Record the error day and stop.
  record_error_and_exit "$rc" "could not read launches from ReportPortal"
fi

launches="$(printf '%s' "$raw_launches" | jq --argjson since "$WINDOW_START_MS" '
  [ .content[]?
    | select(.startTime >= $since)
    | { id, name, number, status, startTime,
        layer:  ((.attributes[]? | select(.key=="layer")  | .value) // "unknown"),
        module: ((.attributes[]? | select(.key=="module") | .value) // "-"),
        branch: ((.attributes[]? | select(.key=="branch") | .value) // "unknown"),
        ci:     ((.attributes[]? | select(.key=="ci")     | .value) // "-"),
        total:  (.statistics.executions.total   // 0),
        passed: (.statistics.executions.passed  // 0),
        failed: (.statistics.executions.failed  // 0),
        skipped:(.statistics.executions.skipped // 0) }
    | select(.branch == "main" or .branch == "unknown") ]
')" || err "could not parse the launch payload as JSON."

# Failed items per launch. Cheap: only failures are fetched, never full item
# lists. Pass/fail history per test is then derived from set membership.
failed_items='[]'
for id in $(printf '%s' "$launches" | jq -r '.[].id'); do
  p="/item?filter.eq.launchId=${id}&filter.in.status=FAILED,INTERRUPTED&page.size=300"
  # MUST route through record_error_and_exit, exactly like the launches call.
  # A partial RP outage — launch list serving, item queries failing — is a real
  # shape, and taking the plain errc() path here would print no STATE line and
  # leave the error-day counter frozen, so the scheduler would dedup the failure
  # into silence. That is the precise failure this counter exists to prevent.
  body="$("$RP" "$p")" || record_error_and_exit $? "could not read failed items for launch ${id}"
  chunk="$(printf '%s' "$body" | jq --argjson lid "$id" '
    [ .content[]? | { launchId: $lid,
                      name: (.name // "unknown"),
                      path: (.pathNames.itemPaths // [] | map(.name) | join(" > ")),
                      status: (.status // "FAILED"),
                      issue: ((.issue.comment // "") ),
                      error: ((.description // "") | split("\n")[0] // "") } ]
  ')" || err "could not parse items for launch ${id}."
  failed_items="$(jq -n --argjson a "$failed_items" --argjson b "$chunk" '$a + $b')"
done

# --------------------------------------------------------------- findings ---
# Findings are accumulated as JSON objects: {category, layer, module, fingerprint, headline, detail}
findings='[]'
add_finding() { findings="$(jq -n --argjson a "$findings" --argjson b "$1" '$a + [$b]')"; }

# (Tasks 4 and 5 append presence, shrink, regression, flaky and chronic
# detection here. Task 3 establishes inventory and plumbing only.)

# ------------------------------------------------------------------ state ---
finding_count="$(printf '%s' "$findings" | jq 'length')"
state_body="$(printf '%s' "$findings" | jq -S -c '[ .[] | .fingerprint ] | sort')"
state_hash="$(printf '%s' "$state_body" | shasum -a 256 | cut -c1-16)"

# Reaching here means the gather succeeded, so the error streak is over.
clear_error_days
STATE="findings=${finding_count}:${state_hash}:errdays=0"

if [[ "$STATE_ONLY" -eq 1 ]]; then
  echo "STATE: ${STATE}"
  exit 0
fi

# ----------------------------------------------------------------- output ---
launch_count="$(printf '%s' "$launches" | jq 'length')"

cat <<EOF
# Test-health digest

- **Window:** last ${DAYS} days (since $(fmt_epoch $((WINDOW_START_MS/1000))))
- **Generated:** $(date -u +%Y-%m-%dT%H:%M:%SZ)
- **Source:** ReportPortal project ${RP_PROJECT:-heblo}
- **Launches in window:** ${launch_count}

> Deterministic digest. Absence of expected data is computed here and appears
> as a finding — do not infer it yourself, and do not treat a missing section
> as "everything is fine".

## 1. Launch inventory

EOF

printf '%s' "$launches" | jq -r '
  if length == 0 then "_(no launches in window)_"
  else
    "| launch | layer | module | # | status | total | passed | failed | skipped | started |",
    "| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |",
    ( sort_by(.layer, .module, -.startTime)[]
      | "| \(.name) | \(.layer) | \(.module) | \(.number) | \(.status) | \(.total) | \(.passed) | \(.failed) | \(.skipped) | \(.startTime) |" )
  end'

echo
echo "## 2. Failed items in window"
echo
printf '%s' "$failed_items" | jq -r '
  if length == 0 then "_(none)_"
  else
    "| launch | test | error (first line) |",
    "| --- | --- | --- |",
    ( .[] | "| \(.launchId) | \(.path) > \(.name) | \(.error | .[0:160]) |" )
  end'

echo
echo "## 3. Findings"
echo
echo "FINDINGS: ${finding_count}"
echo
printf '%s' "$findings" | jq -r '
  if length == 0 then "_(none — nothing to file)_"
  else ( .[] | "- **\(.category)** `\(.fingerprint)` — \(.headline)\n  \(.detail)" )
  end'

echo
echo "STATE: ${STATE}"
```

- [ ] **Step 4: Run test to verify it passes**

```bash
chmod +x docs/routines/test-health/test-health-digest.sh
./docs/routines/test-health/test-health-digest.test.sh
```

Expected: `passed: 19  failed: 0`, exit 0.

- [ ] **Step 5: Commit**

```bash
git add docs/routines/test-health/test-health-digest.sh \
        docs/routines/test-health/test-health-digest.test.sh \
        docs/routines/test-health/fixtures
git commit -m "feat(test-health): add digest skeleton with launch inventory and state line"
```

---

### Task 4: Presence assertions, CI cross-check, cascade suppression

**Files:**
- Modify: `docs/routines/test-health/test-health-digest.sh` (replace the Task 3 placeholder comment in the findings section)
- Modify: `docs/routines/test-health/test-health-digest.test.sh` (append cases)
- Create: `docs/routines/test-health/fixtures/silent-module/` (launch fixture missing one module)
- Create: `docs/routines/test-health/fixtures/ci-broken/` (whole E2E layer missing + failed workflow run)

**Interfaces:**
- Consumes: `launches` and `add_finding` from Task 3.
- Produces: findings with `category` in `schedule-broken | rp-reporting-broken | ci-broken`, fingerprints `test-silence:<layer>:<module>:schedule`, `test-silence:<layer>:<module>:reporting`, `test-ci:<workflow>:<failing-step>`. GitHub Actions responses are fetched via `gh-api.sh GET` and replayed from `GH_FIXTURE_DIR` when set, using the same sanitization rule as `rp-query.sh`.

- [ ] **Step 1: Write the failing test**

Create `fixtures/silent-module/`: copy `fixtures/clean/*` but give the two launches `module: catalog` and `module: transport`, with `startTime` **3 days before `TEST_HEALTH_NOW_MS`** for `transport` and **within 26h** for `catalog`, so `transport` has a baseline but no recent launch. Add the matching empty `item_...` fixtures for both launch ids.

The 3-day value is load-bearing and must sit strictly between the two horizons. A module only earns an expectation by appearing *inside* the window, so a launch older than `--days 7` is filtered out before the presence logic ever sees it and produces no finding at all — correctly, under the "no baseline, no claim" rule. It must also be older than the 26h E2E freshness horizon to read as stale. Anything in `(26h, 7 days]` works; 3 days is comfortably clear of both edges.

Create `fixtures/ci-broken/`: launches contain only `heblo-backend` (E2E entirely absent, but present in the prior baseline via a launch older than 26h), plus a GitHub fixture for the workflow-runs query whose newest run has `"conclusion": "failure"` and a failed step named `🚀 Deploy main to Staging`.

Append to `test-health-digest.test.sh`, before the summary lines:

```bash
# --- one module silent, others fine -> exactly one silence finding ---
out="$(RP_FIXTURE_DIR="${FIX}/silent-module" GH_FIXTURE_DIR="${FIX}/silent-module" "$D" --days 7 2>&1)"
check "silent module is detected" "yes" "$(contains 'test-silence:e2e:transport' "$out")"
check "healthy module is not flagged" "no" "$(contains 'test-silence:e2e:catalog' "$out")"
n="$(printf '%s\n' "$out" | grep -c 'test-silence:')"
check "exactly one silence finding" "1" "$n"

# --- whole layer down due to CI -> ONE ci-broken finding, not eleven ---
out="$(RP_FIXTURE_DIR="${FIX}/ci-broken" GH_FIXTURE_DIR="${FIX}/ci-broken" "$D" --days 7 2>&1)"
check "ci-broken is detected" "yes" "$(contains 'test-ci:' "$out")"
check "ci-broken names the failing step" "yes" "$(contains 'Deploy main to Staging' "$out")"
n="$(printf '%s\n' "$out" | grep -c 'test-silence:')"
check "cascade suppressed: no per-module silence" "0" "$n"
```

- [ ] **Step 2: Run test to verify it fails**

```bash
./docs/routines/test-health/test-health-digest.test.sh
```

Expected: the six new checks FAIL (no silence or ci findings are produced yet); the nineteen Task 3 checks still pass.

- [ ] **Step 3: Write minimal implementation**

In `test-health-digest.sh`, add a GitHub fetch helper immediately after the `epoch_days_ago` function:

```bash
GH="${HERE}/gh-api.sh"

# Fetch a GitHub REST path, honouring GH_FIXTURE_DIR for offline replay. Uses
# the same file-naming rule as rp-query.sh so fixtures are predictable.
gh_get() {
  local p="$1"
  if [[ -n "${GH_FIXTURE_DIR:-}" ]]; then
    local name file
    name="$(printf '%s' "${p#/}" | sed 's/[^A-Za-z0-9._-]/_/g')"
    file="${GH_FIXTURE_DIR}/${name}.json"
    [[ -f "$file" ]] && cat "$file" || echo '{"workflow_runs":[]}'
    return 0
  fi
  [[ -x "$GH" ]] || { echo '{"workflow_runs":[]}'; return 0; }
  "$GH" GET "$p" 2>/dev/null || echo '{"workflow_runs":[]}'
}
```

Then replace the Task 3 placeholder comment with the presence logic:

```bash
# --- presence: which layer/module reported recently vs. has a baseline? ---
# A layer/module earns an expectation by having reported at least once in the
# window. E2E is nightly, so its freshness horizon is 26h; the push-triggered
# layers get the full window (a quiet week on main is not a fault).
E2E_FRESH_MS=$(( 26 * 3600 * 1000 ))

expected="$(printf '%s' "$launches" | jq --argjson now "$NOW_MS" --argjson fresh "$E2E_FRESH_MS" '
  group_by(.layer + "/" + .module)
  | map({ layer: .[0].layer, module: .[0].module,
          newest: (map(.startTime) | max),
          runs: length })
  | map(. + { stale: (if .layer == "e2e" then (($now - .newest) > $fresh) else false end) })
')"

stale_e2e="$(printf '%s' "$expected" | jq '[ .[] | select(.stale) ] | length')"
fresh_e2e="$(printf '%s' "$expected" | jq '[ .[] | select(.layer=="e2e" and (.stale|not)) ] | length')"

# CI cross-check, run once: did the nightly workflow fail before the tests?
nightly='{"workflow_runs":[]}'
if [[ "$stale_e2e" -gt 0 ]]; then
  nightly="$(gh_get "/repos/onpaj/Anela.Heblo/actions/workflows/e2e-nightly-regression.yml/runs?per_page=5")"
fi
nightly_concl="$(printf '%s' "$nightly" | jq -r '.workflow_runs[0].conclusion // "none"')"
nightly_id="$(printf '%s' "$nightly" | jq -r '.workflow_runs[0].id // empty')"

failing_step="unknown"
if [[ "$nightly_concl" == "failure" && -n "$nightly_id" ]]; then
  jobs="$(gh_get "/repos/onpaj/Anela.Heblo/actions/runs/${nightly_id}/jobs")"
  failing_step="$(printf '%s' "$jobs" | jq -r '
    [ .jobs[]?.steps[]? | select(.conclusion=="failure") | .name ] | first // "unknown"')"
fi

# Cascade rule: if EVERY e2e module is stale and the workflow failed, this is
# one infrastructure fault, not eleven missing modules. File once.
if [[ "$stale_e2e" -gt 0 && "$fresh_e2e" -eq 0 && "$nightly_concl" == "failure" ]]; then
  add_finding "$(jq -n --arg s "$failing_step" --argjson n "$stale_e2e" '{
    category: "ci-broken",
    layer: "e2e", module: "-",
    fingerprint: ("test-ci:e2e-nightly-regression.yml:" + $s),
    headline: ("E2E nightly failed at step \"" + $s + "\" — no tests ran, " + ($n|tostring) + " modules have no data"),
    detail: "The workflow run failed before the test step, so ReportPortal received nothing. Resolution is outside the repository (credential/config), not a code change."
  }')"
else
  # Otherwise report each stale module individually, distinguishing "the
  # workflow never ran" from "it ran fine but reporting did not arrive".
  for row in $(printf '%s' "$expected" | jq -r '.[] | select(.stale) | @base64'); do
    d="$(printf '%s' "$row" | base64 --decode)"
    l="$(printf '%s' "$d" | jq -r '.layer')"
    m="$(printf '%s' "$d" | jq -r '.module')"
    if [[ "$nightly_concl" == "success" ]]; then
      cat_name="rp-reporting-broken"; suffix="reporting"
      head_txt="${l}/${m}: workflow succeeded but no ReportPortal launch arrived"
      det="The nightly run completed successfully, so the tests ran — the reporting agent or the tailnet hop failed."
    else
      cat_name="schedule-broken"; suffix="schedule"
      head_txt="${l}/${m}: no launch in the last 26h despite a 7-day baseline"
      det="No recent launch and no successful workflow run explains it. The nightly may not have been scheduled at all."
    fi
    add_finding "$(jq -n --arg c "$cat_name" --arg l "$l" --arg m "$m" --arg s "$suffix" \
                          --arg h "$head_txt" --arg dt "$det" '{
      category: $c, layer: $l, module: $m,
      fingerprint: ("test-silence:" + $l + ":" + $m + ":" + $s),
      headline: $h, detail: $dt }')"
  done
fi
```

- [ ] **Step 4: Run test to verify it passes**

```bash
./docs/routines/test-health/test-health-digest.test.sh
```

Expected: `passed: 25  failed: 0`, exit 0.

- [ ] **Step 5: Commit**

```bash
git add docs/routines/test-health/test-health-digest.sh \
        docs/routines/test-health/test-health-digest.test.sh \
        docs/routines/test-health/fixtures
git commit -m "feat(test-health): detect missing test data with CI cross-check and cascade suppression"
```

---

### Task 5: Suite shrink, regression, flaky and chronic detection

**Files:**
- Modify: `docs/routines/test-health/test-health-digest.sh` (append to the findings section)
- Modify: `docs/routines/test-health/test-health-digest.test.sh` (append cases)
- Create: `docs/routines/test-health/fixtures/shrank/`, `fixtures/regression/`, `fixtures/flaky/`

**Interfaces:**
- Consumes: `launches`, `failed_items`, `add_finding`.
- Produces: findings with `category` in `suite-shrank | regression | flaky | chronic` and fingerprints `test-shrink:<layer>:<module>`, `test-regress:<layer>:<module>:<hash8>`, `test-flaky:<layer>:<module>:<test-path>`, `test-chronic:<layer>:<module>:<hash8>`. `<hash8>` is the first 8 hex chars of the sha256 of the normalized error line.

Detection is set-membership based and needs no extra API calls: a test that appears in launch L's failed-item list failed in L; a test absent from that list in a launch that ran passed in it. For a module with `n` launches in the window and a test failing in `k` of them: `k == n` → chronic; failing only in the newest 2 consecutive launches and in none before → regression; otherwise `0 < k < n` with ≥2 status flips and pass rate `(n-k)/n` in `[0.2, 0.8]` → flaky.

- [ ] **Step 1: Write the failing test**

Create the three fixture sets:
- `fixtures/shrank/` — one module, two launches, `statistics.executions.total` of `20` on the older three and `14` on the newest (30% drop, both `PASSED`).
- `fixtures/regression/` — one module, four nightly launches; the newest two both list the same failed item `catalog.spec.ts > filters by product type` with error `TimeoutError: locator.click: Timeout 30000ms exceeded`, the older two list none.
- `fixtures/flaky/` — one module, four launches; the same test fails in launches 1 and 3 only (pass rate 50%, 2+ flips).

Append to `test-health-digest.test.sh`:

```bash
# --- suite shrank ---
out="$(RP_FIXTURE_DIR="${FIX}/shrank" GH_FIXTURE_DIR="${FIX}/shrank" "$D" --days 7 2>&1)"
check "shrink is detected" "yes" "$(contains 'test-shrink:e2e:catalog' "$out")"

# --- genuine regression: failing the newest two nights, clean before ---
out="$(RP_FIXTURE_DIR="${FIX}/regression" GH_FIXTURE_DIR="${FIX}/regression" "$D" --days 7 2>&1)"
check "regression is detected" "yes" "$(contains 'test-regress:e2e:catalog:' "$out")"
check "regression is not called flaky" "no" "$(contains 'test-flaky:' "$out")"

# --- flaky: alternating pass/fail across the window ---
out="$(RP_FIXTURE_DIR="${FIX}/flaky" GH_FIXTURE_DIR="${FIX}/flaky" "$D" --days 7 2>&1)"
check "flaky is detected" "yes" "$(contains 'test-flaky:e2e:catalog' "$out")"
check "flaky is not called a regression" "no" "$(contains 'test-regress:' "$out")"

# --- fingerprints are stable and collision-free ---
a="$(RP_FIXTURE_DIR="${FIX}/regression" GH_FIXTURE_DIR="${FIX}/regression" "$D" --days 7 2>&1 | grep -o 'test-regress:[^ `]*' | head -1)"
b="$(RP_FIXTURE_DIR="${FIX}/regression" GH_FIXTURE_DIR="${FIX}/regression" "$D" --days 7 2>&1 | grep -o 'test-regress:[^ `]*' | head -1)"
check "fingerprint is stable across runs" "$a" "$b"

# --- the cap is enforced and stated, never silent ---
out="$(RP_FIXTURE_DIR="${FIX}/regression" GH_FIXTURE_DIR="${FIX}/regression" "$D" --days 7 2>&1)"
check "digest states the cap" "yes" "$(contains 'CAP: 5' "$out")"
```

- [ ] **Step 2: Run test to verify it fails**

```bash
./docs/routines/test-health/test-health-digest.test.sh
```

Expected: the seven new checks FAIL; the twenty-five prior checks still pass.

- [ ] **Step 3: Write minimal implementation**

Append to the findings section of `test-health-digest.sh`, after the presence block:

```bash
# --- suite shrink: newest run materially smaller than the window median ---
SHRINK_PCT=20
for key in $(printf '%s' "$launches" | jq -r '[ .[] | .layer + "/" + .module ] | unique | .[]'); do
  l="${key%%/*}"; m="${key##*/}"
  series="$(printf '%s' "$launches" | jq --arg l "$l" --arg m "$m" \
    '[ .[] | select(.layer==$l and .module==$m) ] | sort_by(-.startTime)')"
  n="$(printf '%s' "$series" | jq 'length')"
  [[ "$n" -ge 3 ]] || continue   # no baseline, no claim
  newest="$(printf '%s' "$series" | jq '.[0].total')"
  median="$(printf '%s' "$series" | jq '[ .[1:][].total ] | sort | .[ (length/2 | floor) ]')"
  [[ "$median" -gt 0 ]] || continue
  drop=$(( (median - newest) * 100 / median ))
  if [[ "$drop" -ge "$SHRINK_PCT" ]]; then
    add_finding "$(jq -n --arg l "$l" --arg m "$m" --argjson nw "$newest" --argjson md "$median" --argjson d "$drop" '{
      category: "suite-shrank", layer: $l, module: $m,
      fingerprint: ("test-shrink:" + $l + ":" + $m),
      headline: ($l + "/" + $m + ": test count fell " + ($d|tostring) + "% (" + ($md|tostring) + " -> " + ($nw|tostring) + ")"),
      detail: "The launch still succeeded, so tests were skipped or a fixture aborted a spec early rather than failing."
    }')"
  fi
done

# --- per-test history from failed-item set membership ---
# normalize: strip line/col numbers, hex ids, timestamps and ms durations so the
# same root cause hashes identically across runs.
normalize_error() {
  printf '%s' "$1" \
    | sed -E 's/[0-9]+ms/<ms>/g; s/:[0-9]+:[0-9]+/:<pos>/g; s/[0-9a-f]{8,}/<id>/gi; s/[0-9]{4}-[0-9]{2}-[0-9]{2}[T ][0-9:.]+/<ts>/g; s/[0-9]+/<n>/g' \
    | cut -c1-200
}

CAP=5
for key in $(printf '%s' "$launches" | jq -r '[ .[] | .layer + "/" + .module ] | unique | .[]'); do
  l="${key%%/*}"; m="${key##*/}"
  ids="$(printf '%s' "$launches" | jq -r --arg l "$l" --arg m "$m" \
    '[ .[] | select(.layer==$l and .module==$m) ] | sort_by(-.startTime) | .[].id')"
  n="$(printf '%s\n' "$ids" | grep -c .)"
  [[ "$n" -ge 2 ]] || continue

  tests="$(printf '%s' "$failed_items" | jq -r --argjson ids "$(printf '%s\n' "$ids" | jq -R . | jq -s 'map(tonumber)')" \
    '[ .[] | select(.launchId as $x | $ids | index($x)) | (.path + " > " + .name) ] | unique | .[]')"

  newest_two="$(printf '%s\n' "$ids" | head -2)"
  while IFS= read -r t; do
    [[ -n "$t" ]] || continue
    k=0; recent_fails=0
    for id in $ids; do
      hit="$(printf '%s' "$failed_items" | jq --argjson i "$id" --arg t "$t" \
        '[ .[] | select(.launchId==$i and (.path + " > " + .name)==$t) ] | length')"
      [[ "$hit" -gt 0 ]] && k=$((k+1))
      case "$newest_two" in *"$id"*) [[ "$hit" -gt 0 ]] && recent_fails=$((recent_fails+1)) ;; esac
    done
    err_line="$(printf '%s' "$failed_items" | jq -r --arg t "$t" \
      'map(select((.path + " > " + .name)==$t)) | .[0].error // ""')"
    norm="$(normalize_error "$err_line")"
    hash8="$(printf '%s' "$norm" | shasum -a 256 | cut -c1-8)"
    pass_pct=$(( (n - k) * 100 / n ))

    if [[ "$k" -eq "$n" && "$n" -ge 7 ]]; then
      add_finding "$(jq -n --arg l "$l" --arg m "$m" --arg h "$hash8" --arg t "$t" --arg e "$err_line" '{
        category: "chronic", layer: $l, module: $m,
        fingerprint: ("test-chronic:" + $l + ":" + $m + ":" + $h),
        headline: ($l + "/" + $m + ": \"" + $t + "\" has failed every run for a week"),
        detail: ("First error line: " + $e) }')"
    elif [[ "$recent_fails" -eq 2 && "$k" -eq 2 ]]; then
      add_finding "$(jq -n --arg l "$l" --arg m "$m" --arg h "$hash8" --arg t "$t" --arg e "$err_line" '{
        category: "regression", layer: $l, module: $m,
        fingerprint: ("test-regress:" + $l + ":" + $m + ":" + $h),
        headline: ($l + "/" + $m + ": \"" + $t + "\" newly fails two runs running"),
        detail: ("Passed earlier in the window, now failing. First error line: " + $e) }')"
    elif [[ "$k" -gt 0 && "$k" -lt "$n" && "$pass_pct" -ge 20 && "$pass_pct" -le 80 ]]; then
      add_finding "$(jq -n --arg l "$l" --arg m "$m" --arg t "$t" --argjson p "$pass_pct" --arg e "$err_line" '{
        category: "flaky", layer: $l, module: $m,
        fingerprint: ("test-flaky:" + $l + ":" + $m + ":" + $t),
        headline: ($l + "/" + $m + ": \"" + $t + "\" is flaky (" + ($p|tostring) + "% pass rate)"),
        detail: ("Alternates within the window with no consistent outcome. First error line: " + $e) }')"
    fi
  done <<< "$tests"
done

# Priority order for the cap, so infrastructure faults are never crowded out by
# a pile of flaky tests.
findings="$(printf '%s' "$findings" | jq '
  def rank: { "ci-broken":0, "schedule-broken":1, "rp-reporting-broken":2,
              "regression":3, "suite-shrank":4, "flaky":5, "chronic":6 }[.category] // 9;
  sort_by(rank)')"
```

Then, in the output section, replace the `FINDINGS:` line block so the cap is explicit:

```bash
echo "FINDINGS: ${finding_count}"
echo "CAP: ${CAP} (file at most this many issues; report every finding you skip and why)"
```

- [ ] **Step 4: Run test to verify it passes**

```bash
./docs/routines/test-health/test-health-digest.test.sh
```

Expected: `passed: 32  failed: 0`, exit 0.

- [ ] **Step 5: Commit**

```bash
git add docs/routines/test-health/test-health-digest.sh \
        docs/routines/test-health/test-health-digest.test.sh \
        docs/routines/test-health/fixtures
git commit -m "feat(test-health): detect suite shrink, regressions, flakiness and chronic failures"
```

---

### Task 6: Routine README — the rules the agent reads

**Files:**
- Create: `docs/routines/test-health/README.md`

**Interfaces:**
- Consumes: nothing at runtime.
- Produces: the document the agent prompt (Task 7) references by path. It must state, in full: the flag/skip rules, the fingerprint table, the dedup decision table, the volume cap, the issue contract including the fix contract text, and the thresholds table.

- [ ] **Step 1: Write the README**

Create `docs/routines/test-health/README.md` containing, in this order:

1. **Purpose** — one paragraph, and the eight-night nightly outage as the motivating example.
2. **Files table** — the seven files in this folder and what each does.
3. **Routine details table** — schedule `30 5 * * *` UTC (07:30 Prague), model `sonnet`, repo `onpaj/Anela.Heblo`, window 7 days.
4. **Thresholds** — copied verbatim from the spec, as a table so they can be tuned here:

   | Threshold | Value |
   |---|---|
   | Suite-shrink trigger | ≥20% below 7-day median |
   | Regression trigger | 2 consecutive failing E2E runs (1 for backend/frontend) |
   | Flaky trigger | ≥2 status flips, pass rate 20–80% |
   | Chronic trigger | failing every run for ≥7 days |
   | E2E freshness horizon | 26h |
   | Issues per run | ≤5 |

5. **What it flags** — the seven categories with their rules, copied from the spec.
6. **What it skips** — RP status `SKIPPED`, non-`main` branches, fixes merged in-window, matching open issues, issues closed `not_planned`, layer/modules with no baseline, and the cascade rule.
7. **Fingerprint table** — all seven fingerprints exactly as in the spec.
8. **Dedup decision table** — the four rows from the spec.
9. **Issue contract** — title format, label mapping table, first-line fingerprint rule, required body sections.
10. **The fix contract** — reproduce the block verbatim from the spec, including "A PR that reduces the total test count for this module is wrong by construction," and the `test-infra` clause stating that resolution needs a human.
11. **Running it by hand** — including the dry-run:

```bash
export RP_API_KEY=...            # from the ReportPortal UI
export RP_ENDPOINT=http://nas.tail0cdb23.ts.net:8080/api/v1
export RP_PROJECT=heblo
./docs/routines/test-health/rp-query.sh --test
./docs/routines/test-health/test-health-digest.sh --days 7
```

12. **Troubleshooting** — the exit-code table (`0` ok, `1` config error, `3` unreachable, `4` auth rejected, `5` unexpected HTTP status), with two explicit notes: exit `3` means *ReportPortal is unreachable*, which is never to be read as "the tests did not run"; and exit `1` means *your configuration is wrong*, which is why server-side faults use `5` instead.

- [ ] **Step 2: Verify the README covers every rule**

```bash
cd /Users/rem/Anela.Heblo
for s in test-ci: test-silence: test-shrink: test-regress: test-flaky: test-chronic: \
         "not_planned" "harness:todo" "wrong by construction"; do
  grep -q "$s" docs/routines/test-health/README.md && echo "ok   - $s" || echo "MISSING - $s"
done
```

Expected: nine `ok` lines, no `MISSING`.

- [ ] **Step 3: Commit**

```bash
git add docs/routines/test-health/README.md
git commit -m "docs(test-health): add routine definition with rules, fingerprints and fix contract"
```

---

### Task 7: Harness Process and Agent configs

**Files:**
- Create: `docs/routines/test-health/harness/test-health.process.json`
- Create: `docs/routines/test-health/harness/test-health.agent.json`
- Create: `docs/routines/test-health/harness/install.sh`

**Interfaces:**
- Consumes: the digest and helpers from Tasks 1–6, and `README.md` from Task 6.
- Produces: `install.sh` copies `test-health.process.json` → `~/harness-root/processes/test-health.json` and `test-health.agent.json` → `~/harness-root/agents/test-health.json`. It must refuse to overwrite a newer file without `--force`.

- [ ] **Step 1: Write the Process config**

`docs/routines/test-health/harness/test-health.process.json`:

```json
{
  "trigger": { "cron": "30 5 * * *" },
  "action": {
    "check": "command",
    "params": {
      "command": "git -C /Users/rem/Anela.Heblo pull --ff-only -q; /Users/rem/Anela.Heblo/docs/routines/test-health/test-health-digest.sh --days 7 --state-only",
      "timeout": 300
    }
  },
  "target": { "step": "test-health" },
  "repository": "Anela.Heblo",
  "dedup": "per-state",
  "sink": { "kind": "none" }
}
```

The `git pull` is separated by `;` rather than `&&` deliberately: a pull failure (detached worktree, local edits) must not suppress the health check, which is the one thing that must always run.

- [ ] **Step 2: Write the Agent config**

`docs/routines/test-health/harness/test-health.agent.json` — model `sonnet`, `allowed_tools` `["Read","Write","Bash","Grep","Glob"]`, `allowed_outcomes` `["done"]`, and a prompt that:

1. States it runs non-interactively, that the working directory is already an `Anela.Heblo` checkout, and that it must never wait for input.
2. Orders it to read `docs/routines/test-health/README.md` **first**.
3. Step 1: run `./docs/routines/test-health/test-health-digest.sh --days 7`. If it exits 1 (config) or 3/4 (unreachable/auth), stop, name the missing variable or the unreachable host in the artifact, file nothing, and still finish `done` — a `failed` verdict routes a configuration problem to the self-healer, which cannot fix it.
4. Step 2: pull GitHub context for the same window via `./docs/routines/test-health/gh-api.sh GET '/repos/onpaj/Anela.Heblo/commits?since=<ISO>'` and `.../pulls?state=all&per_page=30`, to tell new problems from already-fixed ones.
5. Step 3: apply the README's flag/skip rules exactly; never invent findings the digest did not compute; treat `FINDINGS: 0` as a complete and correct result.
6. Step 4: for each finding, run `./docs/routines/test-health/gh-api.sh find-signal '<fingerprint>'` and apply the dedup table; when unsure whether two findings are the same, SKIP and record it.
7. Step 5: file at most `CAP` issues via `create-issue`, body from stdin, first line `test-signal: <fingerprint>`, labels `test-health,<category-label>,harness:todo`.
8. Mandates that every issue body ends with the fix contract block verbatim from the README, and that `test-infra` issues additionally state the resolution requires a human.
9. Forbids changing code, opening PRs, committing, creating worktrees or switching branches.
10. Requires the artifact to record: the window, the digest headline numbers, every finding with its fingerprint and FILE/SKIP decision and why, and the URL of each filed issue.
11. Ends with: finish with outcome `done` in every non-crash case, including zero issues filed and including the config-abort case.

- [ ] **Step 3: Write the installer**

`docs/routines/test-health/harness/install.sh`:

```bash
#!/usr/bin/env bash
#
# install.sh — copy the versioned harness configs into ~/harness-root.
#
# ~/harness-root is NOT a git repository, so these files live in the repo and
# are installed by copying. Re-run after editing either JSON file.
#
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="${HARNESS_ROOT:-$HOME/harness-root}"
FORCE=0
[[ "${1:-}" == "--force" ]] && FORCE=1

[[ -d "${ROOT}/processes" ]] || { echo "Error: ${ROOT}/processes not found." >&2; exit 1; }
[[ -d "${ROOT}/agents" ]]    || { echo "Error: ${ROOT}/agents not found." >&2; exit 1; }

install_one() { # install_one <src> <dest>
  local src="$1" dest="$2"
  if [[ -f "$dest" && "$FORCE" -eq 0 && "$dest" -nt "$src" ]]; then
    echo "skip  ${dest} (newer than the repo copy; re-run with --force to overwrite)"
    return 0
  fi
  cp "$src" "$dest"
  echo "wrote ${dest}"
}

install_one "${HERE}/test-health.process.json" "${ROOT}/processes/test-health.json"
install_one "${HERE}/test-health.agent.json"   "${ROOT}/agents/test-health.json"
echo "Done. Restart harness to pick up the new Process:"
echo "  launchctl kickstart -k gui/\$(id -u)/com.harness"
```

- [ ] **Step 4: Verify the JSON is valid and the installer is safe**

```bash
cd /Users/rem/Anela.Heblo
jq empty docs/routines/test-health/harness/test-health.process.json && echo "process json ok"
jq empty docs/routines/test-health/harness/test-health.agent.json && echo "agent json ok"
chmod +x docs/routines/test-health/harness/install.sh
HARNESS_ROOT="$(mktemp -d)" bash -c 'mkdir -p "$HARNESS_ROOT/processes" "$HARNESS_ROOT/agents";
  docs/routines/test-health/harness/install.sh && ls "$HARNESS_ROOT/processes" "$HARNESS_ROOT/agents"'
```

Expected: `process json ok`, `agent json ok`, then `wrote .../test-health.json` twice and both files listed. Nothing is written to the real `~/harness-root` by this step.

- [ ] **Step 5: Commit**

```bash
git add docs/routines/test-health/harness
git commit -m "feat(test-health): add versioned harness Process and Agent configs with installer"
```

---

### Task 8: Live reconciliation against the real ReportPortal

**Files:**
- Modify: `docs/routines/test-health/rp-query.sh` and `test-health-digest.sh` (only if the live API shape differs)
- Modify: `docs/routines/test-health/fixtures/*` (replace hand-authored fixtures with recorded ones)

**Interfaces:**
- Consumes: everything above.
- Produces: fixtures recorded from the live instance, and whatever query-shape corrections the live API demands.

**This task is blocked until `RP_API_KEY` exists in `~/harness-root/secrets.env`.** The fixtures in Tasks 3–5 are hand-authored from the documented ReportPortal v5 shapes and have not been validated against the running server. Do not skip this task — a green offline suite proves the logic, not the integration.

- [ ] **Step 1: Verify credentials and connectivity**

```bash
set -a; . ~/harness-root/secrets.env; set +a
export RP_ENDPOINT="${RP_ENDPOINT:-http://nas.tail0cdb23.ts.net:8080/api/v1}"
export RP_PROJECT="${RP_PROJECT:-heblo}"
./docs/routines/test-health/rp-query.sh --test
```

Expected: `OK — authenticated and reachable.` followed by a JSON fragment. On exit 3 the tailnet or the NAS is down; on exit 4 the key is wrong.

- [ ] **Step 2: Record real responses as fixtures**

```bash
mkdir -p docs/routines/test-health/fixtures/live
./docs/routines/test-health/rp-query.sh '/launch?page.size=300&page.sort=startTime,DESC' \
  > docs/routines/test-health/fixtures/live/launch_page.size_300_page.sort_startTime_DESC.json
jq '{count: (.content|length), names: [.content[].name]|unique, sample: .content[0]}' \
  docs/routines/test-health/fixtures/live/launch_page.size_300_page.sort_startTime_DESC.json
```

Compare the `sample` object against the hand-authored fixture: confirm `id`, `name`, `startTime`, `attributes[]`, and `statistics.executions.{total,passed,failed,skipped}` exist with those names. If any differ, correct the `jq` expressions in `test-health-digest.sh` and the fixtures in Tasks 3–5, then re-run the offline suite until it passes again.

- [ ] **Step 3: Record a failed-items response**

```bash
lid="$(jq -r '.content[0].id' docs/routines/test-health/fixtures/live/launch_page.size_300_page.sort_startTime_DESC.json)"
./docs/routines/test-health/rp-query.sh "/item?filter.eq.launchId=${lid}&filter.in.status=FAILED,INTERRUPTED&page.size=300" \
  | jq '{count: (.content|length), sample: .content[0]}'
```

Expected: a `content` array. Confirm `name`, `status`, `description` and `pathNames.itemPaths` exist. If ReportPortal rejects the `filter.in.status` grammar (HTTP 400/500), try `/item/v2` with the same query, and record whichever works in the README's troubleshooting section.

- [ ] **Step 4: Run the digest live and read it yourself**

```bash
./docs/routines/test-health/test-health-digest.sh --days 7 | tee /tmp/test-health-live.md
```

Expected: a digest whose launch inventory matches what ReportPortal's UI shows. Given the E2E nightly has been dead since 2026-07-26, a correct run **should** report `ci-broken` or silence findings for E2E rather than a clean bill of health. A clean report here is evidence of a bug, not of health.

- [ ] **Step 5: Re-run the offline suite and commit**

```bash
./docs/routines/test-health/rp-query.test.sh
./docs/routines/test-health/gh-api.test.sh
./docs/routines/test-health/test-health-digest.test.sh
git add docs/routines/test-health
git commit -m "fix(test-health): reconcile query shapes with the live ReportPortal instance"
```

---

### Task 9: Dry-run calibration, then arm the routine

**Files:**
- Modify: `docs/routines/test-health/gh-api.sh` (add `TEST_HEALTH_DRY_RUN` support to `create-issue`)
- Modify: `docs/routines/test-health/gh-api.test.sh` (add the dry-run case)
- Modify: `docs/routines/test-health/README.md` (document the dry-run)

**Interfaces:**
- Consumes: everything above.
- Produces: `TEST_HEALTH_DRY_RUN=1` makes `create-issue` print the payload it *would* POST and exit 0 without calling GitHub.

- [ ] **Step 1: Write the failing test**

Append to `docs/routines/test-health/gh-api.test.sh`, before the summary:

```bash
# --- dry run prints the payload and never calls GitHub ---
out="$(printf 'test-signal: test-flaky:e2e:catalog:x\nbody' | \
       TEST_HEALTH_DRY_RUN=1 GITHUB_TOKEN=dummy "$G" create-issue \
       "[test-health] e2e/catalog: example" "test-health,test-flaky,harness:todo" - 2>&1)"; rc=$?
check "dry run exits 0" "0" "$rc"
case "$out" in *DRY-RUN*) r=yes ;; *) r=no ;; esac
check "dry run is labelled as such" "yes" "$r"
case "$out" in *"harness:todo"*) r=yes ;; *) r=no ;; esac
check "dry run shows the labels" "yes" "$r"
```

- [ ] **Step 2: Run test to verify it fails**

```bash
./docs/routines/test-health/gh-api.test.sh
```

Expected: the three new checks FAIL — `create-issue` attempts a real POST and errors on the dummy token.

- [ ] **Step 3: Write minimal implementation**

In `docs/routines/test-health/gh-api.sh`, inside the `create-issue` case, immediately after `payload="$(...)"` is built:

```bash
    if [[ -n "${TEST_HEALTH_DRY_RUN:-}" ]]; then
      echo "DRY-RUN — would POST to /repos/${REPO}/issues:"
      printf '%s' "$payload" | jq '{title, labels, body_first_line: (.body|split("\n")[0]), body_bytes: (.body|length)}'
      exit 0
    fi
```

- [ ] **Step 4: Run test to verify it passes**

```bash
./docs/routines/test-health/gh-api.test.sh
```

Expected: `passed: 11  failed: 0`, exit 0.

- [ ] **Step 5: Dry-run the whole routine by hand**

```bash
set -a; . ~/harness-root/secrets.env; set +a
export RP_ENDPOINT="${RP_ENDPOINT:-http://nas.tail0cdb23.ts.net:8080/api/v1}" RP_PROJECT=heblo
./docs/routines/test-health/test-health-digest.sh --days 7 > /tmp/test-health-digest.md
grep -E '^(FINDINGS|CAP|STATE):' /tmp/test-health-digest.md
```

Read `/tmp/test-health-digest.md` and judge the findings before any issue is filed. **Stop and report to the operator here** — arming the Process turns each finding into an auto-merged PR, and the thresholds have never been validated against real data.

- [ ] **Step 6: Commit**

```bash
git add docs/routines/test-health/gh-api.sh docs/routines/test-health/gh-api.test.sh docs/routines/test-health/README.md
git commit -m "feat(test-health): add dry-run mode for calibration before arming"
```

- [ ] **Step 7: Install and arm (operator decision, not automatic)**

Only after the operator approves the dry-run output:

```bash
./docs/routines/test-health/harness/install.sh
launchctl kickstart -k gui/$(id -u)/com.harness
```

Then confirm the Process is registered:

```bash
ls -l ~/harness-root/processes/test-health.json ~/harness-root/agents/test-health.json
```

---

## Self-Review

**Spec coverage:**

| Spec section | Task |
|---|---|
| Prerequisites / secrets | Task 8 Step 1, Task 9 Step 5 |
| `rp-query.sh` | Task 1 |
| `gh-api.sh` | Tasks 2, 9 |
| `test-health-digest.sh` | Tasks 3, 4, 5 |
| `README.md` (rules, thresholds) | Task 6 |
| `fixtures/` + offline tests | Tasks 3, 4, 5, 8 |
| Harness Process + Agent | Task 7 |
| Launch inventory / presence / CI cross-check | Tasks 3, 4 |
| Suite shrink / regression / flaky / chronic | Task 5 |
| Cascade suppression | Task 4 |
| Fingerprints + dedup | Tasks 2, 5, 6 |
| Volume cap + priority order | Task 5, Task 6 |
| Issue contract + fix contract | Tasks 6, 7 |
| Error handling / exit codes | Tasks 1, 3, 6 |
| RP-unreachable never yields silence findings | Task 3 (test), Task 4 (logic) |
| Dry-run calibration | Task 9 |
| Open question: unverified API shapes | Task 8 |

One spec item is deliberately *not* a separate task: the backend/frontend single-run regression rule falls out of the same `recent_fails` logic as E2E, because those layers have `n` launches per window with no 26h freshness horizon.

The consecutive-error-day counter is implemented in full in Task 3 (`record_error_and_exit` / `clear_error_days`), not stubbed — it needs only the state file, so it is testable offline and carries no dependency on the credential-blocked Task 8. This is the mechanism that stops the routine from failing quietly the way `telemetry-anomaly` did.

**Placeholder scan:** no `TBD`/`TODO`/"implement later"/"similar to Task N" remain. Every code step carries complete code. Task 6 specifies README contents as an explicit ordered list with the exact strings its verification greps for, rather than prose.

**Type consistency:** finding objects use `{category, layer, module, fingerprint, headline, detail}` in Tasks 3, 4 and 5. `add_finding` takes one JSON object argument throughout. Fixture file naming uses one sanitization rule (`[^A-Za-z0-9._-]` → `_`), defined in Task 1 and reused by `gh_get` in Task 4. Exit codes 0/1/3/4 are identical in Tasks 1, 3, 6, 7 and 8.
