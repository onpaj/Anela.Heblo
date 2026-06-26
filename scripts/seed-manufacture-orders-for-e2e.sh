#!/usr/bin/env bash
set -euo pipefail

# Seed staging with the manufacture orders required by E2E protocol tests.
#
# Creates:
#   - One Draft manufacture order
#   - One Completed manufacture order (Draft → Planned → Completed)
#
# Both appear at the top of the list (CreatedDate DESC sort), satisfying
# the protocol.spec.ts requirement to find them in the first 5 rows.
#
# Required env vars:
#   E2E_CLIENT_ID      — Azure AD service principal client ID
#   E2E_CLIENT_SECRET  — Azure AD service principal secret
#   AZURE_TENANT_ID    — Azure AD tenant ID
#
# Optional env vars:
#   PLAYWRIGHT_BASE_URL — Override staging URL (default: https://heblo.stg.anela.cz)

BASE_URL="${PLAYWRIGHT_BASE_URL:-https://heblo.stg.anela.cz}"
SCOPE="api://8b34be89-f86f-422f-af40-7dbcd30cb66a/.default"
TODAY="$(date -u +%Y-%m-%d)"
PLANNED_DATE="2027-01-01"

# --- pre-flight checks ---

missing_vars=()
[[ -z "${E2E_CLIENT_ID:-}" ]] && missing_vars+=("E2E_CLIENT_ID")
[[ -z "${E2E_CLIENT_SECRET:-}" ]] && missing_vars+=("E2E_CLIENT_SECRET")
[[ -z "${AZURE_TENANT_ID:-}" ]] && missing_vars+=("AZURE_TENANT_ID")

if [[ ${#missing_vars[@]} -gt 0 ]]; then
  echo "ERROR: Missing required environment variables: ${missing_vars[*]}" >&2
  echo "Set E2E_CLIENT_ID, E2E_CLIENT_SECRET, and AZURE_TENANT_ID before running." >&2
  exit 1
fi

if ! command -v jq &>/dev/null; then
  echo "ERROR: 'jq' is required but not installed. Install it first (e.g. sudo apt-get install jq)." >&2
  exit 1
fi

if ! command -v curl &>/dev/null; then
  echo "ERROR: 'curl' is required but not installed." >&2
  exit 1
fi

echo "Seeding staging manufacture orders for E2E protocol tests..."
echo "  Target: ${BASE_URL}"

# --- acquire Azure AD token ---

echo ""
echo "Acquiring Azure AD token..."

token_response=$(curl -s --fail-with-body -X POST \
  "https://login.microsoftonline.com/${AZURE_TENANT_ID}/oauth2/v2.0/token" \
  -d "grant_type=client_credentials&client_id=${E2E_CLIENT_ID}&client_secret=${E2E_CLIENT_SECRET}&scope=${SCOPE}")

TOKEN=$(echo "${token_response}" | jq -r '.access_token // empty')
if [[ -z "${TOKEN}" ]]; then
  echo "ERROR: Failed to obtain Azure AD token. Response:" >&2
  echo "${token_response}" >&2
  exit 1
fi

echo "  Token acquired."

# --- helpers ---

api_post() {
  local path="$1"
  local body="$2"
  curl -s --fail-with-body -X POST \
    "${BASE_URL}${path}" \
    -H "Authorization: Bearer ${TOKEN}" \
    -H "Content-Type: application/json" \
    -d "${body}"
}

api_patch() {
  local path="$1"
  local body="$2"
  curl -s --fail-with-body -X PATCH \
    "${BASE_URL}${path}" \
    -H "Authorization: Bearer ${TOKEN}" \
    -H "Content-Type: application/json" \
    -d "${body}"
}

create_order() {
  local label="$1"
  local body
  body=$(printf '{"productCode":"MAS001001M","productName":"Hedvábný pan Jasmín E2E %s","originalBatchSize":5000,"newBatchSize":5000,"scaleFactor":1.0,"plannedDate":"%s","manufactureType":1}' "${label}" "${PLANNED_DATE}")

  local response
  response=$(api_post "/api/ManufactureOrder" "${body}")
  local order_id
  order_id=$(echo "${response}" | jq -r '.id // empty')

  if [[ -z "${order_id}" || "${order_id}" == "null" ]]; then
    echo "ERROR: Failed to create ${label} order. Response:" >&2
    echo "${response}" >&2
    exit 1
  fi

  echo "${order_id}"
}

patch_status() {
  local order_id="$1"
  local new_state="$2"
  local label="$3"
  local body
  body=$(printf '{"id":%s,"newState":%s}' "${order_id}" "${new_state}")

  local response
  response=$(api_patch "/api/ManufactureOrder/${order_id}/status" "${body}")
  local success
  success=$(echo "${response}" | jq -r '.success // "false"')

  if [[ "${success}" != "true" ]]; then
    echo "ERROR: Failed to patch order ${order_id} to ${label}. Response:" >&2
    echo "${response}" >&2
    exit 1
  fi
}

# --- create Draft order ---

echo ""
echo "Creating Draft manufacture order..."
DRAFT_ID=$(create_order "Draft")
echo "  Created Draft order ID: ${DRAFT_ID}"

# --- create Completed order ---

echo ""
echo "Creating Completed manufacture order (Draft → Planned → Completed)..."
COMP_ID=$(create_order "Completed")
echo "  Created order ID: ${COMP_ID} (currently Draft)"

echo "  Transitioning to Planned (state=2)..."
patch_status "${COMP_ID}" "2" "Planned"

echo "  Transitioning to Completed (state=5)..."
patch_status "${COMP_ID}" "5" "Completed"

echo "  Order ${COMP_ID} is now Completed."

# --- done ---

echo ""
echo "Done. Staging now has:"
echo "  Draft order:     ID ${DRAFT_ID}"
echo "  Completed order: ID ${COMP_ID}"
echo ""
echo "Both will appear in the first rows of the manufacture orders list (sorted CreatedDate DESC)."
echo "The E2E protocol tests should now pass against staging."
