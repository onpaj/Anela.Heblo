#!/bin/bash

# Local Production Docker Run Script
# Runs the production Docker image locally with Azure AD authentication
# Usage: ./run-production-local.sh [client-id] [authority]

set -e

DOCKER_IMAGE="anela-heblo:latest"
CONTAINER_NAME="anela-heblo-local"
PORT="8080"

# Azure AD Configuration
AZURE_CLIENT_ID=$1
AZURE_AUTHORITY=$2

echo "🐳 Starting local production Docker container with Azure AD authentication"
echo ""

# Validate parameters
if [ -z "$AZURE_CLIENT_ID" ] || [ -z "$AZURE_AUTHORITY" ]; then
    echo "❌ Missing required Azure AD parameters"
    echo ""
    echo "Usage:"
    echo "  ./run-production-local.sh [client-id] [authority]"
    echo ""
    echo "Example:"
    echo "  ./run-production-local.sh 12345678-1234-1234-1234-123456789012 https://login.microsoftonline.com/your-tenant-id"
    echo ""
    echo "You can also set environment variables:"
    echo "  export AZURE_CLIENT_ID=\"your-client-id\""
    echo "  export AZURE_AUTHORITY=\"https://login.microsoftonline.com/your-tenant-id\""
    echo "  ./run-production-local.sh \$AZURE_CLIENT_ID \$AZURE_AUTHORITY"
    echo ""
    exit 1
fi

# Check if Docker image exists locally
if ! docker image inspect $DOCKER_IMAGE &> /dev/null; then
    echo "❌ Docker image '$DOCKER_IMAGE' not found locally"
    echo ""
    echo "Please build the image first:"
    echo "  docker build -t $DOCKER_IMAGE ."
    echo ""
    exit 1
fi

# Stop and remove existing container if running
if docker ps -a --format 'table {{.Names}}' | grep -q "^${CONTAINER_NAME}$"; then
    echo "🛑 Stopping and removing existing container..."
    docker stop $CONTAINER_NAME &> /dev/null || true
    docker rm $CONTAINER_NAME &> /dev/null || true
fi

echo "🚀 Starting production container..."
echo "📋 Configuration:"
echo "   Image: $DOCKER_IMAGE"
echo "   Container: $CONTAINER_NAME"
echo "   Port: $PORT"
echo "   Client ID: ${AZURE_CLIENT_ID:0:8}..."
echo "   Authority: $AZURE_AUTHORITY"
echo ""

# Run the container with Azure AD configuration
docker run -d \
    --name $CONTAINER_NAME \
    -p $PORT:8080 \
    -e ASPNETCORE_ENVIRONMENT=Production \
    -e REACT_APP_API_URL="http://localhost:$PORT" \
    -e REACT_APP_USE_MOCK_AUTH=false \
    -e REACT_APP_AZURE_CLIENT_ID="$AZURE_CLIENT_ID" \
    -e REACT_APP_AZURE_AUTHORITY="$AZURE_AUTHORITY" \
    $DOCKER_IMAGE

# Wait for container to start
echo "⏳ Waiting for container to start..."
sleep 3

# Check if container is running
if ! docker ps --format 'table {{.Names}}' | grep -q "^${CONTAINER_NAME}$"; then
    echo "❌ Container failed to start"
    echo ""
    echo "📋 Container logs:"
    docker logs $CONTAINER_NAME
    exit 1
fi

echo "✅ Container started successfully!"
echo ""

# Health check
echo "🏥 Checking application health..."
for i in {1..12}; do
    if curl -f -s "http://localhost:$PORT/health" > /dev/null 2>&1; then
        echo "✅ Health check passed!"
        break
    else
        if [ $i -eq 12 ]; then
            echo "❌ Health check failed after 60 seconds"
            echo ""
            echo "📋 Container logs:"
            docker logs --tail 20 $CONTAINER_NAME
            exit 1
        fi
        echo "⏳ Attempt $i/12: Waiting for application to start..."
        sleep 5
    fi
done

echo ""
echo "🎉 Production application is now running with Azure AD authentication!"
echo ""
echo "📊 Connection Info:"
echo "   🌐 Application URL: http://localhost:$PORT"
echo "   🔐 Authentication: Azure AD (real)"
echo "   🏥 Health endpoint: http://localhost:$PORT/health"
echo "   🔌 API endpoint: http://localhost:$PORT/WeatherForecast"
echo ""
echo "🔧 Useful commands:"
echo "   View logs: docker logs -f $CONTAINER_NAME"
echo "   Stop container: docker stop $CONTAINER_NAME"
echo "   Remove container: docker rm $CONTAINER_NAME"
echo ""
echo "📝 Note: Make sure your Azure AD app registration includes 'http://localhost:$PORT' in redirect URIs"