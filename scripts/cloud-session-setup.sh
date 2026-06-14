#!/usr/bin/env bash
#
# cloud-session-setup.sh
#
# SessionStart hook entrypoint for Claude Code on the web (cloud sessions).
#
# WHY THIS EXISTS:
#   The cloud environment's "setup script" field runs BEFORE Claude Code
#   launches, before the repository is reliably checked out at the working
#   directory. Pointing that field at a repo path (e.g. ./scripts/setup-cloud-env.sh)
#   fails with "No such file or directory" (exit 127), and a non-zero setup
#   script makes the whole session fail to start.
#
#   Repo-dependent setup therefore belongs in a SessionStart hook, which runs
#   AFTER launch with the repo checked out and $CLAUDE_PROJECT_DIR pointing at
#   it. This wrapper is wired up in .claude/settings.json.
#
# BEHAVIOUR:
#   - No-op outside cloud sessions (keeps local SessionStart fast).
#   - Delegates to the idempotent scripts/setup-cloud-env.sh, resolved via
#     $CLAUDE_PROJECT_DIR (falls back to this script's own location).
#   - Skips the heavy Playwright browser download by default; run
#     ./scripts/setup-cloud-env.sh manually when an E2E session needs it.
#
set -euo pipefail

# Cloud-only: CLAUDE_CODE_REMOTE=true is set in Claude Code on the web sessions.
if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

# $CLAUDE_PROJECT_DIR is set when the hook runs; fall back to the repo root
# inferred from this script's location for manual invocation.
REPO_ROOT="${CLAUDE_PROJECT_DIR:-$(cd "$(dirname "$0")/.." && pwd)}"

SKIP_PLAYWRIGHT="${SKIP_PLAYWRIGHT:-1}" exec "${REPO_ROOT}/scripts/setup-cloud-env.sh"
