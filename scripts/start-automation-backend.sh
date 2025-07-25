#!/bin/bash

# Start backend for automation testing on port 5001

cd "$(dirname "$0")/../backend/src/Anela.Heblo.API"

echo "🤖 Starting backend for automation testing on port 5001..."
echo "Environment: Automation"
echo "Mock Auth: Enabled"

# Kill existing process on port 5001 if any
lsof -ti:5001 | xargs kill -9 2>/dev/null || true

# Start backend with Automation profile
dotnet run --launch-profile Automation