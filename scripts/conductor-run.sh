#!/bin/bash

# Conductor run script — launches backend + frontend on dynamically picked free ports
# so multiple Conductor workspaces of Heblo can run in parallel without colliding.
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

# Bases 5100/3100 keep Conductor instances clear of the manual 5000/3000 dev scripts.
BACKEND_PORT="$(find_free_port 5100)"
FRONTEND_PORT="$(find_free_port 3100)"

BRANCH="$(git -C "$PROJECT_ROOT" branch --show-current)"
if [ -z "$BRANCH" ]; then
  BRANCH="$(git -C "$PROJECT_ROOT" rev-parse --short HEAD)"
fi

echo "🚀 Starting Heblo (Conductor)"
echo "   Branch:   $BRANCH"
echo "   Backend:  http://localhost:$BACKEND_PORT"
echo "   Frontend: http://localhost:$FRONTEND_PORT"
echo ""

# Stop both children when this script (and its process group) is stopped.
trap 'kill 0' EXIT INT TERM

# Backend — dynamic port via ASPNETCORE_URLS.
# UseConductorOverrides=true layers appsettings.Conductor.json (disables all hydration /
# background refresh) and makes CORS accept any loopback origin, so the dynamically
# chosen frontend port is allowed without a fixed allow-list.
(
  cd "$PROJECT_ROOT"
  ASPNETCORE_ENVIRONMENT=Development \
  ASPNETCORE_URLS="http://localhost:$BACKEND_PORT" \
  UseConductorOverrides=true \
    dotnet run --project backend/src/Anela.Heblo.API --no-launch-profile
) &
BACKEND_PID=$!

# Wait for the backend to actually listen — `dotnet run` builds first, so this can
# take 30-60s. Bail out early with a clear message if the build/start failed.
echo "⏳ Waiting for backend to build and listen on port $BACKEND_PORT ..."
for _ in $(seq 1 150); do
  if ! kill -0 "$BACKEND_PID" 2>/dev/null; then
    echo "❌ Backend exited before becoming ready — check the build output above."
    exit 1
  fi
  if curl -sf -o /dev/null "http://localhost:$BACKEND_PORT/health/live" 2>/dev/null; then
    echo "✅ Backend is up on http://localhost:$BACKEND_PORT"
    break
  fi
  sleep 2
done

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
