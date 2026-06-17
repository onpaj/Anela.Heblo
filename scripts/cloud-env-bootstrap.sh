#!/bin/bash
#
# cloud-env-bootstrap.sh
#
# Repo-INDEPENDENT system installs for Claude Code on the web.
#
# Paste the contents of this file into your cloud environment's "Setup script"
# field (web UI). It runs BEFORE Claude Code launches, before the repo is
# checked out, so it must NOT reference any repo files — it only installs the
# system toolchain the base image lacks (.NET 8 SDK, GitHub CLI, SkiaSharp libs).
#
# Repo-dependent setup (dotnet restore / npm install) is handled separately by
# the SessionStart hook scripts/cloud-session-setup.sh, which runs after launch
# with $CLAUDE_PROJECT_DIR pointing at the checked-out repo.
#
# Output is cached by the environment, so these installs persist across new
# sessions without re-downloading. Idempotent and safe to re-run.
#
set -e

DOTNET_CHANNEL="8.0"

# --- SkiaSharp native libs (PDF generation) --------------------------------
apt-get update -y
apt-get install -y --no-install-recommends \
  curl ca-certificates gnupg libfontconfig1 libfreetype6

# --- .NET 8 SDK (not pre-installed) ----------------------------------------
# Install to /usr/local so `dotnet` is on PATH for the hook, Claude, and shells
# without sourcing a profile.
if ! command -v dotnet >/dev/null 2>&1; then
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  bash /tmp/dotnet-install.sh --channel "${DOTNET_CHANNEL}" --install-dir /usr/local/dotnet
  rm -f /tmp/dotnet-install.sh
  ln -sf /usr/local/dotnet/dotnet /usr/local/bin/dotnet
fi

# --- GitHub CLI (not pre-installed) ----------------------------------------
if ! command -v gh >/dev/null 2>&1; then
  keyring="/usr/share/keyrings/githubcli-archive-keyring.gpg"
  curl -fsSL https://cli.github.com/packages/githubcli-archive-keyring.gpg | dd of="$keyring"
  chmod go+r "$keyring"
  echo "deb [arch=$(dpkg --print-architecture) signed-by=$keyring] https://cli.github.com/packages stable main" \
    > /etc/apt/sources.list.d/github-cli.list
  apt-get update -y
  apt-get install -y gh
fi

echo "cloud-env-bootstrap: system toolchain ready (dotnet $(dotnet --version), gh $(gh --version | head -1))"
