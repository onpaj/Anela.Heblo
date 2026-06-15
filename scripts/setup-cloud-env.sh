#!/usr/bin/env bash
#
# setup-cloud-env.sh
#
# Provisions a fresh Linux cloud box (Debian/Ubuntu — Codespaces, CI, dev container)
# with everything needed to build and run the Anela Heblo backend + frontend:
#
#   - .NET 8 SDK        (backend: ASP.NET Core / net8.0)
#   - Node.js 20        (frontend: Create React App)
#   - GitHub CLI (gh)   (project requires gh for all GitHub ops)
#   - SkiaSharp native libs (libfontconfig1, libfreetype6 — PDF generation)
#   - Backend NuGet restore + frontend npm install
#   - (optional) Playwright browsers for E2E
#
# Idempotent: skips anything already installed. Safe to re-run.
#
# Usage:
#   ./scripts/setup-cloud-env.sh              # full setup
#   SKIP_PLAYWRIGHT=1 ./scripts/setup-cloud-env.sh   # skip Playwright browser download
#
set -euo pipefail

# ---------------------------------------------------------------------------
# Config
# ---------------------------------------------------------------------------
readonly DOTNET_CHANNEL="8.0"
readonly NODE_MAJOR="20"
# Prefer $CLAUDE_PROJECT_DIR (set in Claude Code SessionStart hooks) so the
# repo-dependent steps resolve correctly even when this script is invoked from
# outside the repo. Fall back to the script's own location for manual runs.
readonly REPO_ROOT="${CLAUDE_PROJECT_DIR:-$(cd "$(dirname "$0")/.." && pwd)}"

# Use sudo only when not already root (containers usually run as root).
SUDO=""
if [ "$(id -u)" -ne 0 ]; then
  SUDO="sudo"
fi

log()  { printf '\033[1;34m==>\033[0m %s\n' "$*"; }
ok()   { printf '\033[1;32m  ✓\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m  !\033[0m %s\n' "$*"; }

require_debian() {
  if ! command -v apt-get >/dev/null 2>&1; then
    warn "apt-get not found — this script targets Debian/Ubuntu cloud images."
    warn "On macOS use the start-*-dev.sh scripts + brew (dotnet, node, gh) instead."
    exit 1
  fi
}

# ---------------------------------------------------------------------------
# 1. Base apt packages
# ---------------------------------------------------------------------------
install_base() {
  log "Installing base packages (curl, git, ca-certificates, SkiaSharp libs)..."
  $SUDO apt-get update -y
  $SUDO apt-get install -y --no-install-recommends \
    curl wget git ca-certificates gnupg apt-transport-https \
    libfontconfig1 libfreetype6
  ok "Base packages installed"
}

# ---------------------------------------------------------------------------
# 2. .NET 8 SDK (via official dotnet-install script — no apt repo needed)
# ---------------------------------------------------------------------------
install_dotnet() {
  if command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks | grep -q "^${DOTNET_CHANNEL}\."; then
    ok ".NET ${DOTNET_CHANNEL} SDK already present ($(dotnet --version))"
    return
  fi
  log "Installing .NET ${DOTNET_CHANNEL} SDK..."
  local install_dir="${DOTNET_ROOT:-$HOME/.dotnet}"
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  bash /tmp/dotnet-install.sh --channel "${DOTNET_CHANNEL}" --install-dir "${install_dir}"
  rm -f /tmp/dotnet-install.sh

  # Make dotnet available now and in future shells.
  export DOTNET_ROOT="${install_dir}"
  export PATH="${install_dir}:${PATH}"
  local profile="$HOME/.bashrc"
  if ! grep -q 'DOTNET_ROOT' "$profile" 2>/dev/null; then
    {
      echo ""
      echo "# .NET SDK (added by setup-cloud-env.sh)"
      echo "export DOTNET_ROOT=\"${install_dir}\""
      echo "export PATH=\"${install_dir}:\$PATH\""
    } >> "$profile"
  fi
  ok ".NET SDK installed ($(dotnet --version))"
}

