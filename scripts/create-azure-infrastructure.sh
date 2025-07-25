#!/bin/bash

# Azure Infrastructure Setup Script
# Creates shared infrastructure for Heblo application
# Usage: ./create-azure-infrastructure.sh

set -e

# Configuration
RESOURCE_GROUP="rgHeblo"
PLAN_NAME="spHeblo"
APP_INSIGHTS_NAME="aiHeblo"
LOCATION="West Europe"
SKU="B1"  # Basic tier

echo "🏗️ Creating Azure infrastructure for Heblo application"
echo ""
echo "📋 Configuration:"
echo "   Resource Group: $RESOURCE_GROUP"
echo "   App Service Plan: $PLAN_NAME"
echo "   Application Insights: $APP_INSIGHTS_NAME"
echo "   Location: $LOCATION"
echo "   SKU: $SKU"
echo ""

# Check if logged in to Azure
echo "🔐 Checking Azure login..."
if ! az account show &> /dev/null; then
    echo "❌ Not logged in to Azure. Please run 'az login' first."
    exit 1
fi

SUBSCRIPTION=$(az account show --query name -o tsv)
echo "✅ Logged in to Azure subscription: $SUBSCRIPTION"
echo ""

# Step 1: Create Resource Group
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
if az appservice plan show --name $PLAN_NAME --resource-group $RESOURCE_GROUP &> /dev/null; then
    echo "✅ App Service Plan $PLAN_NAME already exists"
    
    # Check if we need to scale up
    CURRENT_SKU=$(az appservice plan show --name $PLAN_NAME --resource-group $RESOURCE_GROUP --query sku.name -o tsv)
    if [ "$CURRENT_SKU" != "$SKU" ]; then
        echo "🔄 Scaling App Service Plan from $CURRENT_SKU to $SKU..."
        az appservice plan update \
            --name $PLAN_NAME \
            --resource-group $RESOURCE_GROUP \
            --sku $SKU
        echo "✅ App Service Plan scaled"
    fi
else
    echo "🔨 Creating App Service Plan $PLAN_NAME..."
    az appservice plan create \
        --name $PLAN_NAME \
        --resource-group $RESOURCE_GROUP \
        --sku $SKU \
        --is-linux
    echo "✅ App Service Plan created"
fi

# Step 3: Create Application Insights
echo ""
echo "📋 Step 3: Creating Application Insights..."
if az monitor app-insights component show --app $APP_INSIGHTS_NAME --resource-group $RESOURCE_GROUP &> /dev/null; then
    echo "✅ Application Insights $APP_INSIGHTS_NAME already exists"
else
    echo "🔨 Creating Application Insights $APP_INSIGHTS_NAME..."
    az monitor app-insights component create \
        --app $APP_INSIGHTS_NAME \
        --location $LOCATION \
        --kind web \
        --resource-group $RESOURCE_GROUP \
        --application-type web
    echo "✅ Application Insights created"
fi

# Get Application Insights details
AI_INSTRUMENTATION_KEY=$(az monitor app-insights component show \
    --app $APP_INSIGHTS_NAME \
    --resource-group $RESOURCE_GROUP \
    --query instrumentationKey -o tsv)

AI_CONNECTION_STRING=$(az monitor app-insights component show \
    --app $APP_INSIGHTS_NAME \
    --resource-group $RESOURCE_GROUP \
    --query connectionString -o tsv)

echo ""
echo "🎉 Infrastructure setup completed successfully!"
echo ""
echo "📊 Infrastructure Summary:"
echo "   Resource Group: $RESOURCE_GROUP"
echo "   App Service Plan: $PLAN_NAME ($SKU)"
echo "   Application Insights: $APP_INSIGHTS_NAME"
echo "   Location: $LOCATION"
echo ""
echo "📊 Application Insights Details:"
echo "   Instrumentation Key: ${AI_INSTRUMENTATION_KEY:0:8}..."
echo "   Connection String: InstrumentationKey=${AI_INSTRUMENTATION_KEY:0:8}..."
echo ""
echo "🚀 Next Steps:"
echo "   1. Create production app: ./scripts/deploy-azure.sh production"
echo "   2. Create test app: ./scripts/deploy-azure.sh test"
echo ""
echo "🔧 Useful commands:"
echo "   View resource group: az group show --name $RESOURCE_GROUP"
echo "   View app service plan: az appservice plan show --name $PLAN_NAME --resource-group $RESOURCE_GROUP"
echo "   View Application Insights: az monitor app-insights component show --app $APP_INSIGHTS_NAME --resource-group $RESOURCE_GROUP"