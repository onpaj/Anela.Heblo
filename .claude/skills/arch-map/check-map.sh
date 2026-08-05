#!/usr/bin/env bash
#
# check-map.sh — validate a module map against the contract and against the
# repository it claims to describe.
#
# Three passes:
#   STRUCTURE  every live summary-table row has a `## N.` section with a
#              non-empty `Owns:`; part numbers are unique.
#   REVERSE    every path the map claims exists, still exists  ("dead reference")
#   FORWARD    every source directory belongs to some part     ("unassigned code")
#
# STRUCTURE and REVERSE failures are ERRORs and exit non-zero — they mean the map
# lies, and a review bounded by a lie is worse than no review. FORWARD misses are
# WARNings only: a repo legitimately contains code that is deliberately unmapped,
# and leaf-name matching trades false negatives for far fewer false positives
# (full-path matching produces dozens of useless hits).
#
# Usage:
#   check-map.sh [map-path]      # defaults to the map of the repo you are in
set -euo pipefail

CONVENTIONAL="docs/architecture/module-map.md"

if [ -n "${1:-}" ]; then
  MAP="$1"
else
  root="$(git rev-parse --show-toplevel 2>/dev/null || pwd)"
  MAP="$root/$CONVENTIONAL"
fi

[ -f "$MAP" ] || { echo "check-map: no module map at $MAP" >&2; exit 1; }
MAP="$(cd "$(dirname "$MAP")" && pwd)/$(basename "$MAP")"

# The repo a map describes is the repo the map lives in.
REPO="$(git -C "$(dirname "$MAP")" rev-parse --show-toplevel 2>/dev/null || dirname "$MAP")"
cd "$REPO"

# Owns content for one part (by number) or for every part (""). Both notations
# are legal and both appear in the wild:
#   **Owns:** `path/`                 inline, on the heading line's own line
#   **Owns:**                         followed by a `- \`path/\`` bullet list
#     - `path/`
# `**Also owns:**` is treated the same way.
owns_lines() {
  awk -v want="${2:-}" '
    BEGIN { inpart = (want == "") }
    /^##[[:space:]]+[0-9]+\./ {
      n = $2; sub(/\..*/, "", n)
      inpart = (want == "" || n == want)
      grab = 0
    }
    !inpart { next }
    /^\*\*(Also[[:space:]]+)?[Oo]wns:\*\*/ {
      line = $0
      sub(/^\*\*(Also[[:space:]]+)?[Oo]wns:\*\*[[:space:]]*/, "", line)
      if (line != "") print line
      grab = 1
      next
    }
    /^\*\*[A-Z]/ { grab = 0 }
    grab && /^-/ { print }
  ' "$1"
}

# The first backticked token of each line — the owned path. A line may carry
# commentary that is also backticked (the symbols a module defines, a helper
# shipped alongside); treating those as paths would report a type name as a
# broken boundary.
first_tokens() {
  { grep -oE '^[^`]*`[^`]+`' || true; } | { grep -oE '`[^`]+`$' || true; } | tr -d '`'
}

errors=0
warnings=0
err()  { echo "ERROR:   $*"; errors=$((errors + 1)); }
warn() { echo "WARNING: $*"; warnings=$((warnings + 1)); }

echo "== Checking $MAP =="
echo "repository: $REPO"
echo

# ---------------------------------------------------------------- STRUCTURE --

# Summary-table rows: "| <n> | <name> | ...". Same parse as pick-module.sh, so
# what this validates is exactly what the picker will draw from.
# `|| true` on every grep that may legitimately match nothing: under
# `set -o pipefail` a no-match exits 1 and would abort the script before its own
# diagnostics could run — the empty-map case would fail silently instead of
# reporting an empty map.
rows="$(
  { grep -E '^\|[[:space:]]*[0-9]+[[:space:]]*\|' "$MAP" || true; } \
    | awk -F'|' '{
        num = $2; name = $3;
        gsub(/^[ \t]+|[ \t]+$/, "", num);
        gsub(/^[ \t]+|[ \t]+$/, "", name);
        if (num != "" && name != "") print num "\t" name;
      }'
)"

[ -n "$rows" ] || { err "no summary-table rows found — the picker would draw nothing"; echo; echo "errors: $errors  warnings: $warnings"; exit 1; }

live_rows="$(printf '%s\n' "$rows" | grep -viE '\bRETIRED\b' || true)"
live_count="$(printf '%s\n' "$live_rows" | grep -c . || true)"
total_count="$(printf '%s\n' "$rows" | grep -c . || true)"

echo "parts: $total_count total, $live_count live"

dupes="$(printf '%s\n' "$rows" | cut -f1 | sort | uniq -d)"
if [ -n "$dupes" ]; then
  for d in $dupes; do
    err "part number #$d appears in more than one row — numbers are identifiers"
  done
fi

# Section headings: "## N. Name"
sections="$(grep -E '^##[[:space:]]+[0-9]+\.' "$MAP" | sed -E 's/^##[[:space:]]+([0-9]+)\..*/\1/' || true)"

while IFS="$(printf '\t')" read -r num name; do
  [ -n "$num" ] || continue
  if ! printf '%s\n' "$sections" | grep -qx "$num"; then
    err "part #$num ($name) has a summary row but no '## $num.' section"
    continue
  fi
  owns="$(owns_lines "$MAP" "$num")"
  if [ -z "$owns" ]; then
    err "part #$num ($name) has no 'Owns:' paths — nothing bounds its review"
  fi
