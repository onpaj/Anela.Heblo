#!/usr/bin/env bash
# Migrate Anela.Heblo secrets from Azure Web App App Settings into Azure Key Vault.
#
# Topology:
#   stg  -> Web App "heblo-test" + Key Vault "kv-heblo-stg"
#   prod -> Web App "heblo"      + Key Vault "kv-heblo-prod"
#
# Usage:
#   ./migrate-secrets-to-keyvault.sh <stg|prod> [--dry-run] [--phase=migrate|cleanup]
#
# Prerequisites:
#   - az CLI installed and `az login` completed
#   - `az account set --subscription <id>` if you have multiple
#   - Owner or User Access Admin on resource group rgHeblo (to grant RBAC)
#   - `jq` installed (parses Web App settings JSON)
#
# Phases:
#   migrate (default): create KV if missing, enable managed identity on Web App,
#                      grant RBAC, copy values from App Settings to KV, set KeyVault__Uri.
#                      Leaves old App Settings in place so old build still works.
#   cleanup:           delete the migrated App Settings entries. Run only after the
#                      new build (with AddAzureKeyVault wired up) is deployed and
#                      verified.
#
# Idempotent — safe to re-run after partial failure.

set -euo pipefail

ENV_ARG="${1:-}"
if [[ -z "$ENV_ARG" || "$ENV_ARG" == "-h" || "$ENV_ARG" == "--help" ]]; then
    cat <<USAGE
Usage: $0 <stg|prod> [--dry-run] [--phase=migrate|cleanup]
USAGE
    exit 1
fi

DRY_RUN=false
PHASE=migrate
for arg in "${@:2}"; do
    case "$arg" in
        --dry-run) DRY_RUN=true ;;
        --phase=*) PHASE="${arg#--phase=}" ;;
        *) echo "unknown arg: $arg" >&2; exit 1 ;;
    esac
done

RG="rgHeblo"
case "$ENV_ARG" in
    prod) WEBAPP="heblo";      KV="kv-heblo-prod" ;;
    stg)  WEBAPP="heblo-test"; KV="kv-heblo-stg"  ;;
    *) echo "env must be 'stg' or 'prod' (got: $ENV_ARG)" >&2; exit 1 ;;
esac

# --- Required CLIs ---
for cmd in az jq; do
    if ! command -v "$cmd" >/dev/null 2>&1; then
        echo "ERROR: required command '$cmd' not found in PATH" >&2
        exit 1
    fi
done

# --- Keys to migrate from existing App Settings.
# Config-key format is the App Service form (':' replaced with '__').
# KV secret name conversion: '__' -> '--'  (e.g. ConnectionStrings__Default -> ConnectionStrings--Default)
APP_SETTING_KEYS=(
    "ConnectionStrings__Default"
    "AzureAd__ClientSecret"
    "AzureAd__ClientId"
    "ApplicationInsights__ConnectionString"
    "SendGrid__ApiKey"
    "HomeAssistant__BaseUrl"
    "HomeAssistant__AccessToken"
    "GoogleAds__DeveloperToken"
    "GoogleAds__OAuth2ClientId"
    "GoogleAds__OAuth2ClientSecret"
    "GoogleAds__OAuth2RefreshToken"
    "MetaAds__AccessToken"
    "Smartsupp__ApiToken"
    "Smartsupp__WebhookSecret"
    "Anthropic__ApiKey"
    "OpenAI__ApiKey"
    "WebSearch__ApiKey"
    "Cups__Username"
    "Cups__Password"
    "Comgate__MerchantId"
    "Comgate__Secret"
    "Shoptet__Token"
    "ShoptetPay__ApiToken"
    "FlexiBeeSettings__Login"
    "FlexiBeeSettings__Password"
    "FlexiBeeSettings__Company"
    "StockClient__Url"
    "ProductPriceOptions__ProductExportUrl"
    "ExpeditionList__BlobConnectionString"
)

log() { echo "[$(date +%H:%M:%S)] $*"; }

run() {
    if $DRY_RUN; then
        echo "DRY: $*"
    else
        "$@"
    fi
}

# Confirmation gate for prod
if [[ "$ENV_ARG" == "prod" && "$DRY_RUN" == false ]]; then
    echo "*** You are about to mutate PRODUCTION (Web App $WEBAPP, Key Vault $KV) ***"
    read -r -p "Type 'PROD' to continue: " confirm
    if [[ "$confirm" != "PROD" ]]; then
        echo "aborted"; exit 1
    fi
fi

