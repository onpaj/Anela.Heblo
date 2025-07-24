#!/bin/bash

# Quick Production Docker Run with Environment Variables
# Expects AZURE_CLIENT_ID and AZURE_AUTHORITY to be set in environment

set -e

if [ -z "$AZURE_CLIENT_ID" ] || [ -z "$AZURE_AUTHORITY" ]; then
    echo "❌ Please set environment variables first:"
    echo ""
    echo "export AZURE_CLIENT_ID=\"your-client-id\""
    echo "export AZURE_AUTHORITY=\"https://login.microsoftonline.com/your-tenant-id\""
    echo ""
    exit 1
fi

echo "🚀 Running production Docker with environment variables..."
./scripts/run-production-local.sh "$AZURE_CLIENT_ID" "$AZURE_AUTHORITY"