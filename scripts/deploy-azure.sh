#!/bin/bash

# Azure Deployment Script for Anela Heblo
# Deploys the application to Azure Web App for Containers
# Usage: ./deploy-azure.sh [test|production]

set -e  # Exit on any error

# Configuration
ENVIRONMENT=${1:-"test"}
APP_NAME="anela-heblo"
DOCKER_USERNAME="your-docker-username"  # Replace with actual Docker Hub username
IMAGE_NAME="anela-heblo"

# Environment-specific configuration
if [ "$ENVIRONMENT" = "production" ]; then
    RESOURCE_GROUP="rg-anela-heblo-prod"
    WEBAPP_NAME="anela-heblo"
    LOCATION="West Europe"
    SKU="B1"  # Basic tier for production
    DOCKER_TAG="latest"
    API_URL="https://anela-heblo.azurewebsites.net"
    USE_MOCK_AUTH="false"
elif [ "$ENVIRONMENT" = "test" ]; then
    RESOURCE_GROUP="rg-anela-heblo-test"
    WEBAPP_NAME="anela-heblo-test"
    LOCATION="West Europe"
    SKU="F1"  # Free tier for testing
    DOCKER_TAG="test-latest"
    API_URL="https://anela-heblo-test.azurewebsites.net"
    USE_MOCK_AUTH="true"
else
    echo "❌ Invalid environment. Use 'test' or 'production'"
    exit 1
fi

DOCKER_IMAGE="$DOCKER_USERNAME/$IMAGE_NAME:$DOCKER_TAG"

echo "🚀 Starting Azure deployment for $ENVIRONMENT environment"
echo "📦 Resource Group: $RESOURCE_GROUP"
echo "🌐 Web App: $WEBAPP_NAME"
echo "🐳 Docker Image: $DOCKER_IMAGE"
echo ""

# Check if logged in to Azure
echo "🔐 Checking Azure login..."
if ! az account show &> /dev/null; then
    echo "❌ Not logged in to Azure. Please run 'az login' first."
    exit 1
fi

SUBSCRIPTION=$(az account show --query name -o tsv)
echo "✅ Logged in to Azure subscription: $SUBSCRIPTION"

# Step 1: Create Resource Group
echo ""
echo "📋 Step 1: Creating resource group..."
if az group show --name $RESOURCE_GROUP &> /dev/null; then
    echo "✅ Resource group $RESOURCE_GROUP already exists"
else
    echo "🔨 Creating resource group $RESOURCE_GROUP..."
    az group create \
        --name $RESOURCE_GROUP \
        --location "$LOCATION"
    echo "✅ Resource group created"
fi

# Step 2: Create App Service Plan
echo ""
echo "📋 Step 2: Creating App Service Plan..."
PLAN_NAME="$WEBAPP_NAME-plan"
if az appservice plan show --name $PLAN_NAME --resource-group $RESOURCE_GROUP &> /dev/null; then
    echo "✅ App Service Plan $PLAN_NAME already exists"
else
    echo "🔨 Creating App Service Plan $PLAN_NAME..."
    az appservice plan create \
        --name $PLAN_NAME \
        --resource-group $RESOURCE_GROUP \
        --sku $SKU \
        --is-linux
    echo "✅ App Service Plan created"
fi

# Step 3: Create Web App
echo ""
echo "📋 Step 3: Creating Web App..."
if az webapp show --name $WEBAPP_NAME --resource-group $RESOURCE_GROUP &> /dev/null; then
    echo "✅ Web App $WEBAPP_NAME already exists"
else
    echo "🔨 Creating Web App $WEBAPP_NAME..."
    az webapp create \
        --name $WEBAPP_NAME \
        --resource-group $RESOURCE_GROUP \
        --plan $PLAN_NAME \
        --deployment-container-image-name $DOCKER_IMAGE
    echo "✅ Web App created"
fi

# Step 4: Configure Container Settings
echo ""
echo "📋 Step 4: Configuring container settings..."
echo "🐳 Setting Docker image to $DOCKER_IMAGE..."
az webapp config container set \
    --name $WEBAPP_NAME \
    --resource-group $RESOURCE_GROUP \
    --docker-custom-image-name $DOCKER_IMAGE \
    --docker-registry-server-url https://index.docker.io/v1/

echo "✅ Container configured"

# Step 5: Configure App Settings
echo ""
echo "📋 Step 5: Configuring application settings..."

