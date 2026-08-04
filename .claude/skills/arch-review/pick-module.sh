#!/usr/bin/env bash
#
# pick-module.sh — print one randomly chosen live part of a module map, as a
# single line the /arch-review skill turns into a review request:
#
#   Architecture review of module map part #7 — Monitoring TUI (map: /abs/path/module-map.md)
#
# The map is an INPUT, not a constant: this script works against any map that
# follows the contract in ../arch-map/map-contract.md, in any repository.
#
# Usage:
#   pick-module.sh [map-path]
#
#   map-path   the module map to draw from. Optional — when omitted, the map is
#              discovered in the repository you are standing in, so running this
#              inside a repo needs no arguments at all. Pass a path only to audit
#              a repository other than the current one.
#
# Exits non-zero and prints NOTHING on stdout if anything is wrong. The caller
# treats a non-zero exit as "no review this cycle", so a broken picker costs no
# agent call. Fail closed — a parse bug that silently emitted a malformed line
# would instead spend a full review on nothing.
#
# Env:
#   ARCH_MAP      map path, when not passed as an argument
#   ARCH_NO_PULL  any non-empty value skips the git pull (tests)
#   ARCH_PART     review this exact part number instead of drawing one
set -euo pipefail

# Map resolution, most explicit first: argument, then $ARCH_MAP, then discovery
# in the repository the caller is standing in. Discovery is what makes a zero-arg
# run inside a repo work; the argument exists to audit a DIFFERENT repository.
CONVENTIONAL="docs/architecture/module-map.md"

discover_map() {
  # The repo we're standing in, falling back to this script's own checkout — the
  # two differ whenever the skill is installed elsewhere or invoked from a
  # sibling tree.
  local root
  root="$(git rev-parse --show-toplevel 2>/dev/null)" \
    || root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"

  if [ -f "$root/$CONVENTIONAL" ]; then
    printf '%s\n' "$root/$CONVENTIONAL"
    return 0
  fi

  # Not in the conventional place — look for it, but refuse to guess between
  # several. Bounded depth so this stays cheap on a large tree.
  local found
  found="$(find "$root" -maxdepth 4 -name 'module-map.md' -type f \
             -not -path '*/.git/*' -not -path '*/node_modules/*' 2>/dev/null)"
  local count
  count="$(printf '%s\n' "$found" | grep -c . || true)"

  if [ "$count" -eq 1 ]; then
    printf '%s\n' "$found"
    return 0
  fi
  if [ "$count" -gt 1 ]; then
    echo "pick-module: several module maps found, pass one explicitly:" >&2
    while IFS= read -r map_path; do
      printf '  %s\n' "$map_path" >&2
    done <<<"$found"
    return 1
  fi
  echo "pick-module: no module map found under $root" >&2
  echo "  expected $CONVENTIONAL — create one with the arch-map skill," >&2
  echo "  or pass a path: pick-module.sh <map-path>" >&2
  return 1
}

if [ -n "${1:-}" ]; then
  MAP="$1"
elif [ -n "${ARCH_MAP:-}" ]; then
  MAP="$ARCH_MAP"
else
  MAP="$(discover_map)" || exit 1
fi

[ -f "$MAP" ] || { echo "pick-module: no module map at $MAP" >&2; exit 1; }
MAP="$(cd "$(dirname "$MAP")" && pwd)/$(basename "$MAP")"

# Prefer to review the current default branch, but NEVER fail on the pull. The
# pull is an optimisation; the map on disk is the actual input. A fail-closed
# pull would mean one unpushed local commit — or any diverged clone, an ordinary
# state for a working checkout — silently stops the whole process forever, with
# no review, no issue and no error anywhere. A review of a slightly stale tree is
# enormously better than that.
#
# The repo to pull is the one the MAP lives in, not the one this script lives in
# — they are different whenever the map is passed in from outside.
if [ -z "${ARCH_NO_PULL:-}" ]; then
  if map_repo="$(git -C "$(dirname "$MAP")" rev-parse --show-toplevel 2>/dev/null)"; then
    git -C "$map_repo" pull --ff-only -q >&2 \
      || echo "pick-module: git pull failed, reviewing the tree as it stands" >&2
  fi
fi

# Summary-table rows only: "| <n> | <name> | ...". The per-part "## N." headings
# are deliberately not parsed — a retired part keeps its heading, and parsing
# headings would resurrect it. Retired rows exist only so old references stay
# resolvable.
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

if [ -n "${ARCH_PART:-}" ]; then
  line="$(printf '%s\n' "$rows" | awk -F'\t' -v want="$ARCH_PART" '$1 == want {print; exit}')"
  [ -n "$line" ] || {
    echo "pick-module: part #$ARCH_PART is not a live row in $MAP" >&2
    exit 1
  }
else
  index=$(( RANDOM % count + 1 ))
  line="$(printf '%s\n' "$rows" | sed -n "${index}p")"
fi

num="${line%%$'\t'*}"
name="${line#*$'\t'}"

# The map path travels with the pick so the reviewer is self-sufficient — it can
# open the map and the part's own entry without being told separately where the
# map lives.
printf 'Architecture review of module map part #%s — %s (map: %s)\n' "$num" "$name" "$MAP"
