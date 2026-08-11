#!/usr/bin/env bash
# _lib/gh_api.sh — shared GitHub REST/GraphQL helper used by every AgentHarness
# skill script when USE_GH_API is set, so the pipeline still works in
# environments where the `gh` CLI is not permitted. Generalizes
# applicationinsightsscan/gh-api.sh's pattern (curl + GIT_PAT/GITHUB_TOKEN,
# repo auto-detected from `git remote get-url origin`) to the fuller surface
# the pipeline needs: issues, PRs, labels, comments, merges, and the handful
# of GraphQL-only fields (mergeStateStatus, statusCheckRollup, draft->ready)
# that gh's own --json flags expose but plain REST does not.
#
# Every "shaped" subcommand below (issue-view, pr-view, issue-list, pr-list)
# emits JSON using the SAME field names `gh ... --json <fields>` would, so
# callers pipe the exact jq filters they already use with `gh` unchanged —
# only the "how do I fetch this JSON" line differs between the two
# transports. Each such command emits a superset of commonly-needed fields
# rather than an exact --json-selected subset; the caller's own jq narrows
# it, exactly as it already does against real `gh` output.
#
# Usage: gh_api.sh <command> [args...] — run with no arguments for a list.
set -euo pipefail

detect_repo() {
  local url path
  url="$(git remote get-url origin 2>/dev/null)" || return 1
  [[ "$url" == *github.com* ]] || return 1
  path="${url#*github.com[:/]}"
  path="${path%.git}"
  path="${path%/}"
  [[ -n "$path" && "$path" == */* ]] || return 1
  echo "$path"
}

# Local/dev convenience: fall back to a gitignored .env at the repo root for
# secrets not already present in the environment. Real env vars (e.g. an Orca
# automation that injects them directly) always win over the .env file.
ENV_FILE="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)/.env"
if [[ -f "$ENV_FILE" ]]; then
  _pre_gh_repo="${GH_REPO:-}"
  _pre_git_pat="${GIT_PAT:-}"
  _pre_github_token="${GITHUB_TOKEN:-}"
  set -a
  # shellcheck disable=SC1090
  source "$ENV_FILE"
  set +a
  [[ -n "$_pre_gh_repo" ]] && GH_REPO="$_pre_gh_repo"
  [[ -n "$_pre_git_pat" ]] && GIT_PAT="$_pre_git_pat"
  [[ -n "$_pre_github_token" ]] && GITHUB_TOKEN="$_pre_github_token"
fi

REPO="${GH_REPO:-$(detect_repo || true)}"
API="https://api.github.com"
GRAPHQL_URL="https://api.github.com/graphql"
TOKEN="${GIT_PAT:-${GITHUB_TOKEN:-}}"

err() { echo "Error: $*" >&2; exit 1; }
need_repo() { [[ -n "$REPO" ]] || err "could not auto-detect the target GitHub repo (no origin remote pointing at github.com). Set GH_REPO=owner/repo."; }
[[ -n "$TOKEN" ]] || err "no token — set GIT_PAT (or GITHUB_TOKEN)."
command -v jq >/dev/null || err "jq is required."

# ---- core REST/GraphQL primitives -----------------------------------------

_api_url() {
  # Normalizes a path into a full URL: passes a full URL through unchanged,
  # and inserts the "/" GitHub's own REST paths start with when a caller
  # passes a bare `gh api`-style path (e.g. "repos/owner/repo/...", no
  # leading slash) — without this, "${API}${path}" silently glues onto
  # "api.github.com" with no separator.
  local path="$1"
  if [[ "$path" == http* ]]; then
    echo "$path"
  elif [[ "$path" == /* ]]; then
    echo "${API}${path}"
  else
    echo "${API}/${path}"
  fi
}

req() {
  # req METHOD PATH_OR_URL [json-body] [accept-header]
  # Retries transient 403/429 (GitHub's abuse/rate limiting) with backoff.
  # Prints body followed by a sentinel line carrying the HTTP status.
  local method="$1" path="$2" body="${3:-}" accept="${4:-application/vnd.github+json}"
  local url
  url=$(_api_url "$path")
  local args=(-sS --max-time 30 -X "$method"
    -H "Authorization: Bearer ${TOKEN}"
    -H "Accept: ${accept}"
    -H "X-GitHub-Api-Version: 2022-11-28"
    -w $'\n__HTTP_CODE__%{http_code}')
  [[ -n "$body" ]] && args+=(-d "$body")

  local out code delay=3 attempt
  for attempt in 1 2 3 4; do
    out="$(curl "${args[@]}" "$url")"
    code="${out##*__HTTP_CODE__}"
    if [[ "$code" != "403" && "$code" != "429" ]]; then
      printf '%s' "$out"; return 0
    fi
    [[ $attempt -eq 4 ]] && { printf '%s' "$out"; return 0; }
    echo "GitHub API ${code} (rate limit?) — retrying in ${delay}s..." >&2
    sleep "$delay"; delay=$((delay * 2))
  done
}

emit() {
  # emit RESPONSE [allow404]
  # Prints the JSON body and returns 0 on 2xx. On allow404 + a 404, prints
  # nothing and returns 1 without erroring (used for idempotent removals).
  # Any other non-2xx prints the response body's `.message` (if present)
  # to stderr and exits — matching `gh`'s own "fail loudly, let the
  # caller's `|| true` absorb it" behavior for best-effort calls.
  local resp="$1" allow404="${2:-}"
  local code="${resp##*__HTTP_CODE__}"
  local payload="${resp%__HTTP_CODE__*}"
  if [[ "$allow404" == "allow404" && "$code" == "404" ]]; then
    return 1
  fi
  if [[ ! "$code" =~ ^2 ]]; then
    local msg
    msg=$(echo "$payload" | jq -r '.message // empty' 2>/dev/null || true)
    err "GitHub API HTTP ${code}${msg:+: ${msg}}"
  fi
  echo "$payload"
}

req_paginate() {
  # req_paginate METHOD PATH — follows Link: rel="next", concatenates JSON
  # array pages into one array. Used where `gh api --paginate` is used today
  # (issue/PR comment lists that can exceed one page).
  local method="$1" path="$2"
  local url
  url=$(_api_url "$path")
  [[ "$url" == *"?"* ]] && url="${url}&per_page=100" || url="${url}?per_page=100"
  local all="[]" hdrfile body
  hdrfile=$(mktemp)
  while [[ -n "$url" ]]; do
    body=$(curl -sS --max-time 30 -X "$method" \
      -H "Authorization: Bearer ${TOKEN}" \
      -H "Accept: application/vnd.github+json" \
      -H "X-GitHub-Api-Version: 2022-11-28" \
      -D "$hdrfile" "$url")
    all=$(jq -c -n --argjson a "$all" --argjson b "$body" '$a + $b')
    url=$(grep -i '^link:' "$hdrfile" | grep -o '<[^>]*>; rel="next"' | sed -E 's/^<(.*)>.*/\1/' || true)
  done
  rm -f "$hdrfile"
  printf '%s' "$all"
}

graphql() {
  # graphql QUERY [VARS_JSON] — POSTs {query, variables} to /graphql, prints
  # the `.data` object. Errors (including GraphQL-level `errors[]`) exit.
  local query="$1" vars="${2:-{\}}"
  local payload
  payload=$(jq -n --arg q "$query" --argjson v "$vars" '{query:$q, variables:$v}')
  local resp data
  resp=$(req POST "$GRAPHQL_URL" "$payload")
  data=$(emit "$resp")
  if echo "$data" | jq -e '.errors' >/dev/null 2>&1; then
    err "GraphQL error: $(echo "$data" | jq -c '.errors')"
  fi
  echo "$data" | jq -c '.data'
}

# ---- jq shape defs ----------------------------------------------------------
# Field-name translation from GitHub REST's snake_case shape to the same
# camelCase field names `gh --json` produces, so existing call-site jq
# filters need no changes at all.

ISSUE_SHAPE_JQ='{
  number, title, body,
  state: (.state | ascii_upcase),
  createdAt: .created_at,
  labels: [(.labels // [])[] | {name}]
}'

# mergeable: REST `.mergeable` is true/false/null; gh's (GraphQL-backed)
# `.mergeable` is the enum MERGEABLE/CONFLICTING/UNKNOWN.
# mergeStateStatus: REST `.mergeable_state` is a lowercase string
# (behind/blocked/clean/dirty/draft/has_hooks/unknown/unstable); gh's is the
# same set of values, uppercased.
PR_SHAPE_JQ='{
  number, title, body,
  state: (if .state == "open" then "OPEN" else "CLOSED" end),
  isDraft: .draft,
  createdAt: .created_at,
  baseRefName: .base.ref,
  headRefName: .head.ref,
  additions, deletions,
  changedFiles: .changed_files,
  labels: [(.labels // [])[] | {name}],
  author: {login: .user.login},
  url: .html_url,
  mergeable: (if .mergeable == true then "MERGEABLE" elif .mergeable == false then "CONFLICTING" else "UNKNOWN" end),
  mergeStateStatus: ((.mergeable_state // "unknown") | ascii_upcase)
}'

# ---- issue commands ---------------------------------------------------------

issue_view() {
  need_repo
  local n="${1:?issue number required}"
  local resp
  resp=$(req GET "/repos/${REPO}/issues/${n}")
  emit "$resp" | jq -c "$ISSUE_SHAPE_JQ"
}

issue_list() {
  # issue_list STATE LABEL — mirrors `gh issue list --state S --label L
  # --limit 100 --json number,title,createdAt`. The REST /issues endpoint
  # also returns PRs; filtered out here (gh issue list never returns PRs).
  need_repo
  local state="${1:?state required}" label="${2:?label required}"
  local enc_label resp
  enc_label=$(jq -rn --arg l "$label" '$l|@uri')
  resp=$(req GET "/repos/${REPO}/issues?state=${state}&labels=${enc_label}&per_page=100")
  emit "$resp" | jq -c "[.[] | select(has(\"pull_request\") | not) | $ISSUE_SHAPE_JQ]"
}

issue_edit() {
  # issue_edit NUMBER [--add-label L]... [--remove-label L]...
  need_repo
  local n="${1:?issue number required}"; shift
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --add-label)
        local body resp
        body=$(jq -cn --arg l "$2" '[$l]')
        resp=$(req POST "/repos/${REPO}/issues/${n}/labels" "$body")
        emit "$resp" >/dev/null
        shift 2 ;;
      --remove-label)
        local enc resp
        enc=$(jq -rn --arg l "$2" '$l|@uri')
        resp=$(req DELETE "/repos/${REPO}/issues/${n}/labels/${enc}")
        emit "$resp" allow404 >/dev/null || true
        shift 2 ;;
      *) err "issue-edit: unknown argument $1" ;;
    esac
  done
}

label_create() {
  need_repo
  local name="${1:?label name required}" color="${2:?color required}" desc="${3:-}"
  local body resp
  body=$(jq -n --arg n "$name" --arg c "$color" --arg d "$desc" '{name:$n, color:$c, description:$d}')
  resp=$(req POST "/repos/${REPO}/labels" "$body")
  emit "$resp" >/dev/null
}

issue_create() {
  # issue_create --title T --label L1,L2,... --body-file F -- prints the
  # created issue's URL, like `gh issue create`'s own stdout.
  need_repo
  local title="" labels_csv="" body_file=""
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --title)     title="$2"; shift 2 ;;
      --label)     labels_csv="$2"; shift 2 ;;
      --body-file) body_file="$2"; shift 2 ;;
      *) err "issue-create: unknown argument $1" ;;
    esac
  done
  [[ -n "$title" ]] || err "issue-create: --title required"
  [[ -n "$body_file" ]] || err "issue-create: --body-file required"
  local body labels_json payload resp
  body=$(cat "$body_file")
  labels_json=$(jq -rn --arg l "$labels_csv" '($l | split(",") | map(select(length>0)))')
  payload=$(jq -n --arg t "$title" --arg b "$body" --argjson labels "$labels_json" \
    '{title:$t, body:$b, labels:$labels}')
  resp=$(req POST "/repos/${REPO}/issues" "$payload")
  emit "$resp" | jq -r '.html_url'
}

