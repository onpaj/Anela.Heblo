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

# --- C1: error paths must reach the harness. In --state-only mode the
# harness's command check discards stdout entirely on a nonzero exit and
# returns no observation at all, so an RP-unreachable/auth-rejected/5xx/
# config error must exit 0 (with the STATE line already printed) instead of
# leaving the harness in total silence. Full mode keeps exiting with the real
# code, since the agent (not the harness's own check) reads it. ---
c1sf="$(mktemp)"; rm -f "$c1sf"
out="$(TEST_HEALTH_STATE_FILE="$c1sf" RP_FIXTURE_DIR=/nonexistent-fixture-dir "$D" --days 7 --state-only 2>&1)"; rc=$?
check "C1: --state-only + unreachable RP exits 0" "0" "$rc"
check "C1: --state-only + unreachable RP still prints STATE: error=" "yes" "$(contains 'STATE: error=' "$out")"
rm -f "$c1sf"

# --- C1: startup err() (missing tool / bad argument) must ALSO route through
# the state-emitting path in --state-only mode, not just runtime RP errors.
# A bad argument is the portable stand-in here for "missing jq/perl/rp-query.sh"
# (those require sabotaging PATH); the mechanism err() uses is identical.
c1sf2="$(mktemp)"; rm -f "$c1sf2"
out="$(TEST_HEALTH_STATE_FILE="$c1sf2" "$D" --state-only --bogus-argument 2>&1)"; rc=$?
check "C1: --state-only + bad argument exits 0" "0" "$rc"
check "C1: --state-only + bad argument prints STATE: error=" "yes" "$(contains 'STATE: error=' "$out")"
rm -f "$c1sf2"

out="$("$D" --bogus-argument 2>&1)"; rc=$?
check "C1: bad argument WITHOUT --state-only still exits nonzero" "yes" "$([[ $rc -ne 0 ]] && echo yes || echo no)"

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

# --- whole layer stale, GitHub unreachable -> ONE unattributed finding ---
out="$(RP_FIXTURE_DIR="${FIX}/gh-down" GH_FIXTURE_DIR="${FIX}/gh-down" "$D" --days 7 2>&1)"
check "gh-down is reported as unattributed" "yes" "$(contains 'test-ci:e2e-nightly-regression.yml:unattributed' "$out")"
n="$(printf '%s\n' "$out" | grep -c 'test-ci:')"
check "gh-down files exactly one ci finding" "1" "$n"
n="$(printf '%s\n' "$out" | grep -c 'test-silence:')"
check "gh-down does not cascade into per-module silence" "0" "$n"

# --- partial staleness + GitHub unreachable -> per-module, honestly labelled ---
out="$(RP_FIXTURE_DIR="${FIX}/gh-down-partial" GH_FIXTURE_DIR="${FIX}/gh-down-partial" "$D" --days 7 2>&1)"
check "partial gh-down reaches the per-module path" "yes" "$(contains 'test-silence:e2e:transport:unattributed' "$out")"
check "partial gh-down does not claim a schedule fault" "no" "$(contains '**schedule-broken**' "$out")"
n="$(printf '%s\n' "$out" | grep -c 'test-ci:')"
check "partial gh-down files no cascade finding" "0" "$n"

# --- a 2xx GitHub body of the wrong shape is unknown, not "no failing run" ---
out="$(RP_FIXTURE_DIR="${FIX}/gh-malformed" GH_FIXTURE_DIR="${FIX}/gh-malformed" "$D" --days 7 2>&1)"
check "malformed gh body reads as unattributed" "yes" "$(contains 'unattributed' "$out")"
check "malformed gh body claims no schedule fault" "no" "$(contains '**schedule-broken**' "$out")"

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

# --- fingerprint is stable across repeated runs of the same fixture ---
# (collision-freeness across DIFFERENT errors is checked separately below by
# the two-errors fixture; this only proves determinism, not uniqueness.)
a="$(RP_FIXTURE_DIR="${FIX}/regression" GH_FIXTURE_DIR="${FIX}/regression" "$D" --days 7 2>&1 | grep -o 'test-regress:[^ `]*' | head -1)"
b="$(RP_FIXTURE_DIR="${FIX}/regression" GH_FIXTURE_DIR="${FIX}/regression" "$D" --days 7 2>&1 | grep -o 'test-regress:[^ `]*' | head -1)"
check "fingerprint is stable across runs" "$a" "$b"

# --- two different errors must NOT collide into one fingerprint ---
out="$(RP_FIXTURE_DIR="${FIX}/two-errors" GH_FIXTURE_DIR="${FIX}/two-errors" "$D" --days 7 2>&1)"
n="$(printf '%s\n' "$out" | grep -o 'test-regress:[^ `]*' | sort -u | grep -c .)"
check "two different errors get two fingerprints" "2" "$n"

# --- an 8+ digit DECIMAL must not be normalized like a hex id ---
out="$(RP_FIXTURE_DIR="${FIX}/big-numbers" GH_FIXTURE_DIR="${FIX}/big-numbers" "$D" --days 7 2>&1)"
n="$(printf '%s\n' "$out" | grep -o 'test-regress:[^ `]*' | sort -u | grep -c .)"
check "large decimals do not collide into one fingerprint" "2" "$n"

# --- chronic: red every run held for this module, span reported not claimed ---
out="$(RP_FIXTURE_DIR="${FIX}/chronic" GH_FIXTURE_DIR="${FIX}/chronic" "$D" --days 7 2>&1)"
check "chronic is detected" "yes" "$(contains 'test-chronic:e2e:catalog:' "$out")"
check "chronic is not also called flaky" "no" "$(contains 'test-flaky:' "$out")"
check "chronic reports a measured span, not a claimed week" "no" "$(contains 'for a week' "$out")"
check "chronic reports the measured span (6 days for 7 daily launches)" "yes" "$(contains 'spanning 6 days' "$out")"

# --- thin history is not chronic: two launches is not "every run" evidence ---
out="$(RP_FIXTURE_DIR="${FIX}/chronic-thin" GH_FIXTURE_DIR="${FIX}/chronic-thin" "$D" --days 7 2>&1)"
check "two all-red launches are not called chronic" "no" "$(contains 'test-chronic:' "$out")"

# --- a self-healed test is neither flaky nor a regression ---
out="$(RP_FIXTURE_DIR="${FIX}/self-healed" GH_FIXTURE_DIR="${FIX}/self-healed" "$D" --days 7 2>&1)"
check "self-healed test is not flagged flaky" "no" "$(contains 'test-flaky:' "$out")"
check "self-healed test is not flagged a regression" "no" "$(contains 'test-regress:' "$out")"

# --- the cap is enforced and stated, never silent ---
out="$(RP_FIXTURE_DIR="${FIX}/regression" GH_FIXTURE_DIR="${FIX}/regression" "$D" --days 7 2>&1)"
check "digest states the cap" "yes" "$(contains 'CAP: 5' "$out")"

echo "---"; echo "passed: $pass  failed: $fail"
[[ $fail -eq 0 ]]
