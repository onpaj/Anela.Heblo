#!/bin/bash

# Script to start backend in Development mode
echo "Starting Anela Heblo Backend - Development Mode"
echo "========================================="

# Resolve project root (this script lives in scripts/)
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"

# Start local development dependencies (Azurite blob storage, etc.) via docker-compose.
# In Development, FileStorage uses UseDevelopmentStorage=true, which talks to Azurite on
# port 10000. Without it, blob features fail with "Connection refused (127.0.0.1:10000)".
start_dev_dependencies() {
    local compose_file="$PROJECT_ROOT/docker-compose.yml"

    # Detect a container engine. The zsh `docker` alias to podman does NOT apply inside this
    # bash script, so probe for real binaries and pick the matching compose command.
    local compose_cmd=""
    if command -v docker >/dev/null 2>&1; then
        compose_cmd="docker compose"
    elif command -v podman >/dev/null 2>&1; then
        compose_cmd="podman compose"
    fi

    if [ -z "$compose_cmd" ]; then
        echo "⚠️  No container engine (docker/podman) found — skipping Azurite."
        echo "    Blob features will fail with 'Connection refused (127.0.0.1:10000)'."
        return
    fi

    echo "Starting dev dependencies via: $compose_cmd"
    if ! $compose_cmd -f "$compose_file" up -d; then
        echo "⚠️  Failed to start dev dependencies (is the engine/VM running?)."
        echo "    Blob features will fail until Azurite is up."
        return
    fi

    # Wait for the Azurite blob endpoint to accept connections.
    echo -n "Waiting for Azurite blob endpoint on :10000 "
    for _ in $(seq 1 30); do
        if nc -z localhost 10000 2>/dev/null; then
            echo " ready ✅"
            return
        fi
        echo -n "."
        sleep 0.5
    done
    echo ""
    echo "⚠️  Azurite did not become ready in time; continuing anyway."
}

start_dev_dependencies

# Kill any processes running on port 5000
echo "Cleaning up port 5000..."
lsof -ti:5000 | xargs kill -9 2>/dev/null || true
sleep 1

# Navigate to backend directory
cd "$PROJECT_ROOT/backend/src/Anela.Heblo.API"

# Check if directory exists
if [ ! -d "$(pwd)" ]; then
    echo "Error: Backend directory not found"
    exit 1
fi

echo "Working directory: $(pwd)"
echo "Environment: Development"
echo "Port: 5000"
echo ""

# Start the backend
dotnet run --launch-profile Development
