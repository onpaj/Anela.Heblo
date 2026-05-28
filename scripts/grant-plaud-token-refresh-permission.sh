#!/usr/bin/env bash
# Grant Heblo's managed identity per-secret write access to Plaud--TokensJson in Key Vault.
#
# Topology:
#   stg  -> Web App "heblo-test" + Key Vault "kv-heblo-stg"
#   prod -> Web App "heblo"      + Key Vault "kv-heblo-prod"
#
# Usage:
#   ./scripts/grant-plaud-token-refresh-permission.sh <stg|prod> [--dry-run] [--phase=setup|cleanup] [--force]
#
# Phases:
#   setup (default): seed Plaud--TokensJson in KV from App Settings, grant per-secret RBAC.
#   cleanup:         remove Plaud__TokensJson from Web App App Settings. Run only after
#                    the new build is deployed and verified.
#
# Idempotent — safe to re-run after partial failure.

set -euo pipefail

ENV_ARG="${1:-}"
if [[ -z "$ENV_ARG" || "$ENV_ARG" == "-h" || "$ENV_ARG" == "--help" ]]; then
    cat <<USAGE
Usage: $0 <stg|prod> [--dry-run] [--phase=setup|cleanup] [--force]
USAGE
    exit 1
fi

DRY_RUN=false
PHASE=setup
FORCE=false
for arg in "${@:2}"; do
    case "$arg" in
        --dry-run) DRY_RUN=true ;;
        --phase=*) PHASE="${arg#--phase=}" ;;
        --force)   FORCE=true ;;
        *) echo "unknown arg: $arg" >&2; exit 1 ;;
    esac
done

RG="rgHeblo"
case "$ENV_ARG" in
    prod) WEBAPP="heblo";      KV="kv-heblo-prod" ;;
    stg)  WEBAPP="heblo-test"; KV="kv-heblo-stg"  ;;
    *) echo "env must be 'stg' or 'prod' (got: $ENV_ARG)" >&2; exit 1 ;;
esac

SECRET_NAME="Plaud--TokensJson"
APP_SETTING_KEY="Plaud__TokensJson"

for cmd in az jq; do
    if ! command -v "$cmd" >/dev/null 2>&1; then
        echo "ERROR: required command '$cmd' not found in PATH" >&2
        exit 1
    fi
done

log() { echo "[$(date +%H:%M:%S)] $*"; }

run() {
    if $DRY_RUN; then
        echo "DRY: $*"
    else
        "$@"
    fi
}

# Prod confirmation gate
if [[ "$ENV_ARG" == "prod" && "$DRY_RUN" == false ]]; then
    echo "*** You are about to mutate PRODUCTION (Web App $WEBAPP, Key Vault $KV) ***"
    read -r -p "Type 'PROD' to continue: " confirm
    if [[ "$confirm" != "PROD" ]]; then
        echo "aborted"; exit 1
    fi
fi

