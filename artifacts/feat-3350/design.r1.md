# Design: seed-manufacture-orders-for-e2e.sh

## Script structure

```
scripts/seed-manufacture-orders-for-e2e.sh
```

### Step 1 — Acquire Azure AD token

```bash
TOKEN=$(curl -s -X POST \
  "https://login.microsoftonline.com/${AZURE_TENANT_ID}/oauth2/v2.0/token" \
  -d "grant_type=client_credentials&client_id=${E2E_CLIENT_ID}&client_secret=${E2E_CLIENT_SECRET}&scope=api://8b34be89-f86f-422f-af40-7dbcd30cb66a/.default" \
  | jq -r '.access_token')
```

### Step 2 — Create Draft order (will stay as Draft)

```bash
DRAFT_ID=$(curl -s -X POST "${BASE_URL}/api/ManufactureOrder" \
  -H "Authorization: Bearer ${TOKEN}" \
  -H "Content-Type: application/json" \
  -d '{"productCode":"MAS001001M","productName":"Hedvábný pan Jasmín E2E Draft","originalBatchSize":5000,"newBatchSize":5000,"scaleFactor":1,"plannedDate":"2027-01-01","manufactureType":1}' \
  | jq '.id')
```

### Step 3 — Create second order, transition to Completed

```bash
# Create (Draft)
COMP_ID=$(curl -s -X POST "${BASE_URL}/api/ManufactureOrder" \
  -H "Authorization: Bearer ${TOKEN}" \
  -H "Content-Type: application/json" \
  -d '{"productCode":"MAS001001M","productName":"Hedvábný pan Jasmín E2E Completed","originalBatchSize":5000,"newBatchSize":5000,"scaleFactor":1,"plannedDate":"2027-01-01","manufactureType":1}' \
  | jq '.id')

# Draft → Planned
curl -s -X PATCH "${BASE_URL}/api/ManufactureOrder/${COMP_ID}/status" \
  -H "Authorization: Bearer ${TOKEN}" \
  -H "Content-Type: application/json" \
  -d "{\"id\":${COMP_ID},\"newState\":2}"

# Planned → Completed
curl -s -X PATCH "${BASE_URL}/api/ManufactureOrder/${COMP_ID}/status" \
  -H "Authorization: Bearer ${TOKEN}" \
  -H "Content-Type: application/json" \
  -d "{\"id\":${COMP_ID},\"newState\":5}"
```

## Environment variables required

| Variable | Source | Description |
|---|---|---|
| `E2E_CLIENT_ID` | Azure AD | Service principal client ID |
| `E2E_CLIENT_SECRET` | Azure AD | Service principal secret |
| `AZURE_TENANT_ID` | Azure AD | Tenant ID |
| `PLAYWRIGHT_BASE_URL` | Optional | Override staging URL (default: `https://heblo.stg.anela.cz`) |

## Error handling

- Check that all required env vars are set; exit early with a clear message if any is missing
- Check that the Azure AD token was successfully obtained (non-null/non-error response)
- Check that each `curl` call returns a non-null `id` in the response
- Print the created order numbers on success
