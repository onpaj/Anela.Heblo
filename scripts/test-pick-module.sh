#!/usr/bin/env bash
# Tests for pick-module.sh. Run: ./scripts/test-pick-module.sh
set -uo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PICK="$HERE/pick-module.sh"
export HEBLO_NO_PULL=1

pass=0
fail=0
check() { # check <description> <condition-exit-code>
  if [ "$2" -eq 0 ]; then pass=$((pass + 1)); echo "  ok   $1"
  else fail=$((fail + 1)); echo "  FAIL $1"; fi
}

echo "== real map =="
out="$("$PICK")"; rc=$?
check "exits 0 on the real map" "$rc"
check "prints exactly one line" "$([ "$(printf '%s' "$out" | wc -l | tr -d ' ')" = "0" ] && echo 0 || echo 1)"
check "matches the expected format" \
  "$(printf '%s' "$out" | grep -qE '^Architecture review of module map part #[0-9]+ — .+$' && echo 0 || echo 1)"

echo "== 200 draws =="
tmp="$(mktemp)"
for _ in $(seq 200); do "$PICK" >> "$tmp" || echo "PICKER-FAILED" >> "$tmp"; done
check "no draw failed" "$(grep -q 'PICKER-FAILED' "$tmp" && echo 1 || echo 0)"
malformed="$(grep -cvE '^Architecture review of module map part #[0-9]+ — .+$' "$tmp")"
check "every draw is well-formed (malformed: $malformed)" \
  "$([ "$malformed" = "0" ] && echo 0 || echo 1)"
distinct="$(sed -E 's/^.*part #([0-9]+) .*$/\1/' "$tmp" | sort -un | wc -l | tr -d ' ')"
echo "  (distinct parts drawn in 200: $distinct)"
check "draws are spread over many parts (>30 distinct)" "$([ "$distinct" -gt 30 ] && echo 0 || echo 1)"
check "never draws a RETIRED part" "$(grep -qi 'RETIRED' "$tmp" && echo 1 || echo 0)"
rm -f "$tmp"

echo "== synthetic map: retired rows are skipped =="
syn="$(mktemp)"
cat > "$syn" <<'EOF'
# Application Module Map
| # | Part | Approx. size |
|---|------|--------------|
| 1 | Alpha | BE ~1k |
| 2 | RETIRED — merged into #1 | — |
| 3 | Gamma | BE ~2k |
EOF
res="$(HEBLO_MAP="$syn" bash -c 'for _ in $(seq 60); do "$0"; done' "$PICK" | sed -E 's/^.*part #([0-9]+) .*$/\1/' | sort -un | tr '\n' ' ')"
check "only live rows drawn (got: $res)" "$([ "$res" = "1 3 " ] && echo 0 || echo 1)"
rm -f "$syn"

echo "== synthetic map: header and separator rows are not parts =="
syn2="$(mktemp)"
cat > "$syn2" <<'EOF'
# Application Module Map
| # | Part | Approx. size | Primary route(s) |
|---|------|--------------|------------------|
| 7 | Seven | BE ~1k | `/seven` |
|---|------|--------------|------------------|
| # | Part | Approx. size |
| 8 | Eight | BE ~2k |
EOF
res2="$(HEBLO_MAP="$syn2" bash -c 'for _ in $(seq 40); do "$0"; done' "$PICK" | sort -u | tr '\n' '/')"
check "only the two real rows drawn (got: $res2)" \
  "$([ "$res2" = "Architecture review of module map part #7 — Seven/Architecture review of module map part #8 — Eight/" ] && echo 0 || echo 1)"
rm -f "$syn2"

echo "== failure modes =="
HEBLO_MAP=/nonexistent/map.md "$PICK" >/dev/null 2>&1
rc=$?
check "missing map exits non-zero" "$([ "$rc" -ne 0 ] && echo 0 || echo 1)"
out="$(HEBLO_MAP=/nonexistent/map.md "$PICK" 2>/dev/null)"
check "missing map prints nothing on stdout" "$([ -z "$out" ] && echo 0 || echo 1)"

empty="$(mktemp)"; printf '# Nothing here\n' > "$empty"
HEBLO_MAP="$empty" "$PICK" >/dev/null 2>&1
rc=$?
check "map with no rows exits non-zero" "$([ "$rc" -ne 0 ] && echo 0 || echo 1)"
out="$(HEBLO_MAP="$empty" "$PICK" 2>/dev/null)"
check "map with no rows prints nothing on stdout" "$([ -z "$out" ] && echo 0 || echo 1)"
rm -f "$empty"

echo
echo "passed: $pass  failed: $fail"
[ "$fail" -eq 0 ]
