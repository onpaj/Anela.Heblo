# App Insights Routine Infrastructure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Set up the credential infrastructure so a Claude Code cloud routine can securely query Application Insights without storing secrets in git.

**Architecture:** An App Insights read-only API key is stored in Azure Key Vault. A dedicated Service Principal (SP) with Key Vault Secrets Reader role is created. The routine prompt holds only the SP credentials (client_id, client_secret, tenant_id) — at runtime it calls Key Vault to retrieve the App Insights key, then queries the REST API. Nothing sensitive lands in the repo.

**Tech Stack:** Azure CLI (`az`), Azure Key Vault, Azure App Insights REST API, Bash, Claude Code Routines (CCR)

---

## Prerequisites (collect before starting)

You need the following values. Find them in the Azure Portal or via `az` CLI:

| Variable | How to find |
|---|---|
| `SUBSCRIPTION_ID` | `az account show --query id -o tsv` |
| `RESOURCE_GROUP` | The RG containing your App Insights resource |
| `APP_INSIGHTS_NAME` | Your Application Insights resource name |
| `APP_INSIGHTS_APP_ID` | Portal → App Insights → API Access → Application ID |

---

## File Structure

- **Create:** `scripts/query-app-insights.sh` — standalone Bash script the routine runs to authenticate and query App Insights
- **Create:** `docs/integrations/app-insights-routine.md` — documents the credential setup, SP name, Key Vault name, and how to rotate keys

---

### Task 1: Create the App Insights API Key

**Files:**
- No file changes — Azure Portal action

- [ ] **Step 1: Open App Insights API Access**

In Azure Portal: go to your App Insights resource → **API Access** (left sidebar).

- [ ] **Step 2: Create a read-only API key**

Click **Create API key**. Settings:
- Description: `claude-routine-read`
- Permissions: check **Read telemetry** only — leave all others unchecked
- Click **Generate key**

- [ ] **Step 3: Copy the key immediately**

