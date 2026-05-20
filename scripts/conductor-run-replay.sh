#!/bin/bash

# Conductor run script for SmartsuppWebhookReplay standalone tool.
# No frontend process — the tool serves its own static UI from wwwroot/.
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

PORT="$(find_free_port 5050)"

BRANCH="$(git -C "$PROJECT_ROOT" branch --show-current)"
if [ -z "$BRANCH" ]; then
  BRANCH="$(git -C "$PROJECT_ROOT" rev-parse --short HEAD)"
fi

echo "🚀 Starting SmartsuppWebhookReplay (Conductor)"
echo "   Branch: $BRANCH"
echo "   UI:     http://localhost:$PORT"
echo ""
echo "ℹ️  Connection string must be configured in:"
echo "   backend/tools/SmartsuppWebhookReplay/secrets.json"
echo ""

trap 'kill 0' EXIT INT TERM

(
  cd "$PROJECT_ROOT"
  ASPNETCORE_ENVIRONMENT=Development \
  ASPNETCORE_URLS="http://localhost:$PORT" \
    dotnet run --project backend/tools/SmartsuppWebhookReplay --no-launch-profile
) &
APP_PID=$!

echo "⏳ Waiting for app to build and listen on port $PORT ..."
for _ in $(seq 1 90); do
  if ! kill -0 "$APP_PID" 2>/dev/null; then
    echo "❌ App exited before becoming ready — check build output above."
    exit 1
  fi
  if curl -sf -o /dev/null "http://localhost:$PORT/" 2>/dev/null; then
    echo "✅ Ready at http://localhost:$PORT"
    break
  fi
  sleep 2
done

wait
