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

# --- I1: a genuine flake whose newest two runs both happen to fail must still
# be classified flaky, not regression. Fixture hit sequence (newest->oldest)
# is 1101010: k=4 fails, flips=5, pass rate 42%. Before the fix, dropping the
# old `k -eq 2` guard made `flaky` unreachable whenever recent_fails==2, so
# this exact shape misclassified as a regression with an overclaiming
# headline ("newly fails two runs running") and a fingerprint that could
# never dedup against the test's own flaky fingerprint.
out="$(RP_FIXTURE_DIR="${FIX}/flaky-newest-two-failed" GH_FIXTURE_DIR="${FIX}/flaky-newest-two-failed" "$D" --days 7 2>&1)"
check "I1: alternating newest-two-failed test is classified flaky" "yes" "$(contains 'test-flaky:e2e:catalog' "$out")"
check "I1: alternating newest-two-failed test is NOT called a regression" "no" "$(contains 'test-regress:' "$out")"

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

# --- C2: total absence of data must not read as a healthy week. The `clean`
# fixture's one module/launch is inside the window and the 26h freshness
# horizon at the pinned TEST_HEALTH_NOW_MS above; advancing "now" 9 days past
# it pushes that same launch outside BOTH, so `launches` (window-filtered) is
# empty for this module while `raw_launches` (unfiltered newest-300) still
# knows it exists. Before the fix this produced FINDINGS: 0 and the exact
# same STATE as the healthy run -- indistinguishable from "nothing to report."
c2_healthy_state="$(RP_FIXTURE_DIR="${FIX}/clean" GH_FIXTURE_DIR="${FIX}/clean" TEST_HEALTH_NOW_MS=1785030000000 "$D" --days 7 --state-only 2>&1)"
c2_stale_out="$(RP_FIXTURE_DIR="${FIX}/clean" GH_FIXTURE_DIR="${FIX}/clean" TEST_HEALTH_NOW_MS=1785777600000 "$D" --days 7 2>&1)"
c2_stale_state="$(RP_FIXTURE_DIR="${FIX}/clean" GH_FIXTURE_DIR="${FIX}/clean" TEST_HEALTH_NOW_MS=1785777600000 "$D" --days 7 --state-only 2>&1)"
check "C2: 9-days-stale clean fixture is no longer FINDINGS: 0" "no" "$(contains 'FINDINGS: 0' "$c2_stale_out")"
check "C2: 9-days-stale STATE differs from the healthy run's" "no" "$([[ "$c2_healthy_state" == "$c2_stale_state" ]] && echo yes || echo no)"

# --- C3: three different tests failing on the same normalized error in one
# module must collapse to exactly ONE finding (chronic/regression fingerprints
# deliberately omit the test path), with the other affected specs rolled into
# the surviving finding's detail rather than silently dropped.
out="$(RP_FIXTURE_DIR="${FIX}/chronic-collision" GH_FIXTURE_DIR="${FIX}/chronic-collision" "$D" --days 7 2>&1)"
n="$(printf '%s\n' "$out" | grep -c 'test-chronic:')"
check "C3: three same-error tests collapse to one finding" "1" "$n"
check "C3: detail names the first collapsed test" "yes" "$(contains 'loads product page' "$out")"
check "C3: detail names the second collapsed test" "yes" "$(contains 'filters by category' "$out")"
check "C3: detail names the third collapsed test" "yes" "$(contains 'sorts by price' "$out")"

# --- C4: a sustained regression (failing the newest 3 of 7 runs) must be
# detected. The old rule required EXACTLY two failures in the whole window
# (`k -eq 2`), so this was neither chronic (k != n) nor a regression (k != 2)
# nor flaky (only 1 flip) -- it produced nothing at all.
out="$(RP_FIXTURE_DIR="${FIX}/sustained-regression" GH_FIXTURE_DIR="${FIX}/sustained-regression" "$D" --days 7 2>&1)"
check "C4: newest-3-of-7 failures reported as a regression" "yes" "$(contains 'test-regress:e2e:catalog:' "$out")"
check "C4: sustained regression is not called chronic" "no" "$(contains 'test-chronic:' "$out")"
check "C4: sustained regression is not called flaky" "no" "$(contains 'test-flaky:' "$out")"

# --- I2: a malformed RP launch payload (.content missing/not-an-array) must
# be a hard error, not silently read as zero launches -- the same defect C2
# fixed for the window-filtered view, one level up on the raw page. ---
out="$(RP_FIXTURE_DIR="${FIX}/rp-malformed" "$D" --days 7 2>&1)"; rc=$?
check "I2: malformed RP .content exits 5" "5" "$rc"
check "I2: malformed RP .content emits a STATE error line" "yes" "$(contains 'STATE: error=5' "$out")"

# --- I2: a validly-shaped but genuinely EMPTY .content is a real answer, not
# a parse failure -- but it must not read as a healthy clean week. It needs
# its own finding and a STATE distinct from the clean fixture's. ---
out="$(RP_FIXTURE_DIR="${FIX}/rp-empty" "$D" --days 7 2>&1)"; rc=$?
check "I2: empty RP .content exits 0" "0" "$rc"
check "I2: empty RP .content produces a finding" "yes" "$(contains 'test-rp-empty:no-launches' "$out")"
check "I2: empty RP .content is not FINDINGS: 0" "no" "$(contains 'FINDINGS: 0' "$out")"
clean_state="$(RP_FIXTURE_DIR="${FIX}/clean" "$D" --days 7 --state-only 2>&1)"
empty_state="$(RP_FIXTURE_DIR="${FIX}/rp-empty" "$D" --days 7 --state-only 2>&1)"
check "I2: empty-.content STATE differs from the clean run's" "no" "$([[ "$clean_state" == "$empty_state" ]] && echo yes || echo no)"

# --- I7: suite-shrank must not fire when the newest launch actually FAILED --
# the shrink is fully explained by the failure, and the finding's own detail
# text ("The launch still succeeded...") would otherwise be an assertion it
# never checked.
out="$(RP_FIXTURE_DIR="${FIX}/shrank-failed" GH_FIXTURE_DIR="${FIX}/shrank-failed" "$D" --days 7 2>&1)"
check "I7: shrink is suppressed when the newest launch FAILED" "no" "$(contains 'test-shrink:' "$out")"

echo "---"; echo "passed: $pass  failed: $fail"
[[ $fail -eq 0 ]]
