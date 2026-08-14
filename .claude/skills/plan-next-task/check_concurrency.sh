#!/usr/bin/env bash
# Refuse to proceed if too many sibling invocations of this automation are
# already running on this machine. Concurrency is capped locally, per
# stage, because machine capacity is a machine-local fact, not pipeline
# state — see
# docs/superpowers/specs/2026-08-07-two-phase-labeled-pipeline-design.md
# ("Concurrency & staleness").
#
# Usage: check_concurrency.sh <max-concurrent> <pgrep-pattern>
# Exit codes:
#   0  under capacity — caller should proceed
#   4  at/over capacity — caller should stop this cycle without claiming work
#   2  usage error
set -euo pipefail

MAX="${1:-}"
PATTERN="${2:-}"
if [[ -z "$MAX" || -z "$PATTERN" || ! "$MAX" =~ ^[0-9]+$ ]]; then
  echo "usage: check_concurrency.sh <max-concurrent> <pgrep-pattern>" >&2
  exit 2
fi

# Exclude our own invocation's process ancestry from the count. `pgrep -f`
# matches full command lines, and $PATTERN is passed to this script as a
# literal argv string -- so this script's own process (and any wrapping
# shell layers between the top-level `claude` process and this script, and
# that `claude` process itself, since its own argv legitimately contains
# something like `claude --dangerously-skip-permissions "/plan-next-task"`)
# can all spuriously match $PATTERN just by having invoked this script.
# Excluding only $PPID (one PID) is not enough -- walk the whole ancestry.
SELF_LINEAGE="$$"
walk_pid="$$"
for _ in 1 2 3 4 5 6 7 8; do
  parent=$(ps -o ppid= -p "$walk_pid" 2>/dev/null | tr -d ' ')
  if [ -z "$parent" ] || [ "$parent" -le 1 ]; then
    break
  fi
  SELF_LINEAGE="$SELF_LINEAGE $parent"
  walk_pid="$parent"
done

MATCHES=$(pgrep -f "$PATTERN" 2>/dev/null || true)
COUNT=0
for pid in $MATCHES; do
  case " $SELF_LINEAGE " in
    *" $pid "*) ;;  # part of our own invocation chain, don't count it
    *) COUNT=$((COUNT + 1)) ;;
  esac
done

if [ "$COUNT" -ge "$MAX" ]; then
  echo "at capacity: $COUNT/$MAX processes already matching '$PATTERN'" >&2
  exit 4
fi
echo "under capacity: $COUNT/$MAX processes matching '$PATTERN'"
