#!/usr/bin/env bash
# Bootstrap (or reset) authorization groups in STG / PROD.
# Reads connection string + secrets from Azure Key Vault.
# Requires: dotnet 8 SDK; `az login` (or a service principal env) for KV access.
set -euo pipefail

usage() {
  cat <<EOF
Usage: $(basename "$0") <staging|production> [--reset-group <Name>]

Default (no --reset-group): insert-if-missing across all groups defined in
access-matrix.json. Existing DB groups are left untouched.

--reset-group <Name>: locate <Name> in the JSON seedGroups, then clear and
re-add its permissions in the DB. Other groups untouched.

Examples:
  $(basename "$0") staging
  $(basename "$0") staging --reset-group Spravce
  $(basename "$0") production --reset-group AccessManager
EOF
}

if [[ $# -eq 0 || "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

ENV_ARG="$1"
shift

ENV_ARG_LOWER=$(echo "$ENV_ARG" | tr '[:upper:]' '[:lower:]')
case "$ENV_ARG_LOWER" in
  staging|stg)    ENV_NAME="Staging" ;;
  production|prod) ENV_NAME="Production" ;;
  *)
    echo "ERROR: first argument must be 'staging' or 'production' (got '$ENV_ARG')." >&2
    usage >&2
    exit 2
    ;;
esac

# Warn (but don't block) if az isn't authenticated — KV calls will fail clearly later.
if ! az account show >/dev/null 2>&1; then
  echo "WARNING: 'az account show' returned non-zero. Run 'az login' if Key Vault access fails." >&2
fi

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
exec dotnet run \
  --project "$REPO_ROOT/backend/tools/Anela.Heblo.AuthorizationSeeder/Anela.Heblo.AuthorizationSeeder.csproj" \
  -- "$ENV_NAME" "$@"