# ---- PR commands -------------------------------------------------------------

_resolve_pr_number() {
  # A PR ref can be a bare number, a full PR URL, or (like `gh pr view
  # <branch>`) a head branch name — resolve any of the three to a number.
  need_repo
  local ref="$1"
  if [[ "$ref" =~ ^[0-9]+$ ]]; then echo "$ref"; return 0; fi
  if [[ "$ref" =~ /pull/([0-9]+) ]]; then echo "${BASH_REMATCH[1]}"; return 0; fi
  local owner enc resp n
  owner="${REPO%%/*}"
  enc=$(jq -rn --arg h "${owner}:${ref}" '$h|@uri')
  resp=$(req GET "/repos/${REPO}/pulls?head=${enc}&state=all&per_page=1")
  n=$(emit "$resp" | jq -r '.[0].number // empty')
  [[ -n "$n" ]] || err "no PR found for branch '${ref}'"
  echo "$n"
}

pr_view() {
  # pr_view REF [EXTRA_FIELDS_CSV]
  # EXTRA_FIELDS_CSV gates the expensive calls (statusCheckRollup needs 2
  # extra requests, reviewDecision needs 1, files needs 1) so callers that
  # don't need them don't pay for them.
  need_repo
  local ref="${1:?pr ref required}" extra="${2:-}"
  local n resp pr sha out
  n=$(_resolve_pr_number "$ref")
  resp=$(req GET "/repos/${REPO}/pulls/${n}")
  pr=$(emit "$resp")
  out=$(echo "$pr" | jq -c "$PR_SHAPE_JQ + {number: $n}")

  if [[ "$extra" == *statusCheckRollup* ]]; then
    sha=$(echo "$pr" | jq -r '.head.sha')
    local runs_resp status_resp rollup
    runs_resp=$(req GET "/repos/${REPO}/commits/${sha}/check-runs")
    status_resp=$(req GET "/repos/${REPO}/commits/${sha}/status")
    rollup=$(jq -cn \
      --argjson runs "$(emit "$runs_resp")" \
      --argjson status "$(emit "$status_resp")" \
      '[($runs.check_runs // [])[] | {__typename:"CheckRun", status: (.status | ascii_upcase), conclusion: (.conclusion // "" | ascii_upcase)}]
       + [($status.statuses // [])[] | {__typename:"StatusContext", state: (.state | ascii_upcase)}]')
    out=$(echo "$out" | jq -c --argjson r "$rollup" '. + {statusCheckRollup: $r}')
  fi

  if [[ "$extra" == *reviewDecision* ]]; then
    local reviews_resp decision
    reviews_resp=$(req GET "/repos/${REPO}/pulls/${n}/reviews?per_page=100")
    decision=$(emit "$reviews_resp" | jq -r '
      ([group_by(.user.login)[] | max_by(.submitted_at) | select(.state != "COMMENTED")]) as $latest
      | if ($latest | any(.state=="CHANGES_REQUESTED")) then "CHANGES_REQUESTED"
        elif ($latest | any(.state=="APPROVED")) then "APPROVED"
        else null end')
    out=$(echo "$out" | jq -c --argjson d "${decision:-null}" '. + {reviewDecision: $d}')
  fi

  if [[ "$extra" == *files* ]]; then
    local files_resp files
    files_resp=$(req GET "/repos/${REPO}/pulls/${n}/files?per_page=100")
    files=$(emit "$files_resp" | jq -c '[.[] | {path: .filename}]')
    out=$(echo "$out" | jq -c --argjson f "$files" '. + {files: $f}')
  fi

  echo "$out"
}

pr_edit() {
  # pr_edit REF [--add-label L]... [--remove-label L]... [--title T] [--body B]
  need_repo
  local ref="${1:?pr ref required}"; shift
  local n; n=$(_resolve_pr_number "$ref")
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --add-label)
        local body resp
        body=$(jq -cn --arg l "$2" '[$l]')
        resp=$(req POST "/repos/${REPO}/issues/${n}/labels" "$body")
        emit "$resp" >/dev/null
        shift 2 ;;
      --remove-label)
        local enc resp
        enc=$(jq -rn --arg l "$2" '$l|@uri')
        resp=$(req DELETE "/repos/${REPO}/issues/${n}/labels/${enc}")
        emit "$resp" allow404 >/dev/null || true
        shift 2 ;;
      --title)
        local body resp
        body=$(jq -n --arg t "$2" '{title:$t}')
        resp=$(req PATCH "/repos/${REPO}/pulls/${n}" "$body")
        emit "$resp" >/dev/null
        shift 2 ;;
      --body)
        local body resp
        body=$(jq -n --arg b "$2" '{body:$b}')
        resp=$(req PATCH "/repos/${REPO}/pulls/${n}" "$body")
        emit "$resp" >/dev/null
        shift 2 ;;
      *) err "pr-edit: unknown argument $1" ;;
    esac
  done
}

