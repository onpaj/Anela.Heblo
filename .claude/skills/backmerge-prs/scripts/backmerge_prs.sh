#!/usr/bin/env bash
# backmerge_prs.sh [<pr_number>] [--from <branch>]
# No args          → backmerge all open PRs using each PR's base branch
# --from <branch>  → use <branch> as the merge source instead of each PR's base
# <pr_number>      → limit to a single PR
# Compatible with bash 3.2+ (macOS default)
set -euo pipefail

SPECIFIC_PR=
FROM_BRANCH=

# Parse args
while [ $# -gt 0 ]; do
  case "$1" in
    --from)
      shift
      FROM_BRANCH="${1:-}"
      [ -z "$FROM_BRANCH" ] && { echo "Error: --from requires a branch name"; exit 1; }
      ;;
    -*)
      echo "Unknown flag: $1"; exit 1 ;;
    *)
      SPECIFIC_PR="$1" ;;
  esac
  shift
done

ORIGINAL_BRANCH=$(git rev-parse --abbrev-ref HEAD)

# Store results in a temp file (avoids associative arrays, works on bash 3.2)
RESULTS_FILE=$(mktemp)

cleanup() {
  rm -f "$RESULTS_FILE"
  git checkout "$ORIGINAL_BRANCH" 2>/dev/null || true
}
trap cleanup EXIT

echo "Fetching remote refs..."
git fetch --all --prune -q

# Fetch PR metadata
if [ -n "$SPECIFIC_PR" ]; then
  PR_JSON=$(gh pr view "$SPECIFIC_PR" \
    --json number,title,headRefName,baseRefName,state \
    | jq '[.]')
else
  PR_JSON=$(gh pr list --state open --limit 200 \
    --json number,title,headRefName,baseRefName,state \
    | jq 'sort_by(.number)')
fi

COUNT=$(echo "$PR_JSON" | jq 'length')

if [ "$COUNT" -eq 0 ]; then
  echo "No open PRs found."
  exit 0
fi

LABEL="${SPECIFIC_PR:+PR #$SPECIFIC_PR}"
LABEL="${LABEL:-all open PRs}"
if [ -n "$FROM_BRANCH" ]; then
  echo ""
  echo "Found $COUNT PR(s) — $LABEL (merging from: $FROM_BRANCH)"
else
  echo ""
  echo "Found $COUNT PR(s) — $LABEL"
fi
echo ""

# Try to auto-resolve straightforward conflicts after a failed merge.
# Returns 0 if all conflicts resolved and committed, 1 if manual work remains.
try_auto_resolve() {
  local HEAD="$1"
  local MERGE_SOURCE="$2"

  # Categorise conflicting files
  local AA_FILES UU_FILES ALL_UNMERGED
  AA_FILES=$(git status --short | awk '/^AA / {print $2}')
  UU_FILES=$(git status --short | awk '/^UU / {print $2}')
  ALL_UNMERGED=$(git diff --name-only --diff-filter=U)

  if [ -z "$AA_FILES" ] && [ -z "$UU_FILES" ]; then
    # Nothing to resolve (shouldn't happen, but be safe)
    return 1
  fi

  if [ -n "$UU_FILES" ]; then
    # Content conflicts require human judgment — bail out
    git merge --abort 2>/dev/null || true
    local conflict_list
    conflict_list=$(echo "$ALL_UNMERGED" | tr '\n' ' ')
    printf '%s\t%s\t%s\t%s\n' "$NUM" "$HEAD" "$MERGE_SOURCE" "conflict: $conflict_list" >> "$RESULTS_FILE"
    echo "   ✗ Content conflict (manual): $UU_FILES"
    return 1
  fi

  # Only add/add conflicts — take the base (--theirs) version for each
  local count=0
  while IFS= read -r f; do
    [ -z "$f" ] && continue
    git checkout --theirs -- "$f"
    git add "$f"
    count=$((count + 1))
  done <<< "$AA_FILES"

  git commit --no-edit -q
  git push origin "$HEAD" -q
  echo "   ✓ Done (auto-resolved $count add/add conflict(s), took base version)"
  printf '%s\t%s\t%s\t%s\n' "$NUM" "$HEAD" "$MERGE_SOURCE" "done (auto: $count add/add)" >> "$RESULTS_FILE"
  return 0
}

# Process each PR in a single jq-driven loop
while IFS=$'\t' read -r NUM HEAD BASE STATE; do
  # Determine which branch to merge from
  MERGE_SOURCE="${FROM_BRANCH:-$BASE}"

  echo "── PR #$NUM: $HEAD ← $MERGE_SOURCE ($STATE)"

  if [ "$STATE" = "MERGED" ] || [ "$STATE" = "CLOSED" ]; then
    echo "   ↷ Skipped ($STATE)"
    printf '%s\t%s\t%s\t%s\n' "$NUM" "$HEAD" "$MERGE_SOURCE" "skipped ($STATE)" >> "$RESULTS_FILE"
    continue
  fi

  # Skip if head branch is same as merge source
  if [ "$HEAD" = "$MERGE_SOURCE" ]; then
    echo "   ↷ Skipped (head == merge source)"
    printf '%s\t%s\t%s\t%s\n' "$NUM" "$HEAD" "$MERGE_SOURCE" "skipped (same branch)" >> "$RESULTS_FILE"
    continue
  fi

  # Checkout head branch
  git checkout "$HEAD" -q 2>/dev/null || git checkout -b "$HEAD" "origin/$HEAD" -q
  git pull origin "$HEAD" -q 2>/dev/null || true

  # Attempt clean merge
  if git merge "origin/$MERGE_SOURCE" --no-edit -q 2>/dev/null; then
    git push origin "$HEAD" -q
    echo "   ✓ Done"
    printf '%s\t%s\t%s\t%s\n' "$NUM" "$HEAD" "$MERGE_SOURCE" "done" >> "$RESULTS_FILE"
  else
    # Merge failed — try auto-resolution
    try_auto_resolve "$HEAD" "$MERGE_SOURCE" || true
  fi
done < <(echo "$PR_JSON" | jq -r '.[] | [.number, .headRefName, .baseRefName, .state] | @tsv')

echo ""
echo "═══════════════════════════════════════════════════════"
printf "%-6s %-40s %-36s %s\n" "PR" "Head branch" "Merged from" "Status"
echo "───────────────────────────────────────────────────────"
while IFS=$'\t' read -r NUM HEAD BASE STATUS; do
  printf "%-6s %-40s %-36s %s\n" "#$NUM" "$HEAD" "$BASE" "$STATUS"
done < "$RESULTS_FILE"
echo "═══════════════════════════════════════════════════════"

git checkout "$ORIGINAL_BRANCH" -q
echo ""
echo "Restored branch: $ORIGINAL_BRANCH"
