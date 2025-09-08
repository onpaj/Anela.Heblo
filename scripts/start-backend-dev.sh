#!/bin/bash

# Script to start backend in Development mode
echo "Starting Anela Heblo Backend - Development Mode"
echo "========================================="

# Kill any processes running on port 5000
echo "Cleaning up port 5000..."
lsof -ti:5000 | xargs kill -9 2>/dev/null || true
sleep 1

# Navigate to backend directory
cd "$(dirname "$0")/../backend/src/Anela.Heblo.API"

# Check if directory exists
if [ ! -d "$(pwd)" ]; then
    echo "Error: Backend directory not found"
    exit 1
fi

echo "Working directory: $(pwd)"
echo "Environment: Development"
echo "Port: 5000"
echo ""

# Start the backend
dotnet run --launch-profile Development