phase_migrate() {
    log "Env=$ENV_ARG  WebApp=$WEBAPP  KeyVault=$KV  ResourceGroup=$RG  DryRun=$DRY_RUN"

    log "Ensuring Key Vault $KV exists"
    if ! az keyvault show -n "$KV" -g "$RG" -o none 2>/dev/null; then
        run az keyvault create -n "$KV" -g "$RG" \
            --enable-rbac-authorization true \
            --enable-purge-protection true \
            --retention-days 90 \
            -o none
    else
        log "  (already exists)"
    fi

    log "Ensuring system-assigned managed identity on $WEBAPP"
    if $DRY_RUN; then
        echo "DRY: az webapp identity assign -g $RG -n $WEBAPP"
        PRINCIPAL_ID="<dry-run-principal-id>"
    else
        PRINCIPAL_ID=$(az webapp identity assign -g "$RG" -n "$WEBAPP" --query principalId -o tsv)
    fi
    log "  principalId=$PRINCIPAL_ID"

    KV_ID=$(az keyvault show -n "$KV" -g "$RG" --query id -o tsv 2>/dev/null || echo "<dry-run-kv-id>")

    log "Granting 'Key Vault Secrets User' on $KV to Web App identity"
    run az role assignment create \
        --assignee-object-id "$PRINCIPAL_ID" \
        --assignee-principal-type ServicePrincipal \
        --role "Key Vault Secrets User" \
        --scope "$KV_ID" \
        -o none 2>/dev/null || log "  (assignment already exists or dry-run)"

    log "Granting 'Key Vault Secrets Officer' on $KV to current user (needed to write secrets)"
    ME=$(az ad signed-in-user show --query id -o tsv 2>/dev/null || echo "<dry-run-user-id>")
    run az role assignment create \
        --assignee-object-id "$ME" \
        --assignee-principal-type User \
        --role "Key Vault Secrets Officer" \
        --scope "$KV_ID" \
        -o none 2>/dev/null || log "  (assignment already exists or dry-run)"

    if ! $DRY_RUN; then
        log "Waiting 30s for RBAC propagation..."
        sleep 30
    fi

    log "Reading current App Settings from $WEBAPP"
    SETTINGS_JSON=$(az webapp config appsettings list -g "$RG" -n "$WEBAPP" -o json)

    log "Migrating App Setting keys -> Key Vault"
    for app_key in "${APP_SETTING_KEYS[@]}"; do
        value=$(echo "$SETTINGS_JSON" | jq -r --arg k "$app_key" '.[] | select(.name==$k) | .value' 2>/dev/null || echo "")
        if [[ -z "$value" || "$value" == "null" ]]; then
            log "  SKIP $app_key (not present in App Settings)"
            continue
        fi
        kv_name="${app_key//__/--}"
        log "  -> $kv_name"
        run az keyvault secret set --vault-name "$KV" --name "$kv_name" --value "$value" -o none
    done

    log "Setting KeyVault__Uri on $WEBAPP"
    if $DRY_RUN; then
        KV_URI="https://${KV}.vault.azure.net/"
    else
        KV_URI=$(az keyvault show -n "$KV" -g "$RG" --query properties.vaultUri -o tsv)
    fi
    run az webapp config appsettings set -g "$RG" -n "$WEBAPP" \
        --settings "KeyVault__Uri=$KV_URI" -o none

    cat <<DONE

Migrate phase complete for env=$ENV_ARG.

Next steps:
  1. Deploy the build that includes the AddAzureKeyVault wiring in Program.cs.
  2. Restart Web App: az webapp restart -g $RG -n $WEBAPP
  3. Verify endpoints (/health/ready, an authed endpoint, a DB-touching action).
  4. When confident, run cleanup: $0 $ENV_ARG --phase=cleanup
DONE
}

phase_cleanup() {
    log "Env=$ENV_ARG  WebApp=$WEBAPP  KeyVault=$KV  Cleanup migrated App Settings  DryRun=$DRY_RUN"

    # Safety gate 1: KV must exist.
    if ! $DRY_RUN; then
        if ! az keyvault show -n "$KV" -g "$RG" -o none 2>/dev/null; then
            echo "ERROR: Key Vault $KV does not exist. Run --phase=migrate first." >&2
            exit 1
        fi
    fi

    # Safety gate 2: Web App must already be pointing at the KV.
    if ! $DRY_RUN; then
        KV_URI_SET=$(az webapp config appsettings list -g "$RG" -n "$WEBAPP" -o json \
            | jq -r '.[] | select(.name=="KeyVault__Uri") | .value')
        if [[ -z "$KV_URI_SET" || "$KV_URI_SET" == "null" ]]; then
            echo "ERROR: KeyVault__Uri is not set on $WEBAPP. Run --phase=migrate first and deploy the new build before cleanup." >&2
            exit 1
        fi
        log "  KeyVault__Uri on $WEBAPP = $KV_URI_SET"
    fi

    # Safety gate 3: explicit confirmation (extra layer beyond the prod gate at the top).
    if ! $DRY_RUN; then
        echo
        echo "*** This will DELETE the following App Settings from $WEBAPP (only if their KV counterpart exists): ***"
        for app_key in "${APP_SETTING_KEYS[@]}"; do echo "    $app_key"; done
        read -r -p "Type 'CLEANUP' to continue: " confirm
        if [[ "$confirm" != "CLEANUP" ]]; then
            echo "aborted"; exit 1
        fi
    fi

    log "Deleting App Settings keys, but only if the corresponding KV secret exists"
    SETTINGS_JSON=$(az webapp config appsettings list -g "$RG" -n "$WEBAPP" -o json)
    for app_key in "${APP_SETTING_KEYS[@]}"; do
        kv_name="${app_key//__/--}"

        # Skip if not currently in App Settings (nothing to delete).
        has_setting=$(echo "$SETTINGS_JSON" | jq -r --arg k "$app_key" '[.[] | select(.name==$k)] | length')
        if [[ "$has_setting" == "0" ]]; then
            log "  SKIP $app_key (not present in App Settings)"
            continue
        fi

        # Refuse to delete unless the secret is in KV.
        if ! az keyvault secret show --vault-name "$KV" --name "$kv_name" -o none 2>/dev/null; then
            log "  REFUSE $app_key (secret $kv_name not found in $KV) — run --phase=migrate first"
            continue
        fi

        log "  DELETE $app_key (KV has $kv_name)"
        run az webapp config appsettings delete -g "$RG" -n "$WEBAPP" \
            --setting-names "$app_key" -o none
    done

    log "Cleanup phase complete. KeyVault__Uri, ASPNETCORE_ENVIRONMENT, WEBSITES_PORT and other non-secret settings are preserved."
}

case "$PHASE" in
    migrate) phase_migrate ;;
    cleanup) phase_cleanup ;;
    *) echo "unknown phase: $PHASE (use migrate or cleanup)" >&2; exit 1 ;;
esac
