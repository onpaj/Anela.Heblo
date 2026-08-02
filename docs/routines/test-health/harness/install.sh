#!/usr/bin/env bash
#
# install.sh — copy the versioned harness configs into ~/harness-root.
#
# ~/harness-root is NOT a git repository, so these files live in the repo and
# are installed by copying. Re-run after editing either JSON file.
#
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="${HARNESS_ROOT:-$HOME/harness-root}"
FORCE=0
[[ "${1:-}" == "--force" ]] && FORCE=1

[[ -d "${ROOT}/processes" ]] || { echo "Error: ${ROOT}/processes not found." >&2; exit 1; }
[[ -d "${ROOT}/agents" ]]    || { echo "Error: ${ROOT}/agents not found." >&2; exit 1; }

install_one() { # install_one <src> <dest>
  local src="$1" dest="$2"
  if [[ -f "$dest" && "$FORCE" -eq 0 && "$dest" -nt "$src" ]]; then
    echo "skip  ${dest} (newer than the repo copy; re-run with --force to overwrite)"
    return 0
  fi
  cp "$src" "$dest"
  echo "wrote ${dest}"
}

install_one "${HERE}/test-health.process.json" "${ROOT}/processes/test-health.json"
install_one "${HERE}/test-health.agent.json"   "${ROOT}/agents/test-health.json"
echo "Done. Restart harness to pick up the new Process:"
echo "  launchctl kickstart -k gui/\$(id -u)/com.harness"
