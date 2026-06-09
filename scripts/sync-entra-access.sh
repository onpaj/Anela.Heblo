#!/usr/bin/env bash
set -euo pipefail

MANIFEST="${MANIFEST:-access-matrix-entra.generated.json}"
# Space-separated parallel lists — APP_IDS[i] pairs with SP_OBJECT_IDS[i].
APP_IDS="${APP_IDS:?set APP_IDS (space-separated app/client ids)}"
SP_OBJECT_IDS="${SP_OBJECT_IDS:?set SP_OBJECT_IDS (space-separated enterprise app object ids, same order)}"
MODE="${1:---dry-run}"

echo "Mode: $MODE  Manifest: $MANIFEST"

read -ra app_id_arr <<< "$APP_IDS"
read -ra sp_id_arr <<< "$SP_OBJECT_IDS"

# 1. App roles — full-replace on each app registration.
#    Entra requires a two-step: disable existing roles first, then apply new ones.
for app_id in "${app_id_arr[@]}"; do
  if [[ "$MODE" == "--apply" ]]; then
    echo "Disabling existing app roles on $app_id..."
    az ad app show --id "$app_id" --query "appRoles" -o json \
      | jq '[.[] | .isEnabled = false]' > /tmp/approles_disabled.json
    az ad app update --id "$app_id" --app-roles @/tmp/approles_disabled.json
    echo "Applying new app roles on $app_id..."
    jq '.appRoles' "$MANIFEST" > /tmp/approles.json
    az ad app update --id "$app_id" --app-roles @/tmp/approles.json
    echo "App roles updated on $app_id."
  else
    echo "[dry-run] would set $(jq '.appRoles | length' "$MANIFEST") app roles on $app_id"
  fi
done

# 2. Groups — create if missing (tenant-scoped, only needed once).
for name in $(jq -r '.groups[].name' "$MANIFEST"); do
  gid=$(az ad group list --filter "displayName eq '$name'" --query "[0].id" -o tsv)
  if [[ -z "$gid" ]]; then
    if [[ "$MODE" == "--apply" ]]; then
      gid=$(az ad group create --display-name "$name" --mail-nickname "$name" --query id -o tsv)
      echo "Created group $name ($gid)"
    else
      echo "[dry-run] would create group $name"
    fi
  fi
done

# 3. Group → app-role assignments — diff and reconcile per service principal.
for i in "${!app_id_arr[@]}"; do
  sp_id="${sp_id_arr[$i]}"
  echo "--- Reconciling assignments for SP $sp_id ---"
  for name in $(jq -r '.groups[].name' "$MANIFEST"); do
    gid=$(az ad group list --filter "displayName eq '$name'" --query "[0].id" -o tsv)
    [[ -z "$gid" ]] && { echo "skip $name (no group yet)"; continue; }
    desired=$(jq -r --arg n "$name" '.groups[] | select(.name==$n) | .roles[]' "$MANIFEST")
    for role in $desired; do
      role_id=$(jq -r --arg v "$role" '.appRoles[] | select(.value==$v) | .id' "$MANIFEST")
      exists=$(az rest --method GET \
        --uri "https://graph.microsoft.com/v1.0/groups/$gid/appRoleAssignments" \
        --query "value[?appRoleId=='$role_id' && resourceId=='$sp_id'] | [0].id" -o tsv 2>/dev/null || true)
      if [[ -z "$exists" ]]; then
        if [[ "$MODE" == "--apply" ]]; then
          out=$(az rest --method POST \
            --uri "https://graph.microsoft.com/v1.0/groups/$gid/appRoleAssignments" \
            --headers "Content-Type=application/json" \
            --body "{\"principalId\":\"$gid\",\"resourceId\":\"$sp_id\",\"appRoleId\":\"$role_id\"}" 2>&1) || {
            if echo "$out" | grep -q "already exists"; then
              echo "Already assigned $role to $name (SP $sp_id)"
            else
              echo "$out" >&2; exit 1
            fi
            continue
          }
          echo "Assigned $role to $name (SP $sp_id)"
        else
          echo "[dry-run] would assign $role to $name (SP $sp_id)"
        fi
      fi
    done
  done
done
echo "Done."
