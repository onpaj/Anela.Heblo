#!/bin/sh
set -e

MAIN=$(git worktree list 2>/dev/null | head -1 | awk '{print $1}')

if [ -n "$MAIN" ] && [ "$MAIN" != "$(pwd)" ]; then
  find "$MAIN" -maxdepth 1 -name '.env*' -exec cp -n {} . \; 2>/dev/null || true
  find "$MAIN/frontend" -maxdepth 1 -name '.env*' -exec cp -n {} ./frontend/ \; 2>/dev/null || true
fi

dotnet restore
npm --prefix frontend install --legacy-peer-deps