done <<EOF
$live_rows
EOF

echo

# ------------------------------------------------------------------ REVERSE --
# Paths the map claims still exist.
#
# Two tiers, and the distinction is the point: an `Owns:` path is a machine-read
# contract — the reviewer's scope boundary — so a dead one is an ERROR. A path in
# prose is documentation; a dead one is worth a WARNING but must not fail the
# map. Without that split, an honest sentence like "there is no `docs/adr/`"
# would be indistinguishable from a broken boundary.

# Repo-relative candidates only. Dropped: absolute paths and slash-commands
# (`/oneshot`), template placeholders (`artifacts/{id}/state.json`, `<name>`),
# and URLs.
path_candidates() {
  { grep -oE '`[^`]+`' "$1" || true; } \
    | tr -d '`' \
    | { grep -E '/' || true; } \
    | { grep -vE '^/' || true; } \
    | { grep -vE '^(https?|git@)' || true; } \
    | { grep -vE '[{}<>]' || true; } \
    | sed 's/[,.]$//' \
    | sort -u
}

# A shorthand fragment like `data/agents/` is relative to some parent the prose
# established; only tokens whose first segment is a real top-level entry can be
# resolved from the repo root.
resolvable() {
  case "${1%%/*}" in "" ) return 1 ;; esac
  [ -e "${1%%/*}" ]
}

# Tokens that look like paths but cannot be resolved from the repo root, and
# must not be reported as broken boundaries:
#   .../Features/Foo/   ellipsis shorthand for a long path the prose established
#   /catalog, /baleni/* URL routes, not files — repo paths are never absolute
#   a/{id}/b, <name>    template placeholders
unresolvable_token() {
  case "$1" in
    ...*|*/...*) return 0 ;;
    /*)          return 0 ;;
    *[{}\<\>]*)  return 0 ;;
  esac
  return 1
}

exists_path() {
  local probe="$1"
  # Truncate a glob at the last complete directory segment before the wildcard:
  # `backend/test/Foo.*.Tests/` is a claim about `backend/test/`, not about the
  # literal prefix `backend/test/Foo.`.
  case "$probe" in
    *'*'*) probe="${probe%%\**}"; probe="${probe%/*}/" ;;
  esac
  [ -n "$probe" ] || return 0
  [ -e "$probe" ] || [ -e "${probe%/}" ]
}

echo "== Reverse pass: paths the map claims =="

# Every owned path in the map — the boundary contract the reviewer is scoped by.
owns_paths="$(owns_lines "$MAP" "" | first_tokens | sort -u || true)"

missing=0
while IFS= read -r p; do
  [ -n "$p" ] || continue
  unresolvable_token "$p" && continue
  if ! exists_path "$p"; then
    err "dead Owns: path — part boundary is broken: $p"
    missing=$((missing + 1))
  fi
done <<EOF
$owns_paths
EOF

while IFS= read -r p; do
  [ -n "$p" ] || continue
  printf '%s\n' "$owns_paths" | grep -qxF "$p" && continue
  unresolvable_token "$p" && continue
  resolvable "$p" || continue
  exists_path "$p" || { warn "stale path in prose: $p"; missing=$((missing + 1)); }
done <<EOF
$(path_candidates "$MAP")
EOF

[ "$missing" -eq 0 ] && echo "all claimed paths exist"
echo

# ------------------------------------------------------------------ FORWARD --
# Source directories the map never mentions. Leaf-name matching on purpose: the
# map uses compact notation, so full-path matching reports dozens of false hits.

echo "== Forward pass: source directories not mentioned in the map =="
SKIP_RE='(^|/)(\.git|node_modules|vendor|dist|build|out|target|bin|obj|\.venv|venv|__pycache__|\.next|coverage|migrations|generated)/'

if git rev-parse --git-dir >/dev/null 2>&1; then
  tracked="$(git ls-files --cached --others --exclude-standard)"
else
  tracked="$(find . -type f | sed 's|^\./||')"
fi

dirs="$(
  printf '%s\n' "$tracked" \
    | { grep -Ev "$SKIP_RE" || true; } \
    | { grep -E '/' || true; } \
    | awk -F/ 'NF > 1 { print $1 "/" ($2 ~ /\./ ? "" : $2) }' \
    | sed 's|/$||' | sort -u | { grep -v '^$' || true; }
)"

# A directory is covered if the map mentions it, or mentions any ancestor of it —
# a part that owns `artifacts/` covers `artifacts/feat-123/` without having to
# enumerate every child. Matching is on the leaf name, deliberately: the map uses
# compact notation, so full-path matching produces dozens of useless hits.
covered() {
  local path="$1"
  while [ -n "$path" ] && [ "$path" != "." ]; do
    grep -qF "$(basename "$path")" "$MAP" && return 0
    case "$path" in */*) path="${path%/*}" ;; *) path="" ;; esac
  done
  return 1
}

unassigned=0
while IFS= read -r d; do
  [ -n "$d" ] || continue
  covered "$d" || { warn "unassigned: $d/"; unassigned=$((unassigned + 1)); }
done <<EOF
$dirs
EOF
[ "$unassigned" -eq 0 ] && echo "every source directory is mentioned somewhere in the map"
echo

echo "errors: $errors  warnings: $warnings"
[ "$errors" -eq 0 ]