The key is shown only once. Save it temporarily somewhere safe (you'll put it in Key Vault next).
Also note the **Application ID** from the same page.

---

### Task 2: Create Azure Key Vault

**Files:**
- No file changes — Azure CLI commands

- [ ] **Step 1: Set variables in your shell**

```bash
SUBSCRIPTION_ID="<your-subscription-id>"
RESOURCE_GROUP="<your-resource-group>"
LOCATION="westeurope"          # or wherever your other resources live
KEYVAULT_NAME="claude-routines-kv"   # globally unique, 3-24 chars, alphanumeric + hyphens
```

- [ ] **Step 2: Create the Key Vault**

```bash
az keyvault create \
  --name "$KEYVAULT_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --location "$LOCATION" \
  --sku standard \
  --enable-rbac-authorization true
```

Expected output: JSON with `"provisioningState": "Succeeded"`

- [ ] **Step 3: Verify**

```bash
az keyvault show --name "$KEYVAULT_NAME" --query "name" -o tsv
```

Expected: `claude-routines-kv`

---

### Task 3: Store App Insights Secrets in Key Vault

**Files:**
- No file changes — Azure CLI commands

- [ ] **Step 1: Grant yourself write access to Key Vault**

```bash
MY_OBJECT_ID=$(az ad signed-in-user show --query id -o tsv)

az role assignment create \
  --assignee "$MY_OBJECT_ID" \
  --role "Key Vault Secrets Officer" \
  --scope "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RESOURCE_GROUP/providers/Microsoft.KeyVault/vaults/$KEYVAULT_NAME"
```

Wait ~30 seconds for the role assignment to propagate before the next step.

- [ ] **Step 2: Store the App Insights API key**

```bash
APP_INSIGHTS_API_KEY="<the key you copied in Task 1>"

az keyvault secret set \
  --vault-name "$KEYVAULT_NAME" \
  --name "app-insights-api-key" \
  --value "$APP_INSIGHTS_API_KEY"
```

Expected: JSON with `"id": "https://claude-routines-kv.vault.azure.net/secrets/app-insights-api-key/..."`

- [ ] **Step 3: Store the App Insights Application ID**

```bash
APP_INSIGHTS_APP_ID="<your Application ID from the API Access page>"

az keyvault secret set \
  --vault-name "$KEYVAULT_NAME" \
  --name "app-insights-app-id" \
  --value "$APP_INSIGHTS_APP_ID"
```

- [ ] **Step 4: Verify both secrets exist**

```bash
az keyvault secret list --vault-name "$KEYVAULT_NAME" --query "[].name" -o tsv
```

Expected output:
```
app-insights-api-key
app-insights-app-id
```

---

### Task 4: Create Service Principal with Key Vault Reader Access

**Files:**
- No file changes — Azure CLI commands

- [ ] **Step 1: Create the Service Principal**

```bash
SP_OUTPUT=$(az ad sp create-for-rbac \
  --name "claude-routine-sp" \
  --skip-assignment \
  --output json)

echo "$SP_OUTPUT"
```

Expected JSON output — copy these four values and save them temporarily:
```json
{
  "appId": "<SP_CLIENT_ID>",
  "password": "<SP_CLIENT_SECRET>",
  "tenant": "<TENANT_ID>",
  "displayName": "claude-routine-sp"
}
```

- [ ] **Step 2: Assign Key Vault Secrets Reader role to the SP**

```bash
SP_CLIENT_ID=$(echo "$SP_OUTPUT" | jq -r '.appId')

az role assignment create \
  --assignee "$SP_CLIENT_ID" \
  --role "Key Vault Secrets User" \
  --scope "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RESOURCE_GROUP/providers/Microsoft.KeyVault/vaults/$KEYVAULT_NAME"
```

Expected: JSON with `"roleDefinitionName": "Key Vault Secrets User"`

- [ ] **Step 3: Verify the SP can read a secret (wait 60s for propagation first)**

```bash
SP_CLIENT_SECRET=$(echo "$SP_OUTPUT" | jq -r '.password')
TENANT_ID=$(echo "$SP_OUTPUT" | jq -r '.tenant')

az login --service-principal \
  --username "$SP_CLIENT_ID" \
  --password "$SP_CLIENT_SECRET" \
  --tenant "$TENANT_ID"

az keyvault secret show \
  --vault-name "$KEYVAULT_NAME" \
  --name "app-insights-api-key" \
  --query "value" -o tsv
```

Expected: the API key value printed to stdout.

- [ ] **Step 4: Log back in as yourself**

```bash
az login
```

---

### Task 5: Write the Query Script

**Files:**
- Create: `scripts/query-app-insights.sh`

- [ ] **Step 1: Write the failing test (verify script doesn't exist yet)**

```bash
ls scripts/query-app-insights.sh 2>&1
```

Expected: `No such file or directory`

- [ ] **Step 2: Create the script**

```bash
cat > scripts/query-app-insights.sh << 'EOF'
#!/usr/bin/env bash
set -euo pipefail

# Usage: SP_CLIENT_ID=... SP_CLIENT_SECRET=... SP_TENANT_ID=... KEYVAULT_NAME=... ./scripts/query-app-insights.sh "your KQL query"
# Returns raw JSON from App Insights REST API.

QUERY="${1:?Usage: $0 '<KQL query>'}"

: "${SP_CLIENT_ID:?SP_CLIENT_ID env var required}"
: "${SP_CLIENT_SECRET:?SP_CLIENT_SECRET env var required}"
: "${SP_TENANT_ID:?SP_TENANT_ID env var required}"
: "${KEYVAULT_NAME:?KEYVAULT_NAME env var required}"

# Authenticate as SP and fetch secrets from Key Vault
az login --service-principal \
  --username "$SP_CLIENT_ID" \
  --password "$SP_CLIENT_SECRET" \
  --tenant "$SP_TENANT_ID" \
  --output none

API_KEY=$(az keyvault secret show \
  --vault-name "$KEYVAULT_NAME" \
  --name "app-insights-api-key" \
  --query "value" -o tsv)

APP_ID=$(az keyvault secret show \
  --vault-name "$KEYVAULT_NAME" \
  --name "app-insights-app-id" \
  --query "value" -o tsv)

# Query App Insights REST API
curl -s \
  -H "x-api-key: $API_KEY" \
  -H "Content-Type: application/json" \
  --data "{\"query\": \"$QUERY\"}" \
  "https://api.applicationinsights.io/v1/apps/$APP_ID/query"
EOF

chmod +x scripts/query-app-insights.sh
```

- [ ] **Step 3: Test the script locally with a simple query**

Replace values with your SP credentials and Key Vault name:

```bash
SP_CLIENT_ID="<appId from Task 4>" \
SP_CLIENT_SECRET="<password from Task 4>" \
SP_TENANT_ID="<tenant from Task 4>" \
KEYVAULT_NAME="claude-routines-kv" \
./scripts/query-app-insights.sh "exceptions | top 5 by timestamp desc | project timestamp, type, outerMessage"
```

Expected: JSON with `"tables"` array containing exception rows.

- [ ] **Step 4: Commit the script**

```bash
git add scripts/query-app-insights.sh
git commit -m "feat: add App Insights query script for cloud routines"
```

---

### Task 6: Document the Setup

**Files:**
- Create: `docs/integrations/app-insights-routine.md`

- [ ] **Step 1: Create the doc**

```bash
cat > docs/integrations/app-insights-routine.md << 'EOF'
# App Insights Routine Integration

## Overview

Claude Code cloud routines query Application Insights via the REST API. Credentials are stored in Azure Key Vault; the routine authenticates using a Service Principal.

## Infrastructure

| Resource | Name |
|---|---|
| Key Vault | `claude-routines-kv` |
| Service Principal | `claude-routine-sp` |
| KV secret: API key | `app-insights-api-key` |
| KV secret: App ID | `app-insights-app-id` |

## Routine Prompt Setup

The routine prompt must export these env vars before calling `scripts/query-app-insights.sh`:

```
SP_CLIENT_ID=<appId of claude-routine-sp>
SP_CLIENT_SECRET=<password of claude-routine-sp>
SP_TENANT_ID=<tenant>
KEYVAULT_NAME=claude-routines-kv
```

These values live in the routine's prompt text in Anthropic infra — never in git.

## Rotating Keys

1. Create new App Insights API key in Azure Portal → API Access
2. `az keyvault secret set --vault-name claude-routines-kv --name app-insights-api-key --value <new-key>`
3. Delete the old key in the Portal
4. SP credentials never expire unless regenerated — rotate via `az ad sp credential reset --name claude-routine-sp`

## Query Script

`scripts/query-app-insights.sh` accepts a KQL query as its first argument and returns raw JSON from the App Insights REST API.
EOF
```

- [ ] **Step 2: Commit**

```bash
git add docs/integrations/app-insights-routine.md
git commit -m "docs: document App Insights routine credential setup"
```

---

### Task 7: Create the Claude Routine

Once the above infrastructure is in place, create the routine in Claude Code:

- [ ] **Step 1: Collect the values you'll embed in the prompt**

You need:
- `SP_CLIENT_ID` (appId from Task 4)
- `SP_CLIENT_SECRET` (password from Task 4)  
- `SP_TENANT_ID` (tenant from Task 4)
- `KEYVAULT_NAME=claude-routines-kv`

- [ ] **Step 2: Ask Claude to create the routine**

Tell Claude Code:

> "Create a routine that runs every morning at 8am Prague time. It should export `SP_CLIENT_ID`, `SP_CLIENT_SECRET`, `SP_TENANT_ID`, and `KEYVAULT_NAME` as env vars, then run `scripts/query-app-insights.sh` with a KQL query for [describe what you want to monitor — e.g., exceptions in the last 24h, slow dependencies, 5xx errors]. If results exceed a threshold, commit a report to `reports/app-insights/YYYY-MM-DD.md`."

Claude will use `RemoteTrigger` to create it with those credentials embedded in the prompt.

---

## Self-Review Notes

- All Azure CLI commands include expected output so you can verify each step
- The script uses `set -euo pipefail` — it will fail loudly rather than silently with wrong credentials
- Secrets never touch the repo — only the Key Vault name (not sensitive) is in source
- Key rotation procedure documented in Task 6 so it doesn't become tribal knowledge
- The routine itself (Task 7) is intentionally left open — the KQL query depends on what you want to monitor, which you haven't specified yet