pr_create() {
  # pr_create BASE HEAD TITLE BODY_FILE [LABEL] — prints the PR URL, like
  # `PR_URL=$(gh pr create ...)`.
  need_repo
  local base="${1:?base required}" head="${2:?head required}" title="${3:?title required}" body_file="${4:?body file required}" label="${5:-}"
  local body payload resp pr n url
  body=$(cat "$body_file")
  payload=$(jq -n --arg t "$title" --arg b "$body" --arg h "$head" --arg base "$base" \
    '{title:$t, body:$b, head:$h, base:$base, draft:true}')
  resp=$(req POST "/repos/${REPO}/pulls" "$payload")
  pr=$(emit "$resp")
  n=$(echo "$pr" | jq -r '.number')
  url=$(echo "$pr" | jq -r '.html_url')
  if [[ -n "$label" ]]; then
    local lbody lresp
    lbody=$(jq -cn --arg l "$label" '[$l]')
    lresp=$(req POST "/repos/${REPO}/issues/${n}/labels" "$lbody")
    emit "$lresp" >/dev/null
  fi
  echo "$url"
}

pr_comment() {
  need_repo
  local ref="${1:?pr ref required}" body_file="${2:?body file required}"
  local n body payload resp
  n=$(_resolve_pr_number "$ref")
  body=$(cat "$body_file")
  payload=$(jq -n --arg b "$body" '{body:$b}')
  resp=$(req POST "/repos/${REPO}/issues/${n}/comments" "$payload")
  emit "$resp" >/dev/null
}

