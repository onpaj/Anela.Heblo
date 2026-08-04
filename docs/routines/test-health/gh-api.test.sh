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

# No token is an explicit error.
out="$(env -u GIT_PAT -u GITHUB_TOKEN "$G" GET '/rate_limit' 2>&1)"; rc=$?
check "no token exits nonzero" "1" "$rc"
case "$out" in *token*) r=yes ;; *) r=no ;; esac
check "no token says so" "yes" "$r"

echo "---"; echo "passed: $pass  failed: $fail"
[[ $fail -eq 0 ]]
