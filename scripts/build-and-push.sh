#!/bin/bash

# Docker Build and Push Script for Anela Heblo
# Builds Docker image and pushes to Docker Hub
# Usage: ./build-and-push.sh [test|production]

set -e  # Exit on any error

# Configuration
ENVIRONMENT=${1:-"test"}
DOCKER_USERNAME="your-docker-username"  # Replace with actual Docker Hub username
IMAGE_NAME="anela-heblo"

# Environment-specific configuration
if [ "$ENVIRONMENT" = "production" ]; then
    DOCKER_TAG="latest"
    API_URL="https://anela-heblo.azurewebsites.net"
    USE_MOCK_AUTH="false"
    VERSION_TAG="v$(date +%Y%m%d-%H%M%S)"  # Add timestamp version
elif [ "$ENVIRONMENT" = "test" ]; then
    DOCKER_TAG="test-latest"
    API_URL="https://anela-heblo-test.azurewebsites.net"
    USE_MOCK_AUTH="true"
    VERSION_TAG="test-$(date +%Y%m%d-%H%M%S)"
else
    echo "❌ Invalid environment. Use 'test' or 'production'"
    exit 1
fi

DOCKER_IMAGE="$DOCKER_USERNAME/$IMAGE_NAME"
FULL_IMAGE_TAG="$DOCKER_IMAGE:$DOCKER_TAG"
VERSION_IMAGE_TAG="$DOCKER_IMAGE:$VERSION_TAG"

echo "🐳 Building and pushing Docker image for $ENVIRONMENT environment"
echo "📦 Image: $FULL_IMAGE_TAG"
echo "🏷️ Version: $VERSION_IMAGE_TAG"
echo "🌐 API URL: $API_URL"
echo "🔐 Mock Auth: $USE_MOCK_AUTH"
echo ""

# Check if logged in to Docker Hub
echo "🔐 Checking Docker Hub login..."
if ! docker info | grep -i username &> /dev/null; then
    echo "❌ Not logged in to Docker Hub. Please run 'docker login' first."
    exit 1
fi
echo "✅ Docker Hub login verified"

# Step 1: Build Docker Image
echo ""
echo "📋 Step 1: Building Docker image..."
echo "🔨 Building with build args:"
echo "   REACT_APP_API_URL=$API_URL"
echo "   REACT_APP_USE_MOCK_AUTH=$USE_MOCK_AUTH"

docker build \
    --tag $FULL_IMAGE_TAG \
    --tag $VERSION_IMAGE_TAG \
    --build-arg REACT_APP_API_URL=$API_URL \
    --build-arg REACT_APP_USE_MOCK_AUTH=$USE_MOCK_AUTH \
    .

echo "✅ Docker image built successfully"

# Step 2: Test Docker Image Locally
echo ""
echo "📋 Step 2: Testing Docker image locally..."
echo "🧪 Starting test container..."

# Stop and remove any existing test container
docker stop anela-test-container 2>/dev/null || true
docker rm anela-test-container 2>/dev/null || true

# Start test container
docker run -d -p 8081:8080 --name anela-test-container $FULL_IMAGE_TAG

echo "⏳ Waiting for container to start..."
sleep 10

# Test health endpoint
echo "🏥 Testing health endpoint..."
if curl -f -s http://localhost:8081/health > /dev/null; then
    echo "✅ Local health check passed"
else
    echo "❌ Local health check failed"
    echo "📋 Container logs:"
    docker logs anela-test-container
    docker stop anela-test-container
    docker rm anela-test-container
    exit 1
fi

# Test main page
echo "🌐 Testing main page..."
MAIN_STATUS=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:8081/)
if [ "$MAIN_STATUS" = "200" ]; then
    echo "✅ Main page test passed (HTTP $MAIN_STATUS)"
else
    echo "❌ Main page test failed (HTTP $MAIN_STATUS)"
fi

# Cleanup test container
echo "🧹 Cleaning up test container..."
docker stop anela-test-container
docker rm anela-test-container

echo "✅ Local tests completed"

# Step 3: Push to Docker Hub
echo ""
echo "📋 Step 3: Pushing to Docker Hub..."
echo "📤 Pushing $FULL_IMAGE_TAG..."
docker push $FULL_IMAGE_TAG

echo "📤 Pushing $VERSION_IMAGE_TAG..."
docker push $VERSION_IMAGE_TAG

echo "✅ Images pushed to Docker Hub successfully"

# Step 4: Summary
echo ""
echo "🎉 Build and push completed successfully!"
echo ""
echo "📊 Build Summary:"
echo "   Environment: $ENVIRONMENT"
echo "   Main Tag: $FULL_IMAGE_TAG"
echo "   Version Tag: $VERSION_IMAGE_TAG"
echo "   API URL: $API_URL"
echo "   Mock Auth: $USE_MOCK_AUTH"
echo ""
echo "🚀 Ready for deployment!"
echo "   Run: ./scripts/deploy-azure.sh $ENVIRONMENT"
echo ""
echo "🔍 Verify on Docker Hub:"
echo "   https://hub.docker.com/r/$DOCKER_USERNAME/$IMAGE_NAME/tags"