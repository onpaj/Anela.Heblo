#!/bin/bash

# Script for starting automation environment (backend + frontend)
# Used for development and testing of manual refresh functionality

set -e  # Exit on any error

echo "🔄 Starting automation environment..."

# Kill existing processes on ports 3001 and 5001
echo "🧹 Killing existing processes on ports 3001 and 5001..."
lsof -ti:3001,5001 | xargs kill -9 2>/dev/null || true

# Get script directory and project root
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"

# Start backend on port 5001 in background
echo "🚀 Starting backend on port 5001 (Automation environment)..."
cd "$PROJECT_ROOT/backend/src/Anela.Heblo.API"
ASPNETCORE_ENVIRONMENT=Automation dotnet run --launch-profile Automation > /tmp/backend-automation.log 2>&1 &
BACKEND_PID=$!

# Start frontend on port 3001 in background
echo "🚀 Starting frontend on port 3001 (Automation environment)..."
cd "$PROJECT_ROOT/frontend"
npm run start:automation > /tmp/frontend-automation.log 2>&1 &
FRONTEND_PID=$!

# Wait for services to initialize
echo "⏳ Waiting 5 seconds for services to start..."
sleep 5

echo "✅ Automation environment started!"
echo "📊 Backend (port 5001) PID: $BACKEND_PID"
echo "🌐 Frontend (port 3001) PID: $FRONTEND_PID"
echo "📋 Backend logs: tail -f /tmp/backend-automation.log"
echo "📋 Frontend logs: tail -f /tmp/frontend-automation.log"
echo "🔗 Application URL: http://localhost:3001"

# Script ends here - processes continue running in background