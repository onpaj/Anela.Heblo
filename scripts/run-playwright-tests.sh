#!/bin/bash

# Run Playwright tests with automation environment

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

echo -e "${BLUE}🎭 Starting Playwright tests with automation environment${NC}"

# Kill any existing processes on automation ports
echo -e "${YELLOW}🧹 Cleaning up existing processes...${NC}"
lsof -ti:3001 | xargs kill -9 2>/dev/null || true
lsof -ti:5001 | xargs kill -9 2>/dev/null || true

# Wait a moment for ports to be free
sleep 2

# Function to cleanup and exit immediately
cleanup_and_exit() {
    local exit_code=$1
    echo -e "${YELLOW}🧹 Cleaning up...${NC}"
    [ ! -z "$BACKEND_PID" ] && kill $BACKEND_PID 2>/dev/null || true
    [ ! -z "$FRONTEND_PID" ] && kill $FRONTEND_PID 2>/dev/null || true
    lsof -ti:3001 | xargs kill -9 2>/dev/null || true
    lsof -ti:5001 | xargs kill -9 2>/dev/null || true
    exit $exit_code
}

# Get absolute paths
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BACKEND_DIR="$SCRIPT_DIR/../backend/src/Anela.Heblo.API"
FRONTEND_DIR="$SCRIPT_DIR/../frontend"

# Start backend in background (no waiting)
echo -e "${BLUE}🚀 Starting automation backend on port 5001...${NC}"
cd "$BACKEND_DIR"
ASPNETCORE_ENVIRONMENT=Automation dotnet run --launch-profile Automation > /dev/null 2>&1 &
BACKEND_PID=$!

# Start frontend in background (no waiting)  
echo -e "${BLUE}🚀 Starting automation frontend on port 3001...${NC}"
cd "$FRONTEND_DIR"
npm run start:automation > /dev/null 2>&1 &
FRONTEND_PID=$!

# Wait for both services to be ready
echo -e "${YELLOW}⏳ Waiting for services to start...${NC}"
sleep 8

# Quick health check (no waiting on failure)
if ! curl -s http://localhost:5001/health > /dev/null 2>&1; then
    echo -e "${RED}❌ Backend failed to start${NC}"
    cleanup_and_exit 1
fi

if ! curl -s http://localhost:3001 > /dev/null 2>&1; then
    echo -e "${RED}❌ Frontend failed to start${NC}"
    cleanup_and_exit 1
fi

echo -e "${GREEN}✅ Services started successfully${NC}"

# Run Playwright tests
echo -e "${BLUE}🎭 Running Playwright tests...${NC}"
cd "$FRONTEND_DIR"
npx playwright test "$@"

# Immediate cleanup and exit
cleanup_and_exit $?