pr_merge() {
  # pr_merge REF [--squash|--merge|--rebase] [--delete-branch]
  need_repo
  local ref="${1:?pr ref required}"; shift
  local method="squash" delete_branch=false
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --squash) method="squash"; shift ;;
      --merge) method="merge"; shift ;;
      --rebase) method="rebase"; shift ;;
      --delete-branch) delete_branch=true; shift ;;
      *) err "pr-merge: unknown argument $1" ;;
    esac
  done
  local n pr_resp pr head_ref payload resp
  n=$(_resolve_pr_number "$ref")
  payload=$(jq -n --arg m "$method" '{merge_method:$m}')
  resp=$(req PUT "/repos/${REPO}/pulls/${n}/merge" "$payload")
  emit "$resp" >/dev/null
  if $delete_branch; then
    pr_resp=$(req GET "/repos/${REPO}/pulls/${n}")
    pr=$(emit "$pr_resp")
    head_ref=$(echo "$pr" | jq -r '.head.ref')
    local enc del_resp
    enc=$(jq -rn --arg r "heads/${head_ref}" '$r|@uri')
    del_resp=$(req DELETE "/repos/${REPO}/git/refs/${enc}")
    emit "$del_resp" allow404 >/dev/null || true
  fi
}

pr_ready() {
  # No REST endpoint exists for undrafting a PR — GraphQL only.
  need_repo
  local ref="${1:?pr ref required}"
  local n resp pr node_id
  n=$(_resolve_pr_number "$ref")
  resp=$(req GET "/repos/${REPO}/pulls/${n}")
  pr=$(emit "$resp")
  node_id=$(echo "$pr" | jq -r '.node_id')
  graphql 'mutation($id:ID!){markPullRequestReadyForReview(input:{pullRequestId:$id}){pullRequest{id}}}' \
    "$(jq -n --arg id "$node_id" '{id:$id}')" >/dev/null
}

