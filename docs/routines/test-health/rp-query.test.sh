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
