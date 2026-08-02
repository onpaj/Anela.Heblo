#!/usr/bin/env bash
#
# test-health-digest.sh — deterministic test-health digest from ReportPortal.
#
# The gathering half of the test-health routine: queries ReportPortal for the
# launches in a window plus their failed items, computes findings, and prints a
# Markdown digest ending in a machine-readable STATE line.
#
# Absence of expected data is computed HERE, not left to the agent: a nightly
# that never ran is the failure mode this routine exists to catch.
#
# Usage:
#   test-health-digest.sh [--days N] [--state-only]
#
# Env: RP_API_KEY / RP_ENDPOINT / RP_PROJECT (see rp-query.sh),
#      RP_FIXTURE_DIR, GH_FIXTURE_DIR (offline replay),
#      TEST_HEALTH_STATE_FILE (default $HOME/.cache/test-health/state)
#
# Exit codes: 0 ok | 1 config error | 3 RP unreachable | 4 RP auth rejected
#             5 unexpected RP HTTP status (404/429/5xx)
#
set -uo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RP="${HERE}/rp-query.sh"
DAYS=7
STATE_ONLY=0
STATE_FILE="${TEST_HEALTH_STATE_FILE:-$HOME/.cache/test-health/state}"

err()  { echo "Error: $*" >&2; exit 1; }
errc() { local c="$1"; shift; echo "Error: $*" >&2; exit "$c"; }

command -v jq >/dev/null || err "jq is required (expected on PATH, e.g. /Users/rem/.local/bin)."
# perl is not decoration: normalize_error's hex-id rule needs a lookahead that
# sed -E cannot express. Without this guard its absence is silent and total —
# the pipeline yields an empty string for every error line, so every regression
# and chronic fingerprint in the run collapses onto sha256("") and dedup drops
# everything after the first.
command -v perl >/dev/null || err "perl is required for normalize_error's hex-id rule."
[[ -x "$RP" ]] || err "${RP} not found or not executable."

while [[ $# -gt 0 ]]; do
  case "$1" in
    --days)       DAYS="${2:?--days requires a number}"; shift 2 ;;
    --state-only) STATE_ONLY=1; shift ;;
    -h|--help)    awk 'NR>1 && /^#/ {sub(/^# ?/,""); print; next} NR>1 {exit}' "$0"; exit 0 ;;
    *)            err "unknown argument '$1'." ;;
  esac
done

# BSD date first (macOS host), GNU date as fallback.
day_offset() { # day_offset N -> the UTC date N days ago as YYYY-MM-DD
  date -u -v-"${1}"d +%Y-%m-%d 2>/dev/null || date -u -d "${1} days ago" +%Y-%m-%d 2>/dev/null \
    || err "neither BSD nor GNU date available."
}
fmt_epoch() { # fmt_epoch <epoch-seconds> -> ISO-8601 UTC
  date -u -r "$1" +%Y-%m-%dT%H:%M:%SZ 2>/dev/null \
    || date -u -d "@$1" +%Y-%m-%dT%H:%M:%SZ 2>/dev/null \
    || echo "epoch:$1"
}

GH="${HERE}/gh-api.sh"

# A GitHub failure must be distinguishable from "GitHub says there are no runs".
# Collapsing the two is the same mistake as reporting an unreadable ReportPortal
# as "the tests did not run": it would make a GitHub outage look like proof that
# the nightly was never scheduled, defeat cascade suppression, and file one
# issue per stale module — eleven harness:todo issues, eleven PRs, one cause.
GH_UNREACHABLE='{"workflow_runs":[],"__gh_error":true}'

