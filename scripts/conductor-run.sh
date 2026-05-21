#!/bin/bash

# Conductor run script — launches frontend only; backend is expected on port 5000.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"

# Find the first free TCP port at or above the given starting port.
find_free_port() {
  local port=$1
  while lsof -iTCP:"$port" -sTCP:LISTEN -t >/dev/null 2>&1; do
    port=$((port + 1))
  done
  echo "$port"
}

BACKEND_PORT=5000
FRONTEND_PORT=3000

BRANCH="$(git -C "$PROJECT_ROOT" branch --show-current)"
if [ -z "$BRANCH" ]; then
  BRANCH="$(git -C "$PROJECT_ROOT" rev-parse --short HEAD)"
fi

echo "🚀 Starting Heblo (Conductor)"
echo "   Branch:   $BRANCH"
echo "   Backend:  http://localhost:$BACKEND_PORT (external — start it yourself)"
echo "   Frontend: http://localhost:$FRONTEND_PORT"
echo ""

# Stop children when this script (and its process group) is stopped.
trap 'kill 0' EXIT INT TERM

# Frontend — start:conductor has no inline env, so these exported vars take effect.
# REACT_APP_REDIRECT_URI is cleared so MSAL falls back to window.location.origin.
(
  cd "$PROJECT_ROOT"
  PORT="$FRONTEND_PORT" \
  REACT_APP_API_URL="http://localhost:$BACKEND_PORT" \
  REACT_APP_USE_MOCK_AUTH=false \
  REACT_APP_REDIRECT_URI= \
  REACT_APP_BRANCH_NAME="$BRANCH" \
    npm --prefix frontend run start:conductor
) &

wait
