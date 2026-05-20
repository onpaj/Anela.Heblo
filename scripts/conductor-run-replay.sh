#!/bin/bash

# Conductor run script — launches frontend + SmartsuppWebhookReplay tool.
# Backend is expected to be running externally (e.g. Rider debugger on port 5001).
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"

find_free_port() {
  local port=$1
  while lsof -iTCP:"$port" -sTCP:LISTEN -t >/dev/null 2>&1; do
    port=$((port + 1))
  done
  echo "$port"
}

BACKEND_PORT="${BACKEND_PORT:-5000}"
FRONTEND_PORT="$(find_free_port 3100)"
REPLAY_PORT="$(find_free_port 5051)"

BRANCH="$(git -C "$PROJECT_ROOT" branch --show-current)"
if [ -z "$BRANCH" ]; then
  BRANCH="$(git -C "$PROJECT_ROOT" rev-parse --short HEAD)"
fi

echo "🚀 Starting Heblo frontend + Replay tool (Conductor)"
echo "   Branch:   $BRANCH"
echo "   Backend:  http://localhost:$BACKEND_PORT  ← run this in Rider"
echo "   Frontend: http://localhost:$FRONTEND_PORT"
echo "   Replay:   http://localhost:$REPLAY_PORT"
echo ""

trap 'kill 0' EXIT INT TERM

# ── Frontend ──────────────────────────────────────────────────────────────────
(
  cd "$PROJECT_ROOT"
  PORT="$FRONTEND_PORT" \
  REACT_APP_API_URL="http://localhost:$BACKEND_PORT" \
  REACT_APP_USE_MOCK_AUTH=false \
  REACT_APP_REDIRECT_URI= \
  REACT_APP_BRANCH_NAME="$BRANCH" \
    npm --prefix frontend run start:conductor
) &

# ── Replay tool ───────────────────────────────────────────────────────────────
(
  cd "$PROJECT_ROOT"
  ASPNETCORE_ENVIRONMENT=Development \
  ASPNETCORE_URLS="http://localhost:$REPLAY_PORT" \
    dotnet run --project backend/tools/SmartsuppWebhookReplay --no-launch-profile
) &
REPLAY_PID=$!

echo "⏳ Waiting for replay tool to listen on port $REPLAY_PORT ..."
for _ in $(seq 1 90); do
  if ! kill -0 "$REPLAY_PID" 2>/dev/null; then
    echo "❌ Replay tool exited before becoming ready — check build output above."
    exit 1
  fi
  if curl -sf -o /dev/null "http://localhost:$REPLAY_PORT/" 2>/dev/null; then
    echo "✅ Replay tool is up on http://localhost:$REPLAY_PORT"
    break
  fi
  sleep 2
done

wait
