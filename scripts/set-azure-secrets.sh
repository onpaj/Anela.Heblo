#!/bin/bash

# Azure AD Secrets Configuration Script
# Sets Azure AD authentication secrets for production environment
# Usage: ./set-azure-secrets.sh [client-id] [authority]

set -e

# Configuration
ENVIRONMENT=${1:-"production"}
AZURE_CLIENT_ID=$2
AZURE_AUTHORITY=$3

RESOURCE_GROUP="rgHeblo"

if [ "$ENVIRONMENT" = "production" ]; then
    WEBAPP_NAME="heblo"
elif [ "$ENVIRONMENT" = "test" ]; then
    WEBAPP_NAME="heblo-test"
else
    echo "❌ Invalid environment. Use 'production' or 'test'"
    exit 1
fi

echo "🔐 Setting Azure AD secrets for $ENVIRONMENT environment"
echo "📦 Resource Group: $RESOURCE_GROUP"
echo "🌐 Web App: $WEBAPP_NAME"
echo ""

# Validate parameters
if [ -z "$AZURE_CLIENT_ID" ] || [ -z "$AZURE_AUTHORITY" ]; then
    echo "❌ Missing required parameters"
    echo ""
    echo "Usage:"
    echo "  ./set-azure-secrets.sh production [client-id] [authority]"
    echo ""
    echo "Example:"
    echo "  ./set-azure-secrets.sh production 12345678-1234-1234-1234-123456789012 https://login.microsoftonline.com/your-tenant-id"
    echo ""
    exit 1
fi

# Check if logged in to Azure
echo "🔐 Checking Azure login..."
if ! az account show &> /dev/null; then
    echo "❌ Not logged in to Azure. Please run 'az login' first."
    exit 1
fi

SUBSCRIPTION=$(az account show --query name -o tsv)
echo "✅ Logged in to Azure subscription: $SUBSCRIPTION"

# Set the secrets
echo ""
echo "🔧 Setting Azure AD configuration..."
az webapp config appsettings set \
    --name $WEBAPP_NAME \
    --resource-group $RESOURCE_GROUP \
    --settings \
        REACT_APP_AZURE_CLIENT_ID="$AZURE_CLIENT_ID" \
        REACT_APP_AZURE_AUTHORITY="$AZURE_AUTHORITY" \
        REACT_APP_USE_MOCK_AUTH=false

echo "✅ Azure AD secrets configured successfully"

# Restart the web app to apply new settings
echo ""
echo "🔄 Restarting web app to apply new configuration..."
az webapp restart \
    --name $WEBAPP_NAME \
    --resource-group $RESOURCE_GROUP

echo "✅ Web app restarted"

# Final status
echo ""
echo "🎉 Azure AD authentication configured successfully!"
echo ""
echo "📊 Configuration Summary:"
echo "   Environment: $ENVIRONMENT"
echo "   Web App: $WEBAPP_NAME"
echo "   Client ID: ${AZURE_CLIENT_ID:0:8}..."
echo "   Authority: $AZURE_AUTHORITY"
echo ""

WEBAPP_URL="https://$WEBAPP_NAME.azurewebsites.net"
echo "🌐 Your application is available at: $WEBAPP_URL"
echo ""
echo "⏳ It may take 1-2 minutes for the new configuration to take effect."
echo "🔍 You can check the logs with: az webapp log tail --name $WEBAPP_NAME --resource-group $RESOURCE_GROUP"