if [ "$ENVIRONMENT" = "production" ]; then
    echo "📝 Production environment - configuring for real Azure AD authentication"
    echo "⚠️  Note: You need to set Azure AD secrets after deployment:"
    echo "   az webapp config appsettings set --name $WEBAPP_NAME --resource-group $RESOURCE_GROUP --settings REACT_APP_AZURE_CLIENT_ID=\"your-client-id\" REACT_APP_AZURE_AUTHORITY=\"your-authority\""
    
    az webapp config appsettings set \
        --name $WEBAPP_NAME \
        --resource-group $RESOURCE_GROUP \
        --settings \
            ASPNETCORE_ENVIRONMENT=$ENVIRONMENT \
            REACT_APP_API_URL=$API_URL \
            REACT_APP_USE_MOCK_AUTH=$USE_MOCK_AUTH \
            WEBSITES_PORT=8080 \
            WEBSITES_ENABLE_APP_SERVICE_STORAGE=false \
            DOCKER_REGISTRY_SERVER_URL=https://index.docker.io \
            SCM_DO_BUILD_DURING_DEPLOYMENT=false
else
    echo "🧪 Test environment - using mock authentication"
    az webapp config appsettings set \
        --name $WEBAPP_NAME \
        --resource-group $RESOURCE_GROUP \
        --settings \
            ASPNETCORE_ENVIRONMENT=$ENVIRONMENT \
            REACT_APP_API_URL=$API_URL \
            REACT_APP_USE_MOCK_AUTH=$USE_MOCK_AUTH \
            WEBSITES_PORT=8080 \
            WEBSITES_ENABLE_APP_SERVICE_STORAGE=false \
            DOCKER_REGISTRY_SERVER_URL=https://index.docker.io \
            SCM_DO_BUILD_DURING_DEPLOYMENT=false
fi

echo "✅ App settings configured"

# Step 6: Enable Container Logging
echo ""
echo "📋 Step 6: Enabling container logging..."
az webapp log config \
    --name $WEBAPP_NAME \
    --resource-group $RESOURCE_GROUP \
    --docker-container-logging filesystem

echo "✅ Container logging enabled"

# Step 7: Restart Web App (force pull latest image)
echo ""
echo "📋 Step 7: Restarting Web App to pull latest image..."
az webapp restart \
    --name $WEBAPP_NAME \
    --resource-group $RESOURCE_GROUP

echo "✅ Web App restarted"

# Step 8: Health Check
echo ""
echo "📋 Step 8: Performing health check..."
WEBAPP_URL="https://$WEBAPP_NAME.azurewebsites.net"
echo "🌐 Web App URL: $WEBAPP_URL"

echo "⏳ Waiting for deployment to complete (this may take a few minutes)..."
sleep 30

# Check health endpoint
echo "🏥 Checking health endpoint..."
for i in {1..12}; do
    if curl -f -s "$WEBAPP_URL/health" > /dev/null; then
        echo "✅ Health check passed!"
        break
    else
        echo "⏳ Attempt $i/12: Health check not ready yet, waiting 30s..."
        if [ $i -eq 12 ]; then
            echo "❌ Health check failed after 6 minutes"
            echo "📋 You can check logs with: az webapp log tail --name $WEBAPP_NAME --resource-group $RESOURCE_GROUP"
            exit 1
        fi
        sleep 30
    fi
done

# Step 9: Final Verification
echo ""
echo "📋 Step 9: Final verification..."
echo "🌐 Testing main page..."
MAIN_STATUS=$(curl -s -o /dev/null -w "%{http_code}" "$WEBAPP_URL/")
if [ "$MAIN_STATUS" = "200" ]; then
    echo "✅ Main page accessible (HTTP $MAIN_STATUS)"
else
    echo "⚠️ Main page returned HTTP $MAIN_STATUS"
fi

echo "🔌 Testing API endpoint..."
API_STATUS=$(curl -s -o /dev/null -w "%{http_code}" "$WEBAPP_URL/WeatherForecast")
if [ "$API_STATUS" = "401" ] || [ "$API_STATUS" = "200" ]; then
    echo "✅ API endpoint accessible (HTTP $API_STATUS)"
else
    echo "⚠️ API endpoint returned HTTP $API_STATUS"
fi

# Success Summary
echo ""
echo "🎉 Deployment completed successfully!"
echo ""
echo "📊 Deployment Summary:"
echo "   Environment: $ENVIRONMENT"
echo "   Resource Group: $RESOURCE_GROUP"
echo "   Web App: $WEBAPP_NAME"
echo "   Docker Image: $DOCKER_IMAGE"
echo "   URL: $WEBAPP_URL"
echo ""
echo "🔧 Useful commands:"
echo "   View logs: az webapp log tail --name $WEBAPP_NAME --resource-group $RESOURCE_GROUP"
echo "   View config: az webapp config show --name $WEBAPP_NAME --resource-group $RESOURCE_GROUP"
echo "   Scale up: az appservice plan update --name $PLAN_NAME --resource-group $RESOURCE_GROUP --sku B2"
echo ""
echo "🌐 Your application is now live at: $WEBAPP_URL"