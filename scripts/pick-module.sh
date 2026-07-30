#!/usr/bin/env bash
#
# Print one randomly chosen live part of the application module map, as a single
# line the harness `command` check turns into an architecture-review task:
#
#   Architecture review of module map part #17 — Manufacture Inventory, Lots & Settings
#
# Exits non-zero and prints nothing on stdout if anything is wrong: the harness
# treats a non-zero exit as "no observation", so a broken picker costs no agent
# call. Fail closed.
#
# Env:
#   HEBLO_MAP      override the map path (tests)
#   HEBLO_NO_PULL  any non-empty value skips the git pull (tests)
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MAP="${HEBLO_MAP:-$REPO/docs/architecture/module-map.md}"

# Prefer to review current main, but NEVER fail on the pull. The pull is an
# optimisation; the map on disk is the actual input. A fail-closed pull would
# mean one unpushed local commit — or any diverged clone — silently stops the
# whole process forever, with no task, no issue and no error anywhere. A review
# of a slightly stale tree is enormously better than that.
if [ -z "${HEBLO_NO_PULL:-}" ]; then
  git -C "$REPO" pull --ff-only -q >&2 \
    || echo "pick-module: git pull failed, reviewing the tree as it stands" >&2
fi

[ -f "$MAP" ] || { echo "pick-module: no module map at $MAP" >&2; exit 1; }

# Summary-table rows only: "| <n> | <name> | ...". The per-part "## N." headings
# are deliberately not parsed — a retired part keeps its heading. Header rows
# ("| # | Part | ...") and separators ("|---|---|") fail the numeric first cell.
# Retired rows are dropped; they exist only so old references stay resolvable.
rows="$(
  grep -E '^\|[[:space:]]*[0-9]+[[:space:]]*\|' "$MAP" \
    | grep -viE '\|[^|]*RETIRED' \
    | awk -F'|' '{
        num = $2; name = $3;
        gsub(/^[ \t]+|[ \t]+$/, "", num);
        gsub(/^[ \t]+|[ \t]+$/, "", name);
        if (num != "" && name != "") print num "\t" name;
      }'
)"

count="$(printf '%s\n' "$rows" | grep -c . || true)"
[ "$count" -gt 0 ] || { echo "pick-module: no parts parsed from $MAP" >&2; exit 1; }

index=$(( RANDOM % count + 1 ))
line="$(printf '%s\n' "$rows" | sed -n "${index}p")"
num="${line%%$'\t'*}"
name="${line#*$'\t'}"

printf 'Architecture review of module map part #%s — %s\n' "$num" "$name"
