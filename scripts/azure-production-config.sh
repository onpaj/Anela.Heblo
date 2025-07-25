#!/bin/bash

# Azure Production Configuration Script
# Sets required app settings for production environment

set -e

RESOURCE_GROUP="rgHeblo"
WEBAPP_NAME="heblo"

echo "Configuring Azure Web App: $WEBAPP_NAME in resource group: $RESOURCE_GROUP"

# Get the actual webapp name (Azure may append random suffix)
ACTUAL_WEBAPP_NAME=$(az webapp list \
  --resource-group $RESOURCE_GROUP \
  --query "[?contains(name, '$WEBAPP_NAME') && !contains(name, 'test')].name" \
  -o tsv | head -1)

if [ -z "$ACTUAL_WEBAPP_NAME" ]; then
  echo "Error: Could not find webapp starting with '$WEBAPP_NAME' in resource group '$RESOURCE_GROUP'"
  exit 1
fi

echo "Found webapp: $ACTUAL_WEBAPP_NAME"

# Get the actual hostname
HOSTNAME=$(az webapp show \
  --name $ACTUAL_WEBAPP_NAME \
  --resource-group $RESOURCE_GROUP \
  --query "defaultHostName" \
  -o tsv)

echo "Webapp hostname: $HOSTNAME"

# Configure app settings
echo "Setting app settings..."

az webapp config appsettings set \
  --name $ACTUAL_WEBAPP_NAME \
  --resource-group $RESOURCE_GROUP \
  --settings \
    ASPNETCORE_ENVIRONMENT="Production" \
    REACT_APP_API_URL="https://$HOSTNAME" \
    REACT_APP_USE_MOCK_AUTH="false" \
    UseMockAuth="false" \
    WEBSITES_PORT="8080"

echo "Production configuration completed!"
echo ""
echo "Still need to configure manually in Azure Portal:"
echo "1. REACT_APP_AZURE_CLIENT_ID - Azure AD application client ID"
echo "2. REACT_APP_AZURE_AUTHORITY - Azure AD authority URL (https://login.microsoftonline.com/YOUR_TENANT_ID)"
echo ""
echo "Azure AD Redirect URIs to configure:"
echo "- https://$HOSTNAME"
echo "- https://$HOSTNAME/auth/callback"