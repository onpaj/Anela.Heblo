#!/usr/bin/env bash
set -euo pipefail

MANIFEST="${MANIFEST:-access-matrix.generated.json}"
APP_ID="${APP_ID:?set APP_ID (application/client id)}"
SP_OBJECT_ID="${SP_OBJECT_ID:?set SP_OBJECT_ID (enterprise app object id)}"
MODE="${1:---dry-run}"

echo "Mode: $MODE  Manifest: $MANIFEST"

# 1. App roles — full-replace (stable ids preserve existing assignments).
if [[ "$MODE" == "--apply" ]]; then
  jq '.appRoles' "$MANIFEST" > /tmp/approles.json
  az ad app update --id "$APP_ID" --app-roles @/tmp/approles.json
  echo "App roles updated."
else
  echo "[dry-run] would set $(jq '.appRoles | length' "$MANIFEST") app roles"
fi

# 2. Groups — create if missing.
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

# 3. Group → app-role assignments — diff and reconcile.
for name in $(jq -r '.groups[].name' "$MANIFEST"); do
  gid=$(az ad group list --filter "displayName eq '$name'" --query "[0].id" -o tsv)
  [[ -z "$gid" ]] && { echo "skip $name (no group yet)"; continue; }
  desired=$(jq -r --arg n "$name" '.groups[] | select(.name==$n) | .roles[]' "$MANIFEST")
  for role in $desired; do
    role_id=$(jq -r --arg v "$role" '.appRoles[] | select(.value==$v) | .id' "$MANIFEST")
    exists=$(az rest --method GET \
      --uri "https://graph.microsoft.com/v1.0/groups/$gid/appRoleAssignments" \
      --query "value[?appRoleId=='$role_id'] | [0].id" -o tsv 2>/dev/null || true)
    if [[ -z "$exists" ]]; then
      if [[ "$MODE" == "--apply" ]]; then
        az rest --method POST \
          --uri "https://graph.microsoft.com/v1.0/groups/$gid/appRoleAssignments" \
          --headers "Content-Type=application/json" \
          --body "{\"principalId\":\"$gid\",\"resourceId\":\"$SP_OBJECT_ID\",\"appRoleId\":\"$role_id\"}"
        echo "Assigned $role to $name"
      else
        echo "[dry-run] would assign $role to $name"
      fi
    fi
  done
done
echo "Done."
