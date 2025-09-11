#!/bin/bash

# Playwright Test Runner for Staging Environment
# Usage: ./scripts/run-playwright-tests.sh [test-file-name]
# Examples:
#   ./scripts/run-playwright-tests.sh                    # Run all tests
#   ./scripts/run-playwright-tests.sh auth              # Run tests matching "auth"
#   ./scripts/run-playwright-tests.sh sidebar.spec.ts  # Run specific test file

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Configuration
STAGING_URL="https://heblo.stg.anela.cz"
FRONTEND_DIR="frontend"
TEST_DIR="$FRONTEND_DIR/test/e2e"

echo -e "${BLUE}🎭 Anela Heblo - Playwright E2E Tests${NC}"
echo -e "${BLUE}Target Environment: ${STAGING_URL}${NC}"
echo ""

# Check if we're in the correct directory
if [ ! -d "$FRONTEND_DIR" ]; then
    echo -e "${RED}❌ Error: Must be run from project root (frontend/ directory not found)${NC}"
    exit 1
fi

# Change to frontend directory
cd "$FRONTEND_DIR"

# Check if Playwright is installed
if [ ! -d "node_modules/@playwright" ]; then
    echo -e "${YELLOW}⚠️  Playwright not found. Installing dependencies...${NC}"
    npm install
fi

# Check if browsers are installed
if [ ! -d "node_modules/@playwright/test" ] || [ ! -f "node_modules/@playwright/test/package.json" ]; then
    echo -e "${YELLOW}⚠️  Installing Playwright browsers...${NC}"
    npx playwright install
fi

# Verify staging environment is accessible
echo -e "${BLUE}🔍 Checking staging environment availability...${NC}"
if ! curl -s --head --fail "$STAGING_URL" > /dev/null; then
    echo -e "${YELLOW}⚠️  Warning: Unable to reach staging environment at $STAGING_URL${NC}"
    echo -e "${YELLOW}   Tests may fail if the environment is down${NC}"
    echo ""
fi

# Set environment variables for Playwright
export PLAYWRIGHT_BASE_URL="$STAGING_URL"
export CI=false  # Disable CI mode for better debugging

# Build test command
PLAYWRIGHT_CMD="npx playwright test"

# Add test file filter if provided
if [ -n "$1" ]; then
    echo -e "${BLUE}🎯 Running tests matching: ${YELLOW}$1${NC}"
    PLAYWRIGHT_CMD="$PLAYWRIGHT_CMD $1"
else
    echo -e "${BLUE}🎯 Running all E2E tests${NC}"
fi

# Additional Playwright options
PLAYWRIGHT_CMD="$PLAYWRIGHT_CMD --config=playwright.config.ts"

echo -e "${BLUE}📂 Test Directory: ${TEST_DIR}${NC}"
echo -e "${BLUE}🚀 Running Command: ${PLAYWRIGHT_CMD}${NC}"
echo ""

# Run the tests
if $PLAYWRIGHT_CMD; then
    echo ""
    echo -e "${GREEN}✅ Tests completed successfully!${NC}"
    
    # Show report location
    if [ -d "playwright-report" ]; then
        echo -e "${BLUE}📊 Test report available at: playwright-report/index.html${NC}"
        echo -e "${BLUE}   View with: npx playwright show-report${NC}"
    fi
else
    echo ""
    echo -e "${RED}❌ Tests failed!${NC}"
    
    # Show debugging information
    echo -e "${YELLOW}🔧 Debugging tips:${NC}"
    echo -e "   • Run with --headed to see browser: npx playwright test --headed"
    echo -e "   • Run with --debug for step-by-step: npx playwright test --debug"
    echo -e "   • Check test report: npx playwright show-report"
    echo -e "   • Verify staging environment: $STAGING_URL"
    
    exit 1
fi