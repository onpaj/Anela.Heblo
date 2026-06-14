#!/bin/bash

# Deploy the "App Usage Analytics" Azure Workbook to the production
# Application Insights resource (aiHeblo). Idempotent: re-running updates the
# workbook in place (the workbook id is a fixed GUID).
#
# Usage: ./deploy-usage-workbook.sh
# Requires: az login to the subscription that owns rgHeblo, and jq.

set -euo pipefail

# Configuration
RESOURCE_GROUP="rgHeblo"
APP_INSIGHTS_NAME="aiHeblo"
DISPLAY_NAME="App Usage Analytics"
# Fixed GUID so re-deploys update the same workbook resource. Do not change.
WORKBOOK_ID="3f9a1c64-7b2e-4c8a-9d51-0a6b2e7c4d18"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TEMPLATE_FILE="$SCRIPT_DIR/usage-analytics-workbook.template.json"
CONTENT_FILE="$SCRIPT_DIR/usage-analytics-workbook.content.json"

echo "📊 Deploying '$DISPLAY_NAME' workbook to $APP_INSIGHTS_NAME ($RESOURCE_GROUP)"
echo ""

# Preconditions
command -v jq >/dev/null 2>&1 || { echo "❌ jq is required but not installed."; exit 1; }
[ -f "$TEMPLATE_FILE" ] || { echo "❌ Template not found: $TEMPLATE_FILE"; exit 1; }
[ -f "$CONTENT_FILE" ] || { echo "❌ Content not found: $CONTENT_FILE"; exit 1; }

echo "🔐 Checking Azure login..."
if ! az account show >/dev/null 2>&1; then
  echo "❌ Not logged in to Azure. Run 'az login' first."
  exit 1
fi
SUBSCRIPTION_ID="$(az account show --query id -o tsv)"
SUBSCRIPTION_NAME="$(az account show --query name -o tsv)"
echo "✅ Subscription: $SUBSCRIPTION_NAME ($SUBSCRIPTION_ID)"
echo ""

# Build the Application Insights resource id (workbook scope).
APP_INSIGHTS_ID="/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RESOURCE_GROUP/providers/microsoft.insights/components/$APP_INSIGHTS_NAME"

echo "🔎 Verifying $APP_INSIGHTS_NAME exists..."
if ! az resource show --ids "$APP_INSIGHTS_ID" >/dev/null 2>&1; then
  echo "❌ Application Insights resource not found: $APP_INSIGHTS_ID"
  echo "   Check you are logged in to the correct subscription."
  exit 1
fi
echo "✅ Found $APP_INSIGHTS_NAME"
echo ""

# Serialize the workbook content (compact) and build a parameters file.
SERIALIZED_DATA="$(jq -c . "$CONTENT_FILE")"
PARAMS_FILE="$(mktemp)"
trap 'rm -f "$PARAMS_FILE"' EXIT
jq -n \
  --arg sd "$SERIALIZED_DATA" \
  --arg appId "$APP_INSIGHTS_ID" \
  --arg id "$WORKBOOK_ID" \
  --arg dn "$DISPLAY_NAME" \
  '{
    serializedData: { value: $sd },
    appInsightsId: { value: $appId },
    workbookId: { value: $id },
    workbookDisplayName: { value: $dn }
  }' > "$PARAMS_FILE"

echo "🚀 Deploying workbook..."
az deployment group create \
  --resource-group "$RESOURCE_GROUP" \
  --name "deploy-usage-workbook" \
  --template-file "$TEMPLATE_FILE" \
  --parameters "@$PARAMS_FILE" \
  --output none

echo ""
echo "✅ Done. Open it in the Azure Portal:"
echo "   $APP_INSIGHTS_NAME → Monitoring → Workbooks → '$DISPLAY_NAME'"
