#!/bin/bash

# Azure Web App Diagnostics
# Usage: ./diagnose-webapp.sh [webapp-name]

set -e

WEBAPP_NAME=${1:-"heblo"}
RESOURCE_GROUP="rgHeblo"

echo "🔍 Diagnosing Azure Web App: $WEBAPP_NAME"
echo ""

# Check if logged in to Azure
if ! az account show &> /dev/null; then
    echo "❌ Not logged in to Azure. Please run 'az login' first."
    exit 1
fi

# Get basic app info
echo "📋 Basic Information:"
WEBAPP_URL=$(az webapp show \
    --name $WEBAPP_NAME \
    --resource-group $RESOURCE_GROUP \
    --query "defaultHostName" -o tsv 2>/dev/null || echo "NOT_FOUND")

if [ "$WEBAPP_URL" = "NOT_FOUND" ]; then
    echo "❌ Web app '$WEBAPP_NAME' not found in resource group '$RESOURCE_GROUP'"
    echo ""
    echo "Available web apps:"
    az webapp list --resource-group $RESOURCE_GROUP --query "[].name" -o table
    exit 1
fi

echo "   URL: https://$WEBAPP_URL"
echo "   Resource Group: $RESOURCE_GROUP"

# Check app state
APP_STATE=$(az webapp show \
    --name $WEBAPP_NAME \
    --resource-group $RESOURCE_GROUP \
    --query "state" -o tsv)
echo "   State: $APP_STATE"

# Check container configuration
echo ""
echo "🐳 Container Configuration:"
CONTAINER_IMAGE=$(az webapp config container show \
    --name $WEBAPP_NAME \
    --resource-group $RESOURCE_GROUP \
    --query "linuxFxVersion" -o tsv 2>/dev/null || echo "None")
echo "   Image: $CONTAINER_IMAGE"

# Check app settings
echo ""
echo "⚙️ Key App Settings:"
az webapp config appsettings list \
    --name $WEBAPP_NAME \
    --resource-group $RESOURCE_GROUP \
    --query "[?name=='WEBSITES_PORT' || name=='DOCKER_REGISTRY_SERVER_URL' || name=='WEBSITES_ENABLE_APP_SERVICE_STORAGE'].{Name:name, Value:value}" \
    -o table

# Test endpoints
echo ""
echo "🌐 Testing Endpoints:"

# Test main page
MAIN_STATUS=$(curl -s -o /dev/null -w "%{http_code}" "https://$WEBAPP_URL/" 2>/dev/null || echo "FAIL")
echo "   Main page (/): $MAIN_STATUS"

# Test health endpoint
HEALTH_STATUS=$(curl -s -o /dev/null -w "%{http_code}" "https://$WEBAPP_URL/health" 2>/dev/null || echo "FAIL")
echo "   Health (/health): $HEALTH_STATUS"

# Test API endpoint
API_STATUS=$(curl -s -o /dev/null -w "%{http_code}" "https://$WEBAPP_URL/WeatherForecast" 2>/dev/null || echo "FAIL")
echo "   API (/WeatherForecast): $API_STATUS"

# Show recent logs
echo ""
echo "📋 Recent Logs (last 10 lines):"
az webapp log tail \
    --name $WEBAPP_NAME \
    --resource-group $RESOURCE_GROUP \
    --provider application \
    | head -10

echo ""
echo "🔧 Suggested Actions:"
if [ "$CONTAINER_IMAGE" = "None" ] || [[ "$CONTAINER_IMAGE" == *"nginx"* ]]; then
    echo "   ❌ Wrong container image detected"
    echo "   🔧 Run: ./scripts/fix-container-deployment.sh $WEBAPP_NAME"
elif [ "$MAIN_STATUS" != "200" ]; then
    echo "   ⚠️ Application not responding correctly"
    echo "   🔧 Check logs: az webapp log tail --name $WEBAPP_NAME --resource-group $RESOURCE_GROUP"
    echo "   🔧 Restart app: az webapp restart --name $WEBAPP_NAME --resource-group $RESOURCE_GROUP"
else
    echo "   ✅ Application appears to be working correctly"
fi

echo ""
echo "🔧 Useful Commands:"
echo "   View live logs: az webapp log tail --name $WEBAPP_NAME --resource-group $RESOURCE_GROUP"
echo "   Restart app: az webapp restart --name $WEBAPP_NAME --resource-group $RESOURCE_GROUP"
echo "   SSH to container: az webapp ssh --name $WEBAPP_NAME --resource-group $RESOURCE_GROUP"