# ---------------------------------------------------------------------------
# 3. Node.js 20 (via NodeSource — matches Dockerfile)
# ---------------------------------------------------------------------------
install_node() {
  if command -v node >/dev/null 2>&1 && [ "$(node -v | sed 's/v\([0-9]*\).*/\1/')" -ge "${NODE_MAJOR}" ]; then
    ok "Node.js already present ($(node -v))"
    return
  fi
  log "Installing Node.js ${NODE_MAJOR}.x..."
  curl -fsSL "https://deb.nodesource.com/setup_${NODE_MAJOR}.x" | $SUDO -E bash -
  $SUDO apt-get install -y nodejs
  ok "Node.js installed ($(node -v), npm $(npm -v))"
}

# ---------------------------------------------------------------------------
# 4. GitHub CLI (official apt repo)
# ---------------------------------------------------------------------------
install_gh() {
  if command -v gh >/dev/null 2>&1; then
    ok "GitHub CLI already present ($(gh --version | head -1))"
    return
  fi
  log "Installing GitHub CLI..."
  local keyring="/usr/share/keyrings/githubcli-archive-keyring.gpg"
  curl -fsSL https://cli.github.com/packages/githubcli-archive-keyring.gpg \
    | $SUDO dd of="$keyring" >/dev/null 2>&1
  $SUDO chmod go+r "$keyring"
  echo "deb [arch=$(dpkg --print-architecture) signed-by=$keyring] https://cli.github.com/packages stable main" \
    | $SUDO tee /etc/apt/sources.list.d/github-cli.list >/dev/null
  $SUDO apt-get update -y
  $SUDO apt-get install -y gh
  ok "GitHub CLI installed ($(gh --version | head -1))"
}

# ---------------------------------------------------------------------------
# 5. Restore project dependencies
# ---------------------------------------------------------------------------
require_repo() {
  if [ ! -f "${REPO_ROOT}/Anela.Heblo.sln" ]; then
    warn "Repository not found at REPO_ROOT='${REPO_ROOT}' (no Anela.Heblo.sln)."
    warn "Repo-dependent steps (dotnet restore / npm install) need the checked-out"
    warn "repo. This script must run from inside the repo or with \$CLAUDE_PROJECT_DIR"
    warn "set — it cannot run as a cloud 'setup script' field (repo not yet present)."
    warn "Use the SessionStart hook (scripts/cloud-session-setup.sh) for these steps."
    exit 1
  fi
}

restore_backend() {
  log "Restoring backend NuGet packages..."
  dotnet restore "${REPO_ROOT}/Anela.Heblo.sln"
  ok "Backend restored"
}

restore_frontend() {
  log "Installing frontend npm packages (--legacy-peer-deps, matches Dockerfile)..."
  npm --prefix "${REPO_ROOT}/frontend" install --legacy-peer-deps
  ok "Frontend dependencies installed"
}

install_playwright() {
  if [ "${SKIP_PLAYWRIGHT:-0}" = "1" ]; then
    warn "SKIP_PLAYWRIGHT=1 set — skipping Playwright browser download"
    return
  fi
  log "Installing Playwright browsers (chromium + OS deps)..."
  ( cd "${REPO_ROOT}/frontend" && npx --yes playwright install --with-deps chromium )
  ok "Playwright browsers installed"
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------
main() {
  log "Anela Heblo — cloud environment setup"
  log "Repo root: ${REPO_ROOT}"
  echo

  require_debian
  install_base
  install_dotnet
  install_node
  install_gh
  require_repo
  restore_backend
  restore_frontend
  install_playwright

  echo
  log "Setup complete 🎉"
  cat <<EOF

  Next steps:
    • Restart your shell (or: source ~/.bashrc) so dotnet is on PATH.
    • Authenticate GitHub:   gh auth login
    • Run the backend:       ./scripts/start-backend-dev.sh    (http://localhost:5000)
    • Run the frontend:      ./scripts/start-frontend-dev.sh   (http://localhost:3000)

  Verify the toolchain:
    dotnet --version    node -v    npm -v    gh --version
EOF
}

main "$@"
