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

echo "---"; echo "passed: $pass  failed: $fail"
[[ $fail -eq 0 ]]
