#!/bin/bash

# Start frontend for automation testing on port 3001

cd "$(dirname "$0")/../frontend"

echo "🤖 Starting frontend for automation testing on port 3001..."
echo "API URL: http://localhost:5001"
echo "Mock Auth: Enabled"

# Kill existing process on port 3001 if any
lsof -ti:3001 | xargs kill -9 2>/dev/null || true

# Start frontend with automation configuration
npm run start:automation