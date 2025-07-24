#!/bin/bash

# Azure Utilities Script for Anela Heblo
# Common management operations for Azure Web Apps
# Usage: ./azure-utils.sh [command] [environment]

set -e

COMMAND=${1:-"help"}
ENVIRONMENT=${2:-"test"}

# Environment-specific configuration
if [ "$ENVIRONMENT" = "production" ]; then
    RESOURCE_GROUP="rg-anela-heblo-prod"
    WEBAPP_NAME="anela-heblo"
    WEBAPP_URL="https://anela-heblo.azurewebsites.net"
elif [ "$ENVIRONMENT" = "test" ]; then
    RESOURCE_GROUP="rg-anela-heblo-test"
    WEBAPP_NAME="anela-heblo-test"
    WEBAPP_URL="https://anela-heblo-test.azurewebsites.net"
else
    echo "❌ Invalid environment. Use 'test' or 'production'"
    exit 1
fi

case $COMMAND in
    "logs")
        echo "📋 Showing logs for $WEBAPP_NAME..."
        az webapp log tail --name $WEBAPP_NAME --resource-group $RESOURCE_GROUP
        ;;
    
    "logs-download")
        echo "📥 Downloading logs for $WEBAPP_NAME..."
        TIMESTAMP=$(date +%Y%m%d-%H%M%S)
        LOGFILE="logs-$WEBAPP_NAME-$TIMESTAMP.zip"
        az webapp log download --name $WEBAPP_NAME --resource-group $RESOURCE_GROUP --log-file $LOGFILE
        echo "✅ Logs downloaded to: $LOGFILE"
        ;;
    
    "restart")
        echo "🔄 Restarting $WEBAPP_NAME..."
        az webapp restart --name $WEBAPP_NAME --resource-group $RESOURCE_GROUP
        echo "✅ App restarted"
        ;;
    
    "stop")
        echo "⏹️ Stopping $WEBAPP_NAME..."
        az webapp stop --name $WEBAPP_NAME --resource-group $RESOURCE_GROUP
        echo "✅ App stopped"
        ;;
    
    "start")
        echo "▶️ Starting $WEBAPP_NAME..."
        az webapp start --name $WEBAPP_NAME --resource-group $RESOURCE_GROUP
        echo "✅ App started"
        ;;
    
    "status")
        echo "📊 Status for $WEBAPP_NAME..."
        echo "🌐 URL: $WEBAPP_URL"
        
        # Get app state
        STATE=$(az webapp show --name $WEBAPP_NAME --resource-group $RESOURCE_GROUP --query state -o tsv)
        echo "🔄 State: $STATE"
        
        # Test health
        echo "🏥 Health check..."
        if curl -f -s "$WEBAPP_URL/health" > /dev/null; then
            echo "✅ Health: OK"
        else
            echo "❌ Health: FAILED"
        fi
        
        # Test main page
        MAIN_STATUS=$(curl -s -o /dev/null -w "%{http_code}" "$WEBAPP_URL/")
        echo "📄 Main page: HTTP $MAIN_STATUS"
        
        # Test API
        API_STATUS=$(curl -s -o /dev/null -w "%{http_code}" "$WEBAPP_URL/WeatherForecast")
        echo "🔌 API: HTTP $API_STATUS"
        ;;
    
    "config")
        echo "⚙️ Configuration for $WEBAPP_NAME..."
        az webapp config show --name $WEBAPP_NAME --resource-group $RESOURCE_GROUP
        ;;
    
    "settings")
        echo "🔧 App settings for $WEBAPP_NAME..."
        az webapp config appsettings list --name $WEBAPP_NAME --resource-group $RESOURCE_GROUP --output table
        ;;
    
    "scale-up")
        SKU=${3:-"B2"}
        PLAN_NAME="$WEBAPP_NAME-plan"
        echo "📈 Scaling up $WEBAPP_NAME to $SKU..."
        az appservice plan update --name $PLAN_NAME --resource-group $RESOURCE_GROUP --sku $SKU
        echo "✅ Scaled to $SKU"
        ;;
    
    "scale-down")
        SKU=${3:-"B1"}
        PLAN_NAME="$WEBAPP_NAME-plan"
        echo "📉 Scaling down $WEBAPP_NAME to $SKU..."
        az appservice plan update --name $PLAN_NAME --resource-group $RESOURCE_GROUP --sku $SKU
        echo "✅ Scaled to $SKU"
        ;;
    
    "update-image")
        DOCKER_IMAGE=${3:-"your-docker-username/anela-heblo:latest"}
        echo "🐳 Updating Docker image to $DOCKER_IMAGE..."
        az webapp config container set \
            --name $WEBAPP_NAME \
            --resource-group $RESOURCE_GROUP \
            --docker-custom-image-name $DOCKER_IMAGE
        echo "🔄 Restarting to pull new image..."
        az webapp restart --name $WEBAPP_NAME --resource-group $RESOURCE_GROUP
        echo "✅ Image updated"
        ;;
    
    "ssh")
        echo "🔌 Opening SSH connection to $WEBAPP_NAME..."
        echo "ℹ️ This will open SSH in your browser"
        az webapp ssh --name $WEBAPP_NAME --resource-group $RESOURCE_GROUP
        ;;
    
    "delete")
        echo "⚠️ This will DELETE the entire $ENVIRONMENT environment!"
        echo "📦 Resource Group: $RESOURCE_GROUP"
        echo "🌐 Web App: $WEBAPP_NAME"
        echo ""
        read -p "Type 'DELETE' to confirm: " CONFIRMATION
        
        if [ "$CONFIRMATION" = "DELETE" ]; then
            echo "🗑️ Deleting resource group $RESOURCE_GROUP..."
            az group delete --name $RESOURCE_GROUP --yes --no-wait
            echo "✅ Deletion initiated (running in background)"
        else
            echo "❌ Deletion cancelled"
        fi
        ;;
    
    "costs")
        echo "💰 Cost analysis for $RESOURCE_GROUP..."
        # Note: This requires Azure Cost Management API
        echo "📊 Resource usage:"
        az resource list --resource-group $RESOURCE_GROUP --output table
        echo ""
        echo "💡 For detailed cost analysis, check:"
        echo "   https://portal.azure.com/#blade/Microsoft_Azure_CostManagement/Menu/costanalysis"
        ;;
    
    "backup")
        echo "💾 Creating backup configuration for $WEBAPP_NAME..."
        STORAGE_ACCOUNT="anelaheblo${ENVIRONMENT}backup"
        echo "📦 Storage account: $STORAGE_ACCOUNT"
        
        # Note: This is a simplified backup setup
        echo "ℹ️ Manual backup setup required. Please configure in Azure Portal:"
        echo "   https://portal.azure.com/#resource/subscriptions/{subscription}/resourceGroups/$RESOURCE_GROUP/providers/Microsoft.Web/sites/$WEBAPP_NAME/backup"
        ;;
    
    "monitor")
        echo "📊 Monitoring $WEBAPP_NAME..."
        echo "🌐 URL: $WEBAPP_URL"
        
        # Continuous monitoring loop
        while true; do
            TIMESTAMP=$(date '+%Y-%m-%d %H:%M:%S')
            if curl -f -s "$WEBAPP_URL/health" > /dev/null; then
                echo "[$TIMESTAMP] ✅ Health OK"
            else
                echo "[$TIMESTAMP] ❌ Health FAILED"
            fi
            sleep 30
        done
        ;;
    
    "help"|*)
        echo "🛠️ Azure Utilities for Anela Heblo"
        echo ""
        echo "Usage: ./azure-utils.sh [command] [environment] [options]"
        echo ""
        echo "Commands:"
        echo "  logs              - Show live application logs"
        echo "  logs-download     - Download logs as zip file"
        echo "  restart           - Restart the web app"
        echo "  stop              - Stop the web app"
        echo "  start             - Start the web app"
        echo "  status            - Show app status and health"
        echo "  config            - Show web app configuration"
        echo "  settings          - Show app settings"
        echo "  scale-up [sku]    - Scale up (default: B2)"
        echo "  scale-down [sku]  - Scale down (default: B1)"
        echo "  update-image [img]- Update Docker image"
        echo "  ssh               - Open SSH connection"
        echo "  delete            - Delete entire environment"
        echo "  costs             - Show cost information"
        echo "  backup            - Setup backup configuration"
        echo "  monitor           - Continuous health monitoring"
        echo "  help              - Show this help"
        echo ""
        echo "Environments: test, production"
        echo ""
        echo "Examples:"
        echo "  ./azure-utils.sh logs test"
        echo "  ./azure-utils.sh restart production"
        echo "  ./azure-utils.sh scale-up production B2"
        echo "  ./azure-utils.sh update-image test myuser/anela-heblo:v1.2.3"
        ;;
esac