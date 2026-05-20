#!/bin/bash

# Conductor run script — launches backend + frontend + SmartsuppWebhookReplay tool.
# All three pick free ports so multiple workspaces can run in parallel.
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

# Bases keep Conductor instances clear of the manual dev scripts (5001/3000/5050).
BACKEND_PORT="$(find_free_port 5100)"
FRONTEND_PORT="$(find_free_port 3100)"
REPLAY_PORT="$(find_free_port 5051)"

BRANCH="$(git -C "$PROJECT_ROOT" branch --show-current)"
if [ -z "$BRANCH" ]; then
  BRANCH="$(git -C "$PROJECT_ROOT" rev-parse --short HEAD)"
fi

echo "🚀 Starting Heblo + Replay tool (Conductor)"
echo "   Branch:   $BRANCH"
echo "   Backend:  http://localhost:$BACKEND_PORT"
echo "   Frontend: http://localhost:$FRONTEND_PORT"
echo "   Replay:   http://localhost:$REPLAY_PORT"
echo ""

trap 'kill 0' EXIT INT TERM

# ── Backend ──────────────────────────────────────────────────────────────────
(
  cd "$PROJECT_ROOT"
  ASPNETCORE_ENVIRONMENT=Development \
  ASPNETCORE_URLS="http://localhost:$BACKEND_PORT" \
  UseConductorOverrides=true \
    dotnet run --project backend/src/Anela.Heblo.API --no-launch-profile
) &
BACKEND_PID=$!

echo "⏳ Waiting for backend to build and listen on port $BACKEND_PORT ..."
for _ in $(seq 1 150); do
  if ! kill -0 "$BACKEND_PID" 2>/dev/null; then
    echo "❌ Backend exited before becoming ready — check build output above."
    exit 1
  fi
  if curl -sf -o /dev/null "http://localhost:$BACKEND_PORT/health/live" 2>/dev/null; then
    echo "✅ Backend is up on http://localhost:$BACKEND_PORT"
    break
  fi
  sleep 2
done

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
