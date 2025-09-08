#!/bin/bash

# Script to start frontend in Development mode
echo "Starting Anela Heblo Frontend - Development Mode"
echo "=========================================="

# Kill any processes running on port 3000
echo "Cleaning up port 3000..."
lsof -ti:3000 | xargs kill -9 2>/dev/null || true
sleep 1

# Navigate to frontend directory
cd "$(dirname "$0")/../frontend"

# Check if directory exists
if [ ! -d "$(pwd)" ]; then
    echo "Error: Frontend directory not found"
    exit 1
fi

echo "Working directory: $(pwd)"
echo "Environment: Development"
echo "Port: 3000"
echo "API URL: http://localhost:5000"
echo ""

# Start the frontend
npm start