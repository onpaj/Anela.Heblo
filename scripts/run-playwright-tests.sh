#!/bin/bash

# Run Playwright tests with automation environment
# Usage:
#   ./run-playwright-tests.sh                    # Run all tests
#   ./run-playwright-tests.sh test.spec.ts       # Run specific test file
#   ./run-playwright-tests.sh test/ --grep="pattern"  # Run tests matching pattern
#   ./run-playwright-tests.sh --headed           # Run tests with visible browser

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

echo -e "${BLUE}🎭 Starting Playwright tests with automation environment${NC}"

# Kill any existing processes on automation ports
echo -e "${YELLOW}🧹 Cleaning up existing processes...${NC}"
lsof -ti:3000 | xargs kill -9 2>/dev/null || true
lsof -ti:5000 | xargs kill -9 2>/dev/null || true

# Wait a moment for ports to be free
sleep 2

# Function to cleanup and exit immediately
cleanup_and_exit() {
    local exit_code=$1
    echo -e "${YELLOW}🧹 Cleaning up...${NC}"
    [ ! -z "$BACKEND_PID" ] && kill $BACKEND_PID 2>/dev/null || true
    [ ! -z "$FRONTEND_PID" ] && kill $FRONTEND_PID 2>/dev/null || true
    lsof -ti:3000 | xargs kill -9 2>/dev/null || true
    lsof -ti:5000 | xargs kill -9 2>/dev/null || true
    exit $exit_code
}

# Get absolute paths
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BACKEND_DIR="$SCRIPT_DIR/../backend/src/Anela.Heblo.API"
FRONTEND_DIR="$SCRIPT_DIR/../frontend"

# Start backend in background (no waiting)
echo -e "${BLUE}🚀 Starting automation backend on port 5000...${NC}"
cd "$BACKEND_DIR"
ASPNETCORE_ENVIRONMENT=Automation dotnet run --launch-profile Automation > /dev/null 2>&1 &
BACKEND_PID=$!

# Start frontend in background (no waiting)  
echo -e "${BLUE}🚀 Starting automation frontend on port 3000...${NC}"
cd "$FRONTEND_DIR"
npm run start:automation > /dev/null 2>&1 &
FRONTEND_PID=$!

# Wait for both services to be ready
echo -e "${YELLOW}⏳ Waiting for services to start...${NC}"

# Wait for backend with retry logic (up to 30 seconds)
BACKEND_READY=false
for i in {1..30}; do
    if curl -s http://localhost:5000/health/live > /dev/null 2>&1; then
        BACKEND_READY=true
        echo -e "${GREEN}✅ Backend is ready${NC}"
        break
    fi
    echo -e "${YELLOW}   Waiting for backend... ($i/30)${NC}"
    sleep 1
done

if [ "$BACKEND_READY" = false ]; then
    echo -e "${RED}❌ Backend failed to start after 30 seconds${NC}"
    cleanup_and_exit 1
fi

# Wait for frontend with retry logic (up to 20 seconds)
FRONTEND_READY=false
for i in {1..20}; do
    if curl -s http://localhost:3000 > /dev/null 2>&1; then
        FRONTEND_READY=true
        echo -e "${GREEN}✅ Frontend is ready${NC}"
        break
    fi
    echo -e "${YELLOW}   Waiting for frontend... ($i/20)${NC}"
    sleep 1
done

if [ "$FRONTEND_READY" = false ]; then
    echo -e "${RED}❌ Frontend failed to start after 20 seconds${NC}"
    cleanup_and_exit 1
fi

echo -e "${GREEN}✅ Services started successfully${NC}"

# Run Playwright tests
echo -e "${BLUE}🎭 Running Playwright tests...${NC}"

# Display what tests will run
if [ $# -eq 0 ]; then
    echo -e "${BLUE}   Running all tests${NC}"
else
    echo -e "${BLUE}   Running with parameters: $@${NC}"
fi

cd "$FRONTEND_DIR"
npx playwright test --reporter=list "$@"

# Immediate cleanup and exit
cleanup_and_exit $?