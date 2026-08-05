#!/usr/bin/env bash
#
# env-fallback.sh — shared by appinsights-query.sh, gh-api.sh, and
# telemetry-digest.sh.
#
# Local/dev convenience: fall back to a gitignored .env at the repo root for
# secrets not already present in the environment. Real env vars (e.g. an Orca
# automation that injects them directly) always win over the .env file.
#
# Usage (from one of the sibling scripts):
#   # shellcheck disable=SC1090
#   source "$(dirname "${BASH_SOURCE[0]}")/env-fallback.sh"
#   load_env_fallback APPINSIGHTS_APP_ID APPINSIGHTS_API_KEY

load_env_fallback() {
  local repo_root
  repo_root="$(cd "$(dirname "${BASH_SOURCE[1]}")/../../.." 2>/dev/null && pwd)" || return 0
  local env_file="${repo_root}/.env"
  [[ -f "$env_file" ]] || return 0

  local var pre_values=()
  for var in "$@"; do
    pre_values+=("${!var:-}")
  done

  set -a
  # shellcheck disable=SC1090
  source "$env_file"
  set +a

  local i=0
  for var in "$@"; do
    [[ -n "${pre_values[$i]}" ]] && printf -v "$var" '%s' "${pre_values[$i]}"
    i=$((i + 1))
  done
}
