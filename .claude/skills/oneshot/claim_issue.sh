#!/usr/bin/env bash
# Atomically claim a GitHub issue for the oneshot pipeline.
#
# The claim IS the remote `feature/<issue>-<slug>` branch: the ref is created
# through the GitHub refs API, which rejects an already-existing ref with 422,
# so when several runners race for the same issue exactly one wins. The label
# swap (`agent` -> `agent-wip`) that follows is advisory only — it hides the
# issue from `--label agent` listings but is not the lock.
#
# On success prints the claimed branch name on stdout.
#
# Usage: claim_issue.sh <issue-number>
# Exit codes:
#   0  claimed — this runner owns the issue
#   3  already claimed — a feature/<issue>-* branch exists on origin, or
#      another runner created the ref first (lost the race)
#   1  error, 2 usage
set -euo pipefail

ISSUE="${1:-}"
if [[ -z "$ISSUE" || ! "$ISSUE" =~ ^[0-9]+$ ]]; then
  echo "usage: claim_issue.sh <issue-number>" >&2
  exit 2
fi

# Slug derivation — must stay byte-identical to the oneshot naming convention.
SLUG=$(gh issue view "$ISSUE" --json title --jq '.title' \
  | sed -E "s/['’]//g" \
  | sed -E 's/[^A-Za-z0-9]+/ /g' \
  | awk '{for(i=1;i<=NF;i++) $i=toupper(substr($i,1,1)) tolower(substr($i,2)); print}' \
  | sed -E 's/ +/-/g; s/^-+|-+$//g' \
  | cut -c1-50 | sed -E 's/-+$//')
BRANCH="feature/${ISSUE}-${SLUG}"

# Any feature/<issue>-* branch on the remote means the issue is already taken
# (mid-flight or finished), even if the slug has drifted since it was created.
if [[ -n "$(git ls-remote --heads origin "feature/${ISSUE}-*")" ]]; then
  echo "issue #${ISSUE} already claimed: a feature/${ISSUE}-* branch exists on origin" >&2
  exit 3
fi

DEFAULT_BRANCH=$(gh repo view --json defaultBranchRef --jq '.defaultBranchRef.name')
BASE_SHA=$(git ls-remote origin "refs/heads/${DEFAULT_BRANCH}" | cut -f1)
if [[ -z "$BASE_SHA" ]]; then
  echo "ERROR: cannot resolve origin/${DEFAULT_BRANCH}" >&2
  exit 1
fi

# Atomic test-and-set: creating a ref that already exists fails, so exactly
# one concurrent claimer succeeds no matter how tight the race.
if ! err=$(gh api "repos/{owner}/{repo}/git/refs" \
      -f ref="refs/heads/${BRANCH}" -f sha="${BASE_SHA}" 2>&1 >/dev/null); then
  if grep -qi "already exists" <<<"$err"; then
    echo "issue #${ISSUE} already claimed: lost the race for ${BRANCH}" >&2
    exit 3
  fi
  echo "ERROR: failed to create claim ref ${BRANCH}: ${err}" >&2
  exit 1
fi

# Advisory visibility: swap agent -> agent-wip so `--label agent` listings
# stop returning this issue. A failure here does not undo the claim.
gh issue edit "$ISSUE" --add-label agent-wip --remove-label agent >/dev/null 2>&1 || true

echo "$BRANCH"
