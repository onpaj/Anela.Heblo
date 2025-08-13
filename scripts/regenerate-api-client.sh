#!/bin/bash

# Script for regenerating OpenAPI TypeScript client for frontend
# This script builds the backend and generates the client using NSwag

set -e  # Exit on any error

echo "🔄 Regenerating OpenAPI TypeScript client..."

# Change to backend API directory
cd "$(dirname "$0")/../backend/src/Anela.Heblo.API"

echo "📦 Building backend project..."
dotnet build --no-restore --verbosity quiet

echo "🚀 Generating TypeScript API client..."
dotnet msbuild -t:GenerateFrontendClientManual

echo "✅ API client regenerated successfully!"
echo "📁 Generated file: frontend/src/api/generated/api-client.ts"

# Optional: Check if the generated file exists
GENERATED_FILE="../../../frontend/src/api/generated/api-client.ts"
if [ -f "$GENERATED_FILE" ]; then
    echo "📈 File size: $(du -h "$GENERATED_FILE" | cut -f1)"
    echo "📅 Last modified: $(stat -f "%Sm" -t "%Y-%m-%d %H:%M:%S" "$GENERATED_FILE")"
else
    echo "❌ Generated file not found at expected location!"
    exit 1
fi

echo "🎉 API client regeneration completed!"