# Fetch a GitHub REST path, honouring GH_FIXTURE_DIR for offline replay. Uses
# the same file-naming rule as rp-query.sh so fixtures are predictable.
gh_get() {
  local p="$1" out rc
  if [[ -n "${GH_FIXTURE_DIR:-}" ]]; then
    local name file
    name="$(printf '%s' "${p#/}" | sed 's/[^A-Za-z0-9._-]/_/g')"
    file="${GH_FIXTURE_DIR}/${name}.json"
    if [[ -f "$file" ]]; then cat "$file"; else echo '{"workflow_runs":[]}'; fi
    return 0
  fi
  [[ -x "$GH" ]] || { echo "$GH_UNREACHABLE"; return 0; }
  out="$("$GH" GET "$p" 2>/dev/null)"; rc=$?
  if [[ $rc -ne 0 ]]; then echo "$GH_UNREACHABLE"; return 0; fi
  printf '%s' "$out"
}

TODAY="$(date -u +%Y-%m-%d)"
mkdir -p "$(dirname "$STATE_FILE")" 2>/dev/null || true

state_field() { # state_field <key> -> value from the state file, or empty
  [[ -f "$STATE_FILE" ]] || return 0
  sed -n "s/^$1=//p" "$STATE_FILE" | head -1
}

# A routine that cannot run must get LOUDER, not quieter. telemetry-anomaly
# reported "done" every day for weeks while entirely broken, because an
# unchanged state produced an identical, deduplicated non-event. Counting
# consecutive error days makes the state change daily, so harness dispatches
# a fresh, escalating task instead of swallowing it.
record_error_and_exit() { # record_error_and_exit <exit-code> <message>
  local code="$1" msg="$2" prev_date prev_n yesterday n
  prev_date="$(state_field lastErrorDate)"
  prev_n="$(state_field errDays)"; [[ -n "$prev_n" ]] || prev_n=0
  yesterday="$(day_offset 1)"
  if [[ "$prev_date" == "$TODAY" ]]; then n="$prev_n"          # re-run same day
  elif [[ "$prev_date" == "$yesterday" ]]; then n=$((prev_n + 1))
  else n=1; fi
  printf 'lastErrorDate=%s\nerrDays=%s\n' "$TODAY" "$n" > "$STATE_FILE" 2>/dev/null || true
  echo "STATE: error=${code}:errdays=${n}"
  echo "Error: ${msg} (exit ${code}, consecutive error days: ${n})" >&2
  exit "$code"
}

clear_error_days() {
  printf 'lastErrorDate=\nerrDays=0\n' > "$STATE_FILE" 2>/dev/null || true
}

# "Now" is overridable so that fixtures can pin it. Fixtures carry absolute
# timestamps; a window computed from wall-clock now would slide past them within
# days and the suite would rot into a false pass — launches silently filtered
# out, zero findings, everything "green". Tests set TEST_HEALTH_NOW_MS.
NOW_MS="${TEST_HEALTH_NOW_MS:-$(( $(date -u +%s) * 1000 ))}"
WINDOW_START_MS=$(( NOW_MS - DAYS * 86400 * 1000 ))

# ---------------------------------------------------------------- gather ----
# One page of 300 launches, newest first, then filter to the window with jq.
# Filtering client-side keeps us off RP's filter-parameter grammar, which is
# version-sensitive and unverified against this instance.
raw_launches="$("$RP" '/launch?page.size=300&page.sort=startTime,DESC')"; rc=$?
if [[ $rc -ne 0 ]]; then
  # 3 = unreachable, 4 = auth. Either way we know NOTHING about test presence,
  # so we must not emit silence findings. Record the error day and stop.
  record_error_and_exit "$rc" "could not read launches from ReportPortal"
fi

launches="$(printf '%s' "$raw_launches" | jq --argjson since "$WINDOW_START_MS" '
  [ .content[]?
    | select(.startTime >= $since)
    | { id, name, number, status, startTime,
        layer:  ((.attributes[]? | select(.key=="layer")  | .value) // "unknown"),
        module: ((.attributes[]? | select(.key=="module") | .value) // "-"),
        branch: ((.attributes[]? | select(.key=="branch") | .value) // "unknown"),
        ci:     ((.attributes[]? | select(.key=="ci")     | .value) // "-"),
        total:  (.statistics.executions.total   // 0),
        passed: (.statistics.executions.passed  // 0),
        failed: (.statistics.executions.failed  // 0),
        skipped:(.statistics.executions.skipped // 0) }
    | select(.branch == "main" or .branch == "unknown") ]
')" || err "could not parse the launch payload as JSON."

# Failed items per launch. Cheap: only failures are fetched, never full item
# lists. Pass/fail history per test is then derived from set membership.
failed_items='[]'
for id in $(printf '%s' "$launches" | jq -r '.[].id'); do
  p="/item?filter.eq.launchId=${id}&filter.in.status=FAILED,INTERRUPTED&page.size=300"
  # MUST route through record_error_and_exit, exactly like the launches call.
  # A partial RP outage — launch list serving, item queries failing — is a real
  # shape, and taking the plain errc() path here would print no STATE line and
  # leave the error-day counter frozen, so the scheduler would dedup the failure
  # into silence. That is the precise failure this counter exists to prevent.
  body="$("$RP" "$p")" || record_error_and_exit $? "could not read failed items for launch ${id}"
  chunk="$(printf '%s' "$body" | jq --argjson lid "$id" '
    [ .content[]? | { launchId: $lid,
                      name: (.name // "unknown"),
                      path: (.pathNames.itemPaths // [] | map(.name) | join(" > ")),
                      status: (.status // "FAILED"),
                      issue: ((.issue.comment // "") ),
                      error: ((.description // "") | split("\n")[0] // "") } ]
  ')" || err "could not parse items for launch ${id}."
  failed_items="$(jq -n --argjson a "$failed_items" --argjson b "$chunk" '$a + $b')"
done

# --------------------------------------------------------------- findings ---
# Findings are accumulated as JSON objects: {category, layer, module, fingerprint, headline, detail}
findings='[]'
add_finding() { findings="$(jq -n --argjson a "$findings" --argjson b "$1" '$a + [$b]')"; }

# --- presence: which layer/module reported recently vs. has a baseline? ---
# A layer/module earns an expectation by having reported at least once in the
# window. E2E is nightly, so its freshness horizon is 26h; the push-triggered
# layers get the full window (a quiet week on main is not a fault).
E2E_FRESH_MS=$(( 26 * 3600 * 1000 ))

expected="$(printf '%s' "$launches" | jq --argjson now "$NOW_MS" --argjson fresh "$E2E_FRESH_MS" '
  group_by(.layer + "/" + .module)
  | map({ layer: .[0].layer, module: .[0].module,
          newest: (map(.startTime) | max),
          runs: length })
  | map(. + { stale: (if .layer == "e2e" then (($now - .newest) > $fresh) else false end) })
')"

stale_e2e="$(printf '%s' "$expected" | jq '[ .[] | select(.stale) ] | length')"
fresh_e2e="$(printf '%s' "$expected" | jq '[ .[] | select(.layer=="e2e" and (.stale|not)) ] | length')"

# CI cross-check, run once: did the nightly workflow fail before the tests?
nightly='{"workflow_runs":[]}'
if [[ "$stale_e2e" -gt 0 ]]; then
  nightly="$(gh_get "/repos/onpaj/Anela.Heblo/actions/workflows/e2e-nightly-regression.yml/runs?per_page=5")"
fi
# Trust "none" only from a body that is actually shaped like a runs response.
# A 2xx page that parses as JSON but carries no workflow_runs array — schema
# drift, a proxy interstitial, an error envelope — would otherwise evaluate to
# "none" and let us assert a schedule fault we never confirmed. The test is
# validity, not merely the absence of our own error sentinel.
gh_ok="$(printf '%s' "$nightly" | jq -r '
  if (.__gh_error // false) then "no"
  elif (.workflow_runs | type) == "array" then "yes"
  else "no" end' 2>/dev/null || echo no)"

if [[ "$gh_ok" != "yes" ]]; then
  nightly_concl="unknown"
  nightly_id=""
else
  # Guarded the same way as gh_ok. A well-typed array holding a malformed
  # element would jq-error here and leave an empty string — a fourth state that
  # is neither failure, success, none nor unknown, and which falls through to
  # the same defaults as a confirmed "none". Default it to unknown instead.
  nightly_concl="$(printf '%s' "$nightly" | jq -r '.workflow_runs[0].conclusion // "none"' 2>/dev/null)"
  [[ -n "$nightly_concl" ]] || nightly_concl="unknown"
  nightly_id="$(printf '%s' "$nightly" | jq -r '.workflow_runs[0].id // empty' 2>/dev/null)"
fi

failing_step="unknown"
if [[ "$nightly_concl" == "failure" && -n "$nightly_id" ]]; then
  jobs="$(gh_get "/repos/onpaj/Anela.Heblo/actions/runs/${nightly_id}/jobs")"
  failing_step="$(printf '%s' "$jobs" | jq -r '
    [ .jobs[]?.steps[]? | select(.conclusion=="failure") | .name ] | first // "unknown"')"
fi

# Cascade rule: if EVERY e2e module is stale and the workflow failed, this is
# one infrastructure fault, not eleven missing modules. File once.
if [[ "$stale_e2e" -gt 0 && "$fresh_e2e" -eq 0 && "$nightly_concl" == "failure" ]]; then
  add_finding "$(jq -n --arg s "$failing_step" --argjson n "$stale_e2e" '{
    category: "ci-broken",
    layer: "e2e", module: "-",
    fingerprint: ("test-ci:e2e-nightly-regression.yml:" + $s),
    headline: ("E2E nightly failed at step \"" + $s + "\" — no tests ran, " + ($n|tostring) + " module(s) have no data"),
    detail: "The workflow run failed before the test step, so ReportPortal received nothing. Resolution is outside the repository (credential/config), not a code change."
  }')"
elif [[ "$stale_e2e" -gt 0 && "$fresh_e2e" -eq 0 && "$nightly_concl" == "unknown" ]]; then
  # ReportPortal was readable and genuinely holds no E2E data — that much is
  # fact. What we cannot do is attribute it, because GitHub did not answer.
  # Still exactly ONE finding: the cascade rule is about not multiplying a
  # single unknown cause into eleven issues, and an unattributable outage is
  # no less single than an attributable one.
  add_finding "$(jq -n --argjson n "$stale_e2e" '{
    category: "ci-broken",
    layer: "e2e", module: "-",
    fingerprint: "test-ci:e2e-nightly-regression.yml:unattributed",
    headline: ("E2E data missing for " + ($n|tostring) + " module(s); GitHub could not be reached to attribute the cause"),
    detail: "ReportPortal was readable and holds no recent E2E launches, so the data really is absent. The GitHub Actions API could not be queried, so whether the nightly failed, never ran, or ran without reporting is undetermined. Check the workflow run history by hand."
  }')"
else
  # Otherwise report each stale module individually, distinguishing "the
  # workflow never ran" from "it ran fine but reporting did not arrive".
  for row in $(printf '%s' "$expected" | jq -r '.[] | select(.stale) | @base64'); do
    d="$(printf '%s' "$row" | base64 --decode)"
    l="$(printf '%s' "$d" | jq -r '.layer')"
    m="$(printf '%s' "$d" | jq -r '.module')"
    if [[ "$nightly_concl" == "unknown" ]]; then
      # Its own category, not schedule-broken. schedule-broken asserts a
      # specific claim — that the run was never scheduled — and a consumer
      # keying on `category` rather than reading the prose would receive a
      # confident diagnosis this finding's own text refuses to make.
      cat_name="silence-unattributed"; suffix="unattributed"
      head_txt="${l}/${m}: no launch in the last 26h, cause undetermined"
      det="ReportPortal holds no recent launch for this module. The GitHub Actions API could not be queried, so whether the run failed, never ran, or ran without reporting is undetermined."
    elif [[ "$nightly_concl" == "success" ]]; then
      cat_name="rp-reporting-broken"; suffix="reporting"
      head_txt="${l}/${m}: workflow succeeded but no ReportPortal launch arrived"
      det="The nightly run completed successfully, so the tests ran — the reporting agent or the tailnet hop failed."
    else
      cat_name="schedule-broken"; suffix="schedule"
      head_txt="${l}/${m}: no launch in the last 26h despite a 7-day baseline"
      det="No recent launch and no successful workflow run explains it. The nightly may not have been scheduled at all."
    fi
    add_finding "$(jq -n --arg c "$cat_name" --arg l "$l" --arg m "$m" --arg s "$suffix" \
                          --arg h "$head_txt" --arg dt "$det" '{
      category: $c, layer: $l, module: $m,
      fingerprint: ("test-silence:" + $l + ":" + $m + ":" + $s),
      headline: $h, detail: $dt }')"
  done
fi

# --- suite shrink: newest run materially smaller than the window median ---
SHRINK_PCT=20
for key in $(printf '%s' "$launches" | jq -r '[ .[] | .layer + "/" + .module ] | unique | .[]'); do
  l="${key%%/*}"; m="${key##*/}"
  series="$(printf '%s' "$launches" | jq --arg l "$l" --arg m "$m" \
    '[ .[] | select(.layer==$l and .module==$m) ] | sort_by(-.startTime)')"
  n="$(printf '%s' "$series" | jq 'length')"
  [[ "$n" -ge 3 ]] || continue   # no baseline, no claim
  newest="$(printf '%s' "$series" | jq '.[0].total')"
  median="$(printf '%s' "$series" | jq '[ .[1:][].total ] | sort | .[ (length/2 | floor) ]')"
  [[ "$median" -gt 0 ]] || continue
  drop=$(( (median - newest) * 100 / median ))
  if [[ "$drop" -ge "$SHRINK_PCT" ]]; then
    add_finding "$(jq -n --arg l "$l" --arg m "$m" --argjson nw "$newest" --argjson md "$median" --argjson d "$drop" '{
      category: "suite-shrank", layer: $l, module: $m,
      fingerprint: ("test-shrink:" + $l + ":" + $m),
      headline: ($l + "/" + $m + ": test count fell " + ($d|tostring) + "% (" + ($md|tostring) + " -> " + ($nw|tostring) + ")"),
      detail: "The launch still succeeded, so tests were skipped or a fixture aborted a spec early rather than failing."
    }')"
  fi
done

# --- per-test history from failed-item set membership ---
# Neutralise only what genuinely varies run-to-run. There is deliberately NO
# blanket digit rule: `s/[0-9]+/<n>/g` would fold "expected length 12 to equal 8"
# and "expected length 47 to equal 3" into one string and therefore one hash.
# Since the regression/chronic fingerprint carries no test path — by design, so
# one broken fixture across fifteen specs clusters into one issue — that
# collision would make two different regressions in the same module share a
# fingerprint, and dedup would silently drop the second.
normalize_error() {
  printf '%s' "$1" \
    | sed -E 's/[0-9]+ms/<ms>/g; s/:[0-9]+:[0-9]+/:<pos>/g; s/[0-9]{4}-[0-9]{2}-[0-9]{2}[T ][0-9:.]+/<ts>/g' \
    | perl -pe 's/\b(?=[0-9a-f]{8,}\b)(?=[0-9a-f]*[a-f])[0-9a-f]+\b/<id>/gi' \
    | cut -c1-200
}

CAP=5
for key in $(printf '%s' "$launches" | jq -r '[ .[] | .layer + "/" + .module ] | unique | .[]'); do
  l="${key%%/*}"; m="${key##*/}"
  ids="$(printf '%s' "$launches" | jq -r --arg l "$l" --arg m "$m" \
    '[ .[] | select(.layer==$l and .module==$m) ] | sort_by(-.startTime) | .[].id')"
  n="$(printf '%s\n' "$ids" | grep -c .)"
  [[ "$n" -ge 2 ]] || continue

  # Real elapsed days between this module's oldest and newest launch, so a
  # finding can report the span it actually observed rather than inferring one
  # from a run count.
  span_days="$(printf '%s' "$launches" | jq --arg l "$l" --arg m "$m" \
    '[ .[] | select(.layer==$l and .module==$m) | .startTime ]
     | ((max - min) / 86400000) | floor')"

  tests="$(printf '%s' "$failed_items" | jq -r --argjson ids "$(printf '%s\n' "$ids" | jq -R . | jq -s 'map(tonumber)')" \
    '[ .[] | select(.launchId as $x | $ids | index($x)) | (.path + " > " + .name) ] | unique | .[]')"

  newest_two="$(printf '%s\n' "$ids" | head -2)"
  while IFS= read -r t; do
    [[ -n "$t" ]] || continue
    k=0; recent_fails=0; seq=""
    for id in $ids; do
      hit="$(printf '%s' "$failed_items" | jq --argjson i "$id" --arg t "$t" \
        '[ .[] | select(.launchId==$i and (.path + " > " + .name)==$t) ] | length')"
      if [[ "$hit" -gt 0 ]]; then
        k=$((k+1)); seq="${seq}1"
        # Exact-line match, never a glob. `case $newest_two in *"$id"*` counts
        # launch 42 as one of the newest two whenever a newest id merely
        # contains "42" — 4200, 1042. ReportPortal ids are dense sequential
        # integers, so that misfiles regressions routinely in production while
        # every fixture using small ids passes green.
        if printf '%s\n' "$newest_two" | grep -qxF "$id"; then
          recent_fails=$((recent_fails+1))
        fi
      else
        seq="${seq}0"
      fi
    done

    # Count status transitions across the window. Flaky means *alternating*,
    # not merely "sometimes red". A test that failed the two oldest runs and has
    # passed ever since has one flip and has already healed — filing it as flaky
    # produces a pull request to fix nothing.
    flips="$(printf '%s' "$seq" | awk '{n=0; for(i=2;i<=length($0);i++) if(substr($0,i,1)!=substr($0,i-1,1)) n++; print n+0}')"

    err_line="$(printf '%s' "$failed_items" | jq -r --arg t "$t" \
      'map(select((.path + " > " + .name)==$t)) | .[0].error // ""')"
    norm="$(normalize_error "$err_line")"
    hash8="$(printf '%s' "$norm" | shasum -a 256 | cut -c1-8)"
    pass_pct=$(( (n - k) * 100 / n ))

    # Chronic means "red in every run we have", not "red in seven runs". A
    # launch count is only a calendar week for the nightly E2E layer; backend
    # and frontend report per push to main, where seven launches can be a single
    # afternoon — filing an issue headlined "for a week" that is simply false —
    # or never accumulate at all in a quiet week, leaving a permanently red test
    # re-filed as a fresh regression forever. The headline states the real span
    # and run count instead of asserting a duration it has not measured.
    if [[ "$k" -eq "$n" && "$n" -ge 3 ]]; then
      add_finding "$(jq -n --arg l "$l" --arg m "$m" --arg h "$hash8" --arg t "$t" --arg e "$err_line" \
                            --argjson n "$n" --argjson sd "$span_days" '{
        category: "chronic", layer: $l, module: $m,
        fingerprint: ("test-chronic:" + $l + ":" + $m + ":" + $h),
        headline: ($l + "/" + $m + ": \"" + $t + "\" failed all " + ($n|tostring) + " runs in the window (spanning " + ($sd|tostring) + " days)"),
        detail: ("Red in every launch held for this module — no passing run to compare against. First error line: " + $e) }')"
    elif [[ "$recent_fails" -eq 2 && "$k" -eq 2 ]]; then
      # Claim a prior pass only when one was observed. At n=2 the entire window
      # is red, so "passed earlier" would assert evidence we do not hold — the
      # same overclaim as the chronic headline that used to say "for a week".
      if [[ "$k" -eq "$n" ]]; then
        reg_detail="No passing run observed: all ${n} launches held for this module are red, which is too thin a history to call it chronic."
      else
        reg_detail="Passed earlier in the window, now failing."
      fi
      add_finding "$(jq -n --arg l "$l" --arg m "$m" --arg h "$hash8" --arg t "$t" --arg e "$err_line" --arg d "$reg_detail" '{
        category: "regression", layer: $l, module: $m,
        fingerprint: ("test-regress:" + $l + ":" + $m + ":" + $h),
        headline: ($l + "/" + $m + ": \"" + $t + "\" newly fails two runs running"),
        detail: ($d + " First error line: " + $e) }')"
    # Compare without pre-dividing: pass_pct floors, so `pass_pct <= 80` admits
    # true rates up to 80.99% into the band. pass_pct is kept for the headline.
    elif [[ "$k" -gt 0 && "$k" -lt "$n" && "$flips" -ge 2 \
            && $(( (n - k) * 100 )) -ge $(( 20 * n )) \
            && $(( (n - k) * 100 )) -le $(( 80 * n )) ]]; then
      add_finding "$(jq -n --arg l "$l" --arg m "$m" --arg t "$t" --argjson p "$pass_pct" --arg e "$err_line" '{
        category: "flaky", layer: $l, module: $m,
        fingerprint: ("test-flaky:" + $l + ":" + $m + ":" + $t),
        headline: ($l + "/" + $m + ": \"" + $t + "\" is flaky (" + ($p|tostring) + "% pass rate)"),
        detail: ("Alternates within the window with no consistent outcome. First error line: " + $e) }')"
    fi
  done <<< "$tests"
done

# Priority order for the cap, so infrastructure faults are never crowded out by
# a pile of flaky tests.
findings="$(printf '%s' "$findings" | jq '
  def rank: { "ci-broken":0, "schedule-broken":1, "silence-unattributed":2,
              "rp-reporting-broken":3, "regression":4, "suite-shrank":5,
              "flaky":6, "chronic":7 }[.category] // 9;
  sort_by(rank)')"

# ------------------------------------------------------------------ state ---
finding_count="$(printf '%s' "$findings" | jq 'length')"
state_body="$(printf '%s' "$findings" | jq -S -c '[ .[] | .fingerprint ] | sort')"
state_hash="$(printf '%s' "$state_body" | shasum -a 256 | cut -c1-16)"

# Reaching here means the gather succeeded, so the error streak is over.
clear_error_days
STATE="findings=${finding_count}:${state_hash}:errdays=0"

if [[ "$STATE_ONLY" -eq 1 ]]; then
  echo "STATE: ${STATE}"
  exit 0
fi

# ----------------------------------------------------------------- output ---
launch_count="$(printf '%s' "$launches" | jq 'length')"

cat <<EOF
# Test-health digest

- **Window:** last ${DAYS} days (since $(fmt_epoch $((WINDOW_START_MS/1000))))
- **Generated:** $(date -u +%Y-%m-%dT%H:%M:%SZ)
- **Source:** ReportPortal project ${RP_PROJECT:-heblo}
- **Launches in window:** ${launch_count}

> Deterministic digest. Absence of expected data is computed here and appears
> as a finding — do not infer it yourself, and do not treat a missing section
> as "everything is fine".

## 1. Launch inventory

EOF

printf '%s' "$launches" | jq -r '
  if length == 0 then "_(no launches in window)_"
  else
    "| launch | layer | module | # | status | total | passed | failed | skipped | started |",
    "| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |",
    ( sort_by(.layer, .module, -.startTime)[]
      | "| \(.name) | \(.layer) | \(.module) | \(.number) | \(.status) | \(.total) | \(.passed) | \(.failed) | \(.skipped) | \(.startTime) |" )
  end'

echo
echo "## 2. Failed items in window"
echo
printf '%s' "$failed_items" | jq -r '
  if length == 0 then "_(none)_"
  else
    "| launch | test | error (first line) |",
    "| --- | --- | --- |",
    ( .[] | "| \(.launchId) | \(.path) > \(.name) | \(.error | .[0:160]) |" )
  end'

echo
echo "## 3. Findings"
echo
echo "FINDINGS: ${finding_count}"
echo "CAP: ${CAP} (file at most this many issues; report every finding you skip and why)"
echo
printf '%s' "$findings" | jq -r '
  if length == 0 then "_(none — nothing to file)_"
  else ( .[] | "- **\(.category)** `\(.fingerprint)` — \(.headline)\n  \(.detail)" )
  end'

echo
echo "STATE: ${STATE}"
