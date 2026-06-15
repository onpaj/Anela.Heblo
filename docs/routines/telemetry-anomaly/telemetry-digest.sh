#!/usr/bin/env bash
#
# telemetry-digest.sh — gather a production-telemetry digest from Azure
# Application Insights for the daily telemetry-anomaly routine.
#
# This is the deterministic data-gathering half of the routine: it runs a
# curated, fixed KQL set over a window and prints a Markdown digest. The
# routine's Claude session then reasons over this digest + GitHub activity to
# surface reliability/performance/risk signals (see docs/routines/telemetry-anomaly/README.md).
#
# Credentials and egress are inherited from appinsights-query.sh (which this
# script calls): APPINSIGHTS_APP_ID / APPINSIGHTS_API_KEY env secrets, and the
# api.applicationinsights.io host on the environment's Custom egress allowlist.
#
# Usage:
#   docs/routines/telemetry-anomaly/telemetry-digest.sh                 # default window P7D
#   docs/routines/telemetry-anomaly/telemetry-digest.sh --timespan P1D  # last 24h
#
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
QUERY="${HERE}/appinsights-query.sh"
TIMESPAN="P7D"

[[ -x "$QUERY" ]] || { echo "Error: ${QUERY} not found or not executable." >&2; exit 1; }
command -v jq >/dev/null || { echo "Error: jq is required." >&2; exit 1; }

if [[ "${1:-}" == "--timespan" ]]; then
  TIMESPAN="${2:?--timespan requires a value, e.g. P1D}"
  shift 2
fi

# Render an App Insights JSON result as a GitHub-flavoured Markdown table.
# Empty result sets print "_(none)_" so the digest is unambiguous.
to_md_table() {
  jq -r '
    .tables[0] as $t
    | if ($t.rows | length) == 0 then "_(none)_"
      else
        ( [ $t.columns[].name ]   | @tsv ),
        ( [ $t.columns[] | "---" ] | @tsv ),
        ( $t.rows[] | map(if . == null then "" else tostring end) | @tsv )
      end
    | if . == "_(none)_" then . else "| " + gsub("\t"; " | ") + " |" end
  '
}

section() {
  local title="$1" kql="$2"
  echo "### ${title}"
  echo ""
  "$QUERY" --timespan "$TIMESPAN" "$kql" | to_md_table
  echo ""
}

cat <<EOF
# Telemetry brainstorm digest

- **Window:** ${TIMESPAN} (ISO-8601, ending now)
- **Generated:** $(date -u +%Y-%m-%dT%H:%M:%SZ)
- **Source:** Azure Application Insights (app ${APPINSIGHTS_APP_ID:-?})

> Deterministic digest. The routine reasons over this + GitHub activity to
> surface reliability/performance/risk signals. resultCode \`0\` on browser
> (\`Fetch\`) dependencies is almost always an **aborted** client request
> (tab close / SPA navigation), not a server fault — weigh accordingly.

EOF

section "1. Daily request volume & failure rate" \
  'requests | summarize total=count(), failed=countif(success==false) by bin(timestamp,1d) | extend failPct=round(100.0*failed/total,1) | order by timestamp asc'

section "2. Result-code distribution" \
  'requests | summarize count() by resultCode | order by count_ desc | take 20'

section "3. Server errors (5xx) by endpoint" \
  'requests | where toint(resultCode) >= 500 | summarize count() by name, resultCode | order by count_ desc | take 25'

section "4. Top forbidden/unauthorized API endpoints (401/403)" \
  'requests | where resultCode in ("401","403") and (name contains "/api") | summarize count() by name, resultCode | order by count_ desc | take 20'

section "5. Exceptions by type & problemId" \
  'exceptions | summarize count() by type, problemId | order by count_ desc | take 20'

section "6. Browser (frontend) exceptions" \
  'exceptions | where client_Type == "Browser" | summarize count() by type, problemId | order by count_ desc | take 15'

section "7. Slow dependencies (p95/p99, >50 calls)" \
  'dependencies | summarize calls=count(), failed=countif(success==false), p50=round(percentile(duration,50),0), p95=round(percentile(duration,95),0), p99=round(percentile(duration,99),0) by type, target | where calls>50 | order by p95 desc | take 25'

section "8. Failing dependencies by target" \
  'dependencies | where success==false | summarize failures=count() by type, target, resultCode | order by failures desc | take 25'

section "9. Busiest endpoints (traffic shape)" \
  'requests | summarize count(), p95dur=round(percentile(duration,95),0) by name | order by count_ desc | take 20'
