#!/bin/bash

# Fix Azure Web App Container Deployment
# Usage: ./fix-container-deployment.sh [webapp-name] [environment]

set -e

WEBAPP_NAME=${1:-"heblo"}
ENVIRONMENT=${2:-"production"}
RESOURCE_GROUP="rgHeblo"
DOCKER_USERNAME="remiiik"
IMAGE_NAME="heblo"

if [ "$ENVIRONMENT" = "production" ]; then
    DOCKER_TAG="latest"
else
    DOCKER_TAG="test-latest"
fi

DOCKER_IMAGE="$DOCKER_USERNAME/$IMAGE_NAME:$DOCKER_TAG"

echo "🔧 Fixing Azure Web App container deployment"
echo "📋 Configuration:"
echo "   Web App: $WEBAPP_NAME"
echo "   Resource Group: $RESOURCE_GROUP" 
echo "   Docker Image: $DOCKER_IMAGE"
echo "   Environment: $ENVIRONMENT"
echo ""

# Check if logged in to Azure
if ! az account show &> /dev/null; then
    echo "❌ Not logged in to Azure. Please run 'az login' first."
    exit 1
fi

# Get current container configuration
echo "🔍 Checking current container configuration..."
CURRENT_IMAGE=$(az webapp config container show \
    --name $WEBAPP_NAME \
    --resource-group $RESOURCE_GROUP \
    --query "linuxFxVersion" -o tsv 2>/dev/null || echo "None")

echo "📊 Current container: $CURRENT_IMAGE"

# Set the correct container image
echo ""
echo "🐳 Setting Docker container image..."
az webapp config container set \
    --name $WEBAPP_NAME \
    --resource-group $RESOURCE_GROUP \
    --docker-custom-image-name $DOCKER_IMAGE \
    --docker-registry-server-url https://index.docker.io/v1/

echo "✅ Container image updated"

# Configure container settings
echo ""
echo "🔧 Configuring container settings..."
az webapp config appsettings set \
    --name $WEBAPP_NAME \
    --resource-group $RESOURCE_GROUP \
    --settings \
        WEBSITES_ENABLE_APP_SERVICE_STORAGE=false \
        WEBSITES_PORT=8080 \
        DOCKER_REGISTRY_SERVER_URL=https://index.docker.io \
        SCM_DO_BUILD_DURING_DEPLOYMENT=false

echo "✅ Container settings configured"

# Enable container logging
echo ""
echo "📋 Enabling container logging..."
az webapp log config \
    --name $WEBAPP_NAME \
    --resource-group $RESOURCE_GROUP \
    --docker-container-logging filesystem

echo "✅ Container logging enabled"

# Restart the web app
echo ""
echo "🔄 Restarting web app to pull new container..."
az webapp restart \
    --name $WEBAPP_NAME \
    --resource-group $RESOURCE_GROUP

echo "✅ Web app restarted"

# Get web app URL
WEBAPP_URL=$(az webapp show \
    --name $WEBAPP_NAME \
    --resource-group $RESOURCE_GROUP \
    --query "defaultHostName" -o tsv)

echo ""
echo "⏳ Waiting for container to start (60 seconds)..."
sleep 60

# Health check
echo ""
echo "🏥 Testing application..."
HTTP_STATUS=$(curl -s -o /dev/null -w "%{http_code}" "https://$WEBAPP_URL/health" 2>/dev/null || echo "000")

if [ "$HTTP_STATUS" = "200" ]; then
    echo "✅ Health check passed!"
    echo "🎉 Application is running successfully"
else
    echo "⚠️ Health check returned: $HTTP_STATUS"
    echo "🔍 Checking logs..."
    
    # Show recent logs
    echo ""
    echo "📋 Recent container logs:"
    az webapp log tail \
        --name $WEBAPP_NAME \
        --resource-group $RESOURCE_GROUP \
        --provider application \
        | head -20
fi

echo ""
echo "🌐 Application URL: https://$WEBAPP_URL"
echo ""
echo "🔧 Useful commands:"
echo "   View logs: az webapp log tail --name $WEBAPP_NAME --resource-group $RESOURCE_GROUP"
echo "   View config: az webapp config container show --name $WEBAPP_NAME --resource-group $RESOURCE_GROUP"
echo "   View app settings: az webapp config appsettings list --name $WEBAPP_NAME --resource-group $RESOURCE_GROUP"