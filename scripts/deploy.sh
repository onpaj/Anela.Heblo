#!/bin/bash

# Complete Deployment Script for Anela Heblo
# Builds, pushes, and deploys to Azure
# Usage: ./deploy.sh [test|production] [--skip-build] [--skip-tests]

set -e  # Exit on any error

# Parse arguments
ENVIRONMENT=${1:-"test"}
SKIP_BUILD=false
SKIP_TESTS=false

for arg in "$@"; do
    case $arg in
        --skip-build)
            SKIP_BUILD=true
            shift
            ;;
        --skip-tests)
            SKIP_TESTS=true
            shift
            ;;
    esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

echo "🚀 Complete Anela Heblo Deployment"
echo "📁 Project root: $PROJECT_ROOT"
echo "🌍 Environment: $ENVIRONMENT"
echo "⚙️ Skip build: $SKIP_BUILD"
echo "⚙️ Skip tests: $SKIP_TESTS"
echo ""

# Change to project root
cd "$PROJECT_ROOT"

# Step 1: Pre-deployment checks
echo "📋 Step 1: Pre-deployment checks..."

# Check if scripts are executable
chmod +x scripts/*.sh

# Check Azure CLI
if ! command -v az &> /dev/null; then
    echo "❌ Azure CLI not found. Please install: https://docs.microsoft.com/en-us/cli/azure/install-azure-cli"
    exit 1
fi

# Check Docker
if ! command -v docker &> /dev/null; then
    echo "❌ Docker not found. Please install Docker."
    exit 1
fi

echo "✅ Pre-deployment checks passed"

# Step 2: Run tests (optional)
if [ "$SKIP_TESTS" = "false" ]; then
    echo ""
    echo "📋 Step 2: Running tests..."
    
    echo "🧪 Running frontend tests..."
    cd frontend
    npm install --legacy-peer-deps --silent
    npm run lint
    npm test -- --coverage --watchAll=false --silent
    cd ..
    
    echo "🧪 Running backend tests..."
    dotnet test Anela.Heblo.sln --configuration Release --verbosity minimal
    
    echo "✅ All tests passed"
else
    echo ""
    echo "📋 Step 2: Skipping tests (--skip-tests flag)"
fi

# Step 3: Build and push Docker image (optional)
if [ "$SKIP_BUILD" = "false" ]; then
    echo ""
    echo "📋 Step 3: Building and pushing Docker image..."
    ./scripts/build-and-push.sh $ENVIRONMENT
    echo "✅ Docker image ready"
else
    echo ""
    echo "📋 Step 3: Skipping build (--skip-build flag)"
fi

# Step 4: Deploy to Azure
echo ""
echo "📋 Step 4: Deploying to Azure..."
./scripts/deploy-azure.sh $ENVIRONMENT
echo "✅ Azure deployment completed"

# Step 5: Post-deployment verification
echo ""
echo "📋 Step 5: Post-deployment verification..."

if [ "$ENVIRONMENT" = "production" ]; then
    WEBAPP_URL="https://anela-heblo.azurewebsites.net"
else
    WEBAPP_URL="https://anela-heblo-test.azurewebsites.net"
fi

echo "🔍 Performing final verification..."
sleep 10

# Extended health check
echo "🏥 Extended health check..."
for i in {1..5}; do
    if curl -f -s "$WEBAPP_URL/health" > /dev/null; then
        echo "✅ Health check $i/5 passed"
    else
        echo "⚠️ Health check $i/5 failed"
        if [ $i -eq 5 ]; then
            echo "❌ Final health check failed"
            exit 1
        fi
    fi
    sleep 5
done

# Test main functionality
echo "🌐 Testing main page..."
MAIN_STATUS=$(curl -s -o /dev/null -w "%{http_code}" "$WEBAPP_URL/")
echo "📄 Main page: HTTP $MAIN_STATUS"

echo "🔌 Testing API..."
API_STATUS=$(curl -s -o /dev/null -w "%{http_code}" "$WEBAPP_URL/WeatherForecast")
echo "🌤️ API endpoint: HTTP $API_STATUS"

# Success!
echo ""
echo "🎉🎉🎉 DEPLOYMENT SUCCESSFUL! 🎉🎉🎉"
echo ""
echo "📊 Deployment Summary:"
echo "   🌍 Environment: $ENVIRONMENT"
echo "   🌐 URL: $WEBAPP_URL"
echo "   🐳 Docker: Pushed and deployed"
echo "   ✅ Health checks: Passed"
echo ""
echo "🔗 Quick Links:"
echo "   Application: $WEBAPP_URL"
echo "   Health: $WEBAPP_URL/health"
echo "   API: $WEBAPP_URL/WeatherForecast"
echo ""
echo "🛠️ Management Commands:"
echo "   View logs: az webapp log tail --name $([ "$ENVIRONMENT" = "production" ] && echo "anela-heblo" || echo "anela-heblo-test") --resource-group $([ "$ENVIRONMENT" = "production" ] && echo "rg-anela-heblo-prod" || echo "rg-anela-heblo-test")"
echo "   Restart app: az webapp restart --name $([ "$ENVIRONMENT" = "production" ] && echo "anela-heblo" || echo "anela-heblo-test") --resource-group $([ "$ENVIRONMENT" = "production" ] && echo "rg-anela-heblo-prod" || echo "rg-anela-heblo-test")"
echo ""
echo "🚀 Happy deploying!"