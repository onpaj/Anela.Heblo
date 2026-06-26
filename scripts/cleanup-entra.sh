#!/usr/bin/env bash
# One-time cleanup: removes Entra groups and app roles provisioned by the access matrix.
#
# Groups removed:
#   - All groups defined in the manifest (.groups[].name)
#   - All groups whose displayName starts with "SG_"
#
# App roles removed (from every app in APP_IDS):
#   - All roles defined in the manifest, EXCEPT "super_user" and "heblo_user"
#
# Usage:
#   APP_IDS="<blazor-app-id> <service-app-id>" ./scripts/cleanup-entra.sh             # dry-run
#   APP_IDS="<blazor-app-id> <service-app-id>" ./scripts/cleanup-entra.sh --apply     # live run
#
# Prerequisites: az CLI logged in with sufficient privileges (Application Administrator + Group Administrator).
set -euo pipefail

MANIFEST="${MANIFEST:-access-matrix-entra.generated.json}"
APP_IDS="${APP_IDS:?set APP_IDS as space-separated app/client ids (Heblo_Blazor Heblo_Service)}"
MODE="${1:---dry-run}"

echo "Mode: $MODE  Manifest: $MANIFEST"
echo ""

read -ra app_id_arr <<< "$APP_IDS"

# Roles that must never be removed from any app registration.
KEEP_ROLES='["super_user","heblo_user"]'

# ---------------------------------------------------------------------------
# Step 1 — Delete groups defined in the manifest
# ---------------------------------------------------------------------------
echo "=== Step 1: Deleting manifest groups ==="
for name in $(jq -r '.groups[].name' "$MANIFEST"); do
  gid=$(az ad group list --filter "displayName eq '$name'" --query "[0].id" -o tsv 2>/dev/null || true)
  if [[ -z "$gid" ]]; then
    echo "  Not found (skip): $name"
  elif [[ "$MODE" == "--apply" ]]; then
    az ad group delete --group "$gid"
    echo "  Deleted: $name ($gid)"
  else
    echo "  [dry-run] would delete: $name ($gid)"
  fi
done

# ---------------------------------------------------------------------------
# Step 2 — Delete all groups with "SG_" prefix
# ---------------------------------------------------------------------------
echo ""
echo "=== Step 2: Deleting SG_ groups ==="
# Fetches all groups and filters client-side; avoids OData startswith quirks.
sg_groups=$(az ad group list --query "[?starts_with(displayName,'SG_')].{id:id,name:displayName}" -o json 2>/dev/null || echo "[]")

sg_count=$(echo "$sg_groups" | jq 'length')
if [[ "$sg_count" -eq 0 ]]; then
  echo "  No SG_ groups found."
else
  while IFS=$'\t' read -r gid name; do
    if [[ "$MODE" == "--apply" ]]; then
      az ad group delete --group "$gid"
      echo "  Deleted: $name ($gid)"
    else
      echo "  [dry-run] would delete: $name ($gid)"
    fi
  done < <(echo "$sg_groups" | jq -r '.[] | [.id, .name] | @tsv')
fi

# ---------------------------------------------------------------------------
# Step 3 — Remove app roles from each app registration (keep KEEP_ROLES)
# ---------------------------------------------------------------------------
echo ""
echo "=== Step 3: Removing app roles from app registrations ==="

# Collect role values from the manifest that are eligible for removal.
manifest_removable=$(jq \
  --argjson keep "$KEEP_ROLES" \
  '[.appRoles[] | select(.value | IN($keep[]) | not) | .value]' \
  "$MANIFEST")
removable_count=$(echo "$manifest_removable" | jq 'length')

echo "  Roles to remove: $removable_count"
echo "  Roles kept:      $(echo "$KEEP_ROLES" | jq -r '.[]' | tr '\n' ' ')"
echo ""

for app_id in "${app_id_arr[@]}"; do
  echo "  --- App: $app_id ---"

  current_roles=$(az ad app show --id "$app_id" --query "appRoles" -o json)
  total=$(echo "$current_roles" | jq 'length')
  echo "  Current roles in Entra: $total"

  # Find which of the removable roles actually exist on this app.
  present_removable=$(echo "$current_roles" | jq \
    --argjson remove "$manifest_removable" \
    '[.[] | select(.value | IN($remove[]))] | [.[].value]')
  present_count=$(echo "$present_removable" | jq 'length')
  echo "  Roles present that will be removed: $present_count"

  if [[ "$present_count" -eq 0 ]]; then
    echo "  Nothing to remove for $app_id."
    continue
  fi

  if [[ "$MODE" == "--apply" ]]; then
    # Entra enforces a two-step removal: disable first, then delete.

    # 3a. Disable targeted roles.
    disabled=$(echo "$current_roles" | jq \
      --argjson remove "$manifest_removable" \
      '[.[] | if (.value | IN($remove[])) then .isEnabled = false else . end]')
    echo "$disabled" > /tmp/approles_disable.json
    az ad app update --id "$app_id" --app-roles @/tmp/approles_disable.json
    echo "  Disabled $present_count roles on $app_id."

    # 3b. Remove disabled roles (keep everything not in the remove list).
    kept=$(echo "$current_roles" | jq \
      --argjson remove "$manifest_removable" \
      '[.[] | select(.value | IN($remove[]) | not)]')
    echo "$kept" > /tmp/approles_kept.json
    az ad app update --id "$app_id" --app-roles @/tmp/approles_kept.json
    echo "  Removed $present_count roles from $app_id."
  else
    echo "$present_removable" | jq -r '.[]' | while read -r role; do
      echo "  [dry-run] would remove role: $role"
    done
  fi
done

echo ""
echo "Done."