pr_update_branch() {
  need_repo
  local ref="${1:?pr ref required}"
  local n resp
  n=$(_resolve_pr_number "$ref")
  resp=$(req PUT "/repos/${REPO}/pulls/${n}/update-branch" "{}")
  emit "$resp" >/dev/null
}

pr_diff() {
  need_repo
  local ref="${1:?pr ref required}"
  local n resp code payload
  n=$(_resolve_pr_number "$ref")
  resp=$(req GET "/repos/${REPO}/pulls/${n}" "" "application/vnd.github.v3.diff")
  code="${resp##*__HTTP_CODE__}"
  payload="${resp%__HTTP_CODE__*}"
  [[ "$code" =~ ^2 ]] || err "GitHub API HTTP ${code} fetching diff for PR ${n}"
  printf '%s' "$payload"
}

pr_list() {
  # pr_list STATE LABEL_CSV — mirrors `gh pr list --state S --label L1
  # --label L2 --limit 100 --json <full common set>`, including
  # reviewDecision. Uses the /issues search endpoint for label-AND
  # filtering (the /pulls endpoint has no label filter), then enriches
  # each match with the full PR object — the same per-candidate cost
  # rework-pr's own comment-history lookups already pay.
  need_repo
  local state="${1:?state required}" labels_csv="${2:?labels required}"
  local enc_labels resp numbers out
  enc_labels=$(jq -rn --arg l "$labels_csv" '$l|@uri')
  resp=$(req GET "/repos/${REPO}/issues?state=${state}&labels=${enc_labels}&per_page=100")
  numbers=$(emit "$resp" | jq -r '[.[] | select(has("pull_request"))] | .[].number')
  out="[]"
  for n in $numbers; do
    local entry
    entry=$(pr_view "$n" "reviewDecision")
    out=$(jq -c -n --argjson a "$out" --argjson e "$entry" '$a + [$e]')
  done
  echo "$out"
}

