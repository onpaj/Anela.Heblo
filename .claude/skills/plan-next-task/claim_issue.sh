#!/usr/bin/env bash
# Atomically claim a GitHub issue for the planning stage.
#
# The claim IS the remote `feature/<issue>-<slug>` branch: the ref is
# created through the GitHub refs API, which rejects an already-existing
# ref with 422, so when several runners race for the same issue exactly
# one wins. The label swap (`agent` -> <target-label>) that follows is
# advisory only -- it hides the issue from `--label agent` listings but is
# not the lock.
#
# On success prints the claimed branch name on stdout.
#
# Usage: claim_issue.sh <issue-number> <target-label>
# Exit codes:
#   0  claimed -- this runner owns the issue
#   3  already claimed -- a feature/<issue>-* branch exists on origin, or
#      another runner created the ref first (lost the race)
#   1  error, 2 usage
set -euo pipefail

# When USE_GH_API is set, every `gh` call below routes through the shared
# curl+REST library instead — for environments where the `gh` CLI itself is
# not permitted. See .claude/skills/_lib/gh_api.sh for the transport layer;
# the logic and exit codes here are unchanged either way.
LIB=".claude/skills/_lib/gh_api.sh"

ISSUE="${1:-}"
TARGET_LABEL="${2:-}"
if [[ -z "$ISSUE" || ! "$ISSUE" =~ ^[0-9]+$ || -z "$TARGET_LABEL" ]]; then
  echo "usage: claim_issue.sh <issue-number> <target-label>" >&2
  exit 2
fi

# Slug derivation -- must stay byte-identical to the oneshot naming
# convention (see Global Constraints in the implementation plan).
if [[ -n "${USE_GH_API:-}" ]]; then
  ISSUE_TITLE=$("$LIB" issue-view "$ISSUE" | jq -r '.title')
else
  ISSUE_TITLE=$(gh issue view "$ISSUE" --json title --jq '.title')
fi
SLUG=$(echo "$ISSUE_TITLE" \
  | sed -E "s/['’]//g" \
  | sed -E 's/[^A-Za-z0-9]+/ /g' \
  | awk '{for(i=1;i<=NF;i++) $i=toupper(substr($i,1,1)) tolower(substr($i,2)); print}' \
  | sed -E 's/ +/-/g; s/^-+|-+$//g' \
  | cut -c1-50 | sed -E 's/-+$//')
BRANCH="feature/${ISSUE}-${SLUG}"

# Any feature/<issue>-* branch on the remote means the issue is already
# taken (mid-flight or finished), even if the slug has drifted since it
# was created.
if [[ -n "$(git ls-remote --heads origin "feature/${ISSUE}-*")" ]]; then
  echo "issue #${ISSUE} already claimed: a feature/${ISSUE}-* branch exists on origin" >&2
  exit 3
fi

if [[ -n "${USE_GH_API:-}" ]]; then
  DEFAULT_BRANCH=$("$LIB" default-branch) \
    || { echo "ERROR: cannot resolve default branch" >&2; exit 1; }
else
  DEFAULT_BRANCH=$(gh repo view --json defaultBranchRef --jq '.defaultBranchRef.name') \
    || { echo "ERROR: cannot resolve default branch" >&2; exit 1; }
fi
BASE_SHA=$(git ls-remote origin "refs/heads/${DEFAULT_BRANCH}" | cut -f1) \
  || { echo "ERROR: cannot resolve origin/${DEFAULT_BRANCH}" >&2; exit 1; }
if [[ -z "$BASE_SHA" ]]; then
  echo "ERROR: cannot resolve origin/${DEFAULT_BRANCH}" >&2
  exit 1
fi

# Atomic test-and-set: creating a ref that already exists fails, so
# exactly one concurrent claimer succeeds no matter how tight the race.
if [[ -n "${USE_GH_API:-}" ]]; then
  create_ref_failed=$("$LIB" create-ref "$BRANCH" "$BASE_SHA" 2>&1 >/dev/null) && create_ref_ok=1 || create_ref_ok=0
else
  create_ref_failed=$(gh api "repos/{owner}/{repo}/git/refs" \
      -f ref="refs/heads/${BRANCH}" -f sha="${BASE_SHA}" 2>&1 >/dev/null) && create_ref_ok=1 || create_ref_ok=0
fi
if [[ "$create_ref_ok" -eq 0 ]]; then
  if grep -qi "already exists" <<<"$create_ref_failed"; then
    echo "issue #${ISSUE} already claimed: lost the race for ${BRANCH}" >&2
    exit 3
  fi
  echo "ERROR: failed to create claim ref ${BRANCH}: ${create_ref_failed}" >&2
  exit 1
fi

# The target label may not exist in the repo yet -- create it best-effort
# first (idempotent: already-exists is fine). Without this, `gh issue edit
# --add-label` errors on an unknown label, the swap below is silently
# swallowed by `|| true`, and the issue is left claimed (branch exists) but
# still labelled `agent` -- so the next candidate search picks the same
# issue again, this script sees the branch already exists and exits 3, and
# planning deadlocks on that one issue forever.
if [[ -n "${USE_GH_API:-}" ]]; then
  "$LIB" label-create "$TARGET_LABEL" 5319e7 "AgentHarness pipeline stage label" >/dev/null 2>&1 || true
else
  gh label create "$TARGET_LABEL" --color 5319e7 \
    --description "AgentHarness pipeline stage label" >/dev/null 2>&1 || true
fi

# Advisory visibility: swap agent -> <target-label> so `--label agent`
# listings stop returning this issue. A failure here does not undo the
# claim.
if [[ -n "${USE_GH_API:-}" ]]; then
  "$LIB" issue-edit "$ISSUE" --add-label "$TARGET_LABEL" --remove-label agent >/dev/null 2>&1 || true
else
  gh issue edit "$ISSUE" --add-label "$TARGET_LABEL" --remove-label agent >/dev/null 2>&1 || true
fi

echo "$BRANCH"