phase_setup() {
    log "Env=$ENV_ARG  WebApp=$WEBAPP  KeyVault=$KV  ResourceGroup=$RG  DryRun=$DRY_RUN"

    # 1. Verify Key Vault exists
    if ! az keyvault show -n "$KV" -g "$RG" -o none 2>/dev/null; then
        echo "ERROR: Key Vault $KV not found in $RG. Run migrate-secrets-to-keyvault.sh first." >&2
        exit 1
    fi
    log "Key Vault $KV exists"

    # 2. Seed Plaud--TokensJson from App Settings (initial seed)
    if $DRY_RUN; then
        log "DRY: would read $APP_SETTING_KEY from $WEBAPP App Settings and seed $SECRET_NAME"
    else
        secret_exists=$(az keyvault secret show --vault-name "$KV" --name "$SECRET_NAME" \
            --query 'id' -o tsv 2>/dev/null || echo "")
        if [[ -n "$secret_exists" && "$FORCE" == false ]]; then
            log "$SECRET_NAME already exists in $KV (use --force to overwrite)"
        else
            value=$(az webapp config appsettings list -g "$RG" -n "$WEBAPP" -o json \
                | jq -r --arg k "$APP_SETTING_KEY" '.[] | select(.name==$k) | .value' 2>/dev/null || echo "")
            if [[ -z "$value" || "$value" == "null" ]]; then
                echo "ERROR: $APP_SETTING_KEY not found in $WEBAPP App Settings." >&2
                echo "       Manually paste the current tokens JSON and run again, or set it first." >&2
                exit 1
            fi
            log "Seeding $SECRET_NAME from $APP_SETTING_KEY"
            az keyvault secret set --vault-name "$KV" --name "$SECRET_NAME" --value "$value" -o none
            log "  Seeded."
        fi
    fi

    # 3. Resolve Heblo MI principal ID
    if $DRY_RUN; then
        PRINCIPAL_ID="<dry-run-principal-id>"
    else
        PRINCIPAL_ID=$(az webapp identity show -g "$RG" -n "$WEBAPP" --query principalId -o tsv 2>/dev/null || echo "")
        if [[ -z "$PRINCIPAL_ID" ]]; then
            echo "ERROR: No managed identity on $WEBAPP. Assign one first:" >&2
            echo "       az webapp identity assign -g $RG -n $WEBAPP" >&2
            exit 1
        fi
    fi
    log "MI principalId=$PRINCIPAL_ID"

    # 4. Resolve KV resource ID
    KV_ID=$(az keyvault show -n "$KV" -g "$RG" --query id -o tsv 2>/dev/null || echo "<dry-run-kv-id>")

    # 5. Scope = secret-level resource ID (per-secret RBAC, not vault-wide)
    SECRET_SCOPE="$KV_ID/secrets/$SECRET_NAME"
    log "Granting 'Key Vault Secrets Officer' to MI on scope: $SECRET_SCOPE"

    run az role assignment create \
        --assignee-object-id "$PRINCIPAL_ID" \
        --assignee-principal-type ServicePrincipal \
        --role "Key Vault Secrets Officer" \
        --scope "$SECRET_SCOPE" \
        -o none 2>/dev/null || log "  (assignment already exists or dry-run)"

    cat <<DONE

Setup complete for env=$ENV_ARG.

Next steps:
  1. Deploy the Heblo build that includes PlaudTokenRefreshJob.
  2. Restart: az webapp restart -g $RG -n $WEBAPP
  3. Enable the job via Background Jobs admin UI (DefaultIsEnabled=false).
  4. Trigger manually from Hangfire dashboard to verify end-to-end.
  5. Confirm new secret version:
     az keyvault secret show --vault-name $KV --name $SECRET_NAME \\
         --query 'attributes.{updated:updated,version:id}' -o table
  6. After 1 week stable, run cleanup:
     $0 $ENV_ARG --phase=cleanup
DONE
}

phase_cleanup() {
    log "Env=$ENV_ARG  WebApp=$WEBAPP  KeyVault=$KV  Cleanup App Setting  DryRun=$DRY_RUN"

    if ! $DRY_RUN; then
        if ! az keyvault secret show --vault-name "$KV" --name "$SECRET_NAME" -o none 2>/dev/null; then
            echo "ERROR: $SECRET_NAME not found in $KV. Run --phase=setup first." >&2
            exit 1
        fi
    fi

    if ! $DRY_RUN; then
        echo
        echo "*** This will DELETE App Setting '$APP_SETTING_KEY' from $WEBAPP ***"
        read -r -p "Type 'CLEANUP' to continue: " confirm
        if [[ "$confirm" != "CLEANUP" ]]; then
            echo "aborted"; exit 1
        fi
    fi

    has_setting=$(az webapp config appsettings list -g "$RG" -n "$WEBAPP" -o json \
        | jq -r --arg k "$APP_SETTING_KEY" '[.[] | select(.name==$k)] | length' 2>/dev/null || echo "0")

    if [[ "$has_setting" == "0" ]]; then
        log "$APP_SETTING_KEY not present in App Settings (already cleaned up or never set)"
    else
        log "Deleting $APP_SETTING_KEY from $WEBAPP App Settings"
        run az webapp config appsettings delete -g "$RG" -n "$WEBAPP" \
            --setting-names "$APP_SETTING_KEY" -o none
        log "Done."
    fi
}

case "$PHASE" in
    setup)   phase_setup ;;
    cleanup) phase_cleanup ;;
    *) echo "unknown phase: $PHASE (use setup or cleanup)" >&2; exit 1 ;;
esac