# ---- repo commands ------------------------------------------------------------

repo_print() { need_repo; echo "$REPO"; }

default_branch() {
  need_repo
  local resp
  resp=$(req GET "/repos/${REPO}")
  emit "$resp" | jq -r '.default_branch'
}

compare_behind_by() {
  need_repo
  local base="${1:?base required}" head="${2:?head required}"
  local resp
  resp=$(req GET "/repos/${REPO}/compare/${base}...${head}")
  emit "$resp" | jq -r '.behind_by // 0'
}

create_ref() {
  # create_ref BRANCH SHA — atomic test-and-set claim: creating a ref that
  # already exists fails with 422 "Reference already exists", exactly like
  # `gh api repos/{owner}/{repo}/git/refs -f ref=... -f sha=...` does.
  need_repo
  local branch="${1:?branch required}" sha="${2:?sha required}"
  local body resp
  body=$(jq -n --arg r "refs/heads/${branch}" --arg s "$sha" '{ref:$r, sha:$s}')
  resp=$(req POST "/repos/${REPO}/git/refs" "$body")
  emit "$resp" >/dev/null
}

# ---- dispatch -------------------------------------------------------------

cmd="${1:-}"; shift || true
case "$cmd" in
  repo)                 repo_print ;;
  default-branch)        default_branch ;;
  compare-behind-by)     compare_behind_by "$@" ;;
  create-ref)            create_ref "$@" ;;
  GET|POST|PATCH|PUT|DELETE) emit "$(req "$cmd" "$@")" ;;
  paginate)              req_paginate GET "$1" ;;
  graphql)               graphql "$@" ;;
  issue-view)            issue_view "$@" ;;
  issue-list)            issue_list "$@" ;;
  issue-edit)            issue_edit "$@" ;;
  label-create)          label_create "$@" ;;
  issue-create)          issue_create "$@" ;;
  pr-view)               pr_view "$@" ;;
  pr-edit)               pr_edit "$@" ;;
  pr-create)             pr_create "$@" ;;
  pr-comment)            pr_comment "$@" ;;
  pr-merge)              pr_merge "$@" ;;
  pr-ready)              pr_ready "$@" ;;
  pr-update-branch)      pr_update_branch "$@" ;;
  pr-diff)               pr_diff "$@" ;;
  pr-list)               pr_list "$@" ;;
  ""|-h|--help)
    sed -n '2,20p' "$0" ;;
  *)
    err "unknown command '${cmd}'. Try: repo, default-branch, GET, POST, PATCH, PUT, DELETE, paginate, graphql, issue-view, issue-list, issue-edit, issue-create, label-create, pr-view, pr-edit, pr-create, pr-comment, pr-merge, pr-ready, pr-update-branch, pr-diff, pr-list, compare-behind-by, create-ref." ;;
esac
