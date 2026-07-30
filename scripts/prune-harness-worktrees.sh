#!/usr/bin/env bash
#
# Remove worktrees belonging to TERMINAL harness tasks (done/ or archived/)
# older than AGE_DAYS. Never touches a worktree whose task is still live in any
# other queue - harness_v2 invariant #30 lets a live task's branch stay checked
# out in its own worktree, and removing it under a running task breaks reattach.
#
# Prints nothing on stdout in normal operation: it runs as a harness `command`
# check, and any stdout line would mint a task.
#
# Usage: prune-harness-worktrees.sh [--apply]   (default: dry run, to stderr)
set -euo pipefail

ROOT="${HARNESS_ROOT:-/Users/rem/harness-root}"
AGE_DAYS="${AGE_DAYS:-3}"
APPLY=0
[ "${1:-}" = "--apply" ] && APPLY=1

terminal_ids="$(
  find "$ROOT/done" "$ROOT/archived" -maxdepth 1 -name '*.json' -type f 2>/dev/null \
    | xargs -I{} basename {} .json 2>/dev/null | sort -u
)"

# Anything still live: every task json under the root EXCEPT the two terminal
# queues and the definition directories. Deliberately a denylist, not an
# allowlist of queue names - a queue added by a future harness version must
# read as "live", never as "prunable". Today that means tasks/ (the inbox),
# queues/<step>/ (incl. their .processing/ staging dirs), failed/ and healed/
# all count as live without being named here.
#
# The pruned dirs are pruned with -prune rather than filtered with -not -path:
# worktrees/ holds ~36 GB of checked-out repositories, and descending into it
# only to discard the hits costs ~20s per run versus ~0.05s pruned.
live_ids="$(
  find "$ROOT" \
       \( -path "$ROOT/done" \
       -o -path "$ROOT/archived" \
       -o -path "$ROOT/worktrees" \
       -o -path "$ROOT/agents" \
       -o -path "$ROOT/workflows" \
       -o -path "$ROOT/processes" \
       -o -path "$ROOT/triggers" \) -prune \
    -o -name '*.json' -type f -print 2>/dev/null \
    | xargs -I{} basename {} .json 2>/dev/null | sort -u
)"

removed=0
freed=0
for dir in "$ROOT"/worktrees/*/; do
  [ -d "$dir" ] || continue
  id="$(basename "$dir")"

  printf '%s\n' "$terminal_ids" | grep -qx "$id" || continue
  printf '%s\n' "$live_ids" | grep -qx "$id" && continue
  [ -n "$(find "$dir" -maxdepth 0 -mtime "+$AGE_DAYS")" ] || continue

  size="$(du -sk "$dir" | cut -f1)"
  if [ "$APPLY" -eq 1 ]; then
    rm -rf "$dir"
    echo "removed $id (${size}K)" >&2
  else
    echo "would remove $id (${size}K)" >&2
  fi
  removed=$((removed + 1))
  freed=$((freed + size))
done

echo "worktree-janitor: ${removed} worktrees, $((freed / 1024))MB (apply=$APPLY)" >&2
