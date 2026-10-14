# Marketing agents — identity, approval channel, rollout

Operational runbook for PR C4 of the marketing agents platform
(spec: `docs/superpowers/specs/2026-10-07-marketing-agents-platform-design.md`, section 7).
Every step here is performed by **Ondrej**, staging first, production after review.

## 1. Who may do what

| Caller | Token | Read ad data / propose | Approve / reject |
|---|---|---|---|
| Agent (`heblo-marketing-agent`) | client credentials, app-only (`roles: Marketing.Agent`, no `scp`) | yes, if its AppUser is in `Marketing_Agent` | **never** — refused even with `super_user` |
| Person in the web app | SPA token (`azp` ∈ `Ads:Approval:WebClientIds`) or cookie session | per groups | channel **Web**, needs `marketing.ad_approvals.write` |
| Person in Claude (connector to `/mcp`) | delegated token, `azp` = API client id | per groups | channel **MCP**, needs `marketing.ad_approvals.write` **and** `marketing.ad_approvals_via_mcp.read`; stricter limits |
| Person via HebloMCP | delegated token, `azp` = HebloMCP client id (must be in `McpClientIds`) | per groups | channel **MCP** as above |
| Any other bearer client | — | per groups | refused |

Enforcement: `ApprovalChannelResolver` (Application, `Features/MarketingAds/Proposals/Approval/`), called by every
approve/reject handler, so REST and MCP behave the same.

## 2. Configuration

| Key | Default (repo) | Meaning |
|---|---|---|
| `Ads:Approval:WebClientIds` | `["87193df0-3128-44d2-8673-580e97631a07"]` | `azp`/`appid` of the web SPA |
| `Ads:Approval:McpClientIds` | `[]` (+ `AzureAd:ClientId`, always added in code) | extra MCP clients, e.g. HebloMCP |
| `Ads:AgentRateLimitPerMinute` | `60` | `/mcp` requests per minute per agent `oid`; HTTP 429 above it. People are not limited |

Client ids are not secrets, but all environment overrides go through Key Vault like every other
setting (`--` separator, arrays by index):

```bash
az keyvault secret set --vault-name kv-heblo-stg --name "Ads--Approval--McpClientIds--0" --value "<heblomcp-client-id>"
```

Restart the web app after a Key Vault change (`az webapp restart --name heblo-test --resource-group rgHeblo`;
production: `heblo`). The config binder merges arrays by index; the resolver de-duplicates and
compares case-insensitively, so an override with fewer entries is safe.

**Verify the SPA client id per environment** (a wrong value blocks every web approval): sign in to
the web app, copy the access token of any `/api` call from the browser dev tools (Network →
request → `Authorization` header) and decode it:

```bash
python3 -c "import base64,json,sys;p=sys.argv[1].split('.')[1];p+='='*(-len(p)%4);print(json.loads(base64.urlsafe_b64decode(p)).get('azp'))" "<token>"
```

The printed value must be in `Ads:Approval:WebClientIds`; otherwise set
`Ads--Approval--WebClientIds--0` in that environment's Key Vault.

## 3. Rollout grant step (reviewed, staging first)

`JsonGroupSeeder` only inserts **missing** groups, so a deployment alone grants nothing to
existing groups. Never use `--reset-group` here — it wipes hand-made grants.

1. Deploy to staging.
2. Create the three new groups (non-destructive, insert-if-missing):
   ```bash
   scripts/seed-authorization.sh staging
   ```
   Expected: `Marketing_Agent`, `Marketing_Specialist`, `Marketing_Chief` created; every other
   group reported as existing/untouched.
3. In the web app, `/admin/access` → Groups:
   - `Marketer`: add `marketing.ads.read`, `marketing.ads.write`.
   - `Spravce`: add `marketing.ads.read`, `marketing.ads.write`, `marketing.ad_approvals.write`,
     `marketing.ad_approvals.admin`, `marketing.ad_approvals_via_mcp.read`.
   - Nobody gets `marketing.ad_approvals.read` (the approvals feature has no read level in use).
4. Put people into `Marketing_Specialist` / `Marketing_Chief` as agreed with marketing (these
   groups hold only the ads permissions; people keep their `Marketer` membership for the rest).
5. Verify (read-only, against `Heblo_TST`; production DB is `Heblo_V3`):
   ```sql
   SELECT g."Name", p."PermissionValue"
   FROM public."PermissionGroups" g
   JOIN public."GroupPermissions" p ON p."GroupId" = g."Id"
   WHERE p."PermissionValue" LIKE 'marketing.ad%'
   ORDER BY 1, 2;
   ```
   Expected: `Marketing_Agent` 2 rows, `Marketer` 2, `Marketing_Specialist` 3, `Marketing_Chief` 4,
   `Spravce` 5 — and no other group.
6. After review, repeat 1–5 for production (`scripts/seed-authorization.sh production`).

SQL alternative to step 3 (idempotent; only if the admin UI is unavailable — paste it into the PR
for review before running):

```sql
INSERT INTO public."GroupPermissions" ("GroupId", "PermissionValue")
SELECT g."Id", v.p
FROM public."PermissionGroups" g
JOIN (VALUES
  ('Marketer', 'marketing.ads.read'), ('Marketer', 'marketing.ads.write'),
  ('Spravce', 'marketing.ads.read'), ('Spravce', 'marketing.ads.write'),
  ('Spravce', 'marketing.ad_approvals.write'),
  ('Spravce', 'marketing.ad_approvals.admin'), ('Spravce', 'marketing.ad_approvals_via_mcp.read')
) AS v(g, p) ON v.g = g."Name"
ON CONFLICT DO NOTHING;
```

SQL grants bypass the permission cache invalidation: they take effect within 5 minutes.

## 4. Agent service identity (Entra)

Staging and production use the same tenant and the same Heblo API app registration
(`AzureAd:ClientId`). Confirm before you start:

```bash
TENANT_ID=31fd4df1-b9c0-4abd-a4b0-0e1aceaabe9a
az webapp config appsettings list --name heblo --resource-group rgHeblo \
  --query "[?name=='AzureAd__ClientId'].value" -o tsv
API_APP_ID=8b34be89-f86f-422f-af40-7dbcd30cb66a   # use the value printed above if it differs
```

### 4.1 App role `Marketing.Agent` on the Heblo API

`--app-roles` replaces the whole list, so append to the current one:

```bash
az ad app show --id "$API_APP_ID" --query appRoles > approles.current.json
jq -e '.[] | select(.value=="Marketing.Agent")' approles.current.json && echo "already exists — skip to 4.2"
ROLE_ID=$(uuidgen | tr '[:upper:]' '[:lower:]')
jq --arg id "$ROLE_ID" '. + [{
  "allowedMemberTypes": ["Application"],
  "description": "Unattended marketing agent: reads ad data and submits proposals. Never approves.",
  "displayName": "Marketing.Agent",
  "id": $id,
  "isEnabled": true,
  "value": "Marketing.Agent"
}]' approles.current.json > approles.new.json
az ad app update --id "$API_APP_ID" --app-roles @approles.new.json
ROLE_ID=$(az ad app show --id "$API_APP_ID" --query "appRoles[?value=='Marketing.Agent'].id" -o tsv)
```

### 4.2 The `heblo-marketing-agent` app and service principal

```bash
AGENT_APP_ID=$(az ad app create --display-name heblo-marketing-agent --sign-in-audience AzureADMyOrg --query appId -o tsv)
AGENT_SP_OID=$(az ad sp create --id "$AGENT_APP_ID" --query id -o tsv)
API_SP_OID=$(az ad sp show --id "$API_APP_ID" --query id -o tsv)

# Record the required permission on the agent app (visible in the portal)...
az ad app permission add --id "$AGENT_APP_ID" --api "$API_APP_ID" --api-permissions "$ROLE_ID=Role"
# ...and grant it (this assignment is what puts Marketing.Agent into the token).
az rest --method POST \
  --uri "https://graph.microsoft.com/v1.0/servicePrincipals/$AGENT_SP_OID/appRoleAssignments" \
  --headers "Content-Type=application/json" \
  --body "{\"principalId\":\"$AGENT_SP_OID\",\"resourceId\":\"$API_SP_OID\",\"appRoleId\":\"$ROLE_ID\"}"
```

Verify it holds **exactly one** role and never `super_user`:

```bash
az rest --method GET \
  --uri "https://graph.microsoft.com/v1.0/servicePrincipals/$AGENT_SP_OID/appRoleAssignments" \
  --query "value[].appRoleId" -o tsv
jq -r '.appRoles[] | select(.value=="super_user") | .id' access-matrix-entra.generated.json
```

The first command must print only `$ROLE_ID`; it must not print the `super_user` id from the second.

### 4.3 Credential (held by the agent runtime, never by Heblo)

Prefer a certificate (`az ad app credential reset --id "$AGENT_APP_ID" --cert @agent-cert.pem --append`).
With a secret:

```bash
az ad app credential reset --id "$AGENT_APP_ID" --display-name "agent-runtime-$(date +%Y%m)" \
  --years 1 --append --query password -o tsv
```

Paste the output straight into the agent runtime's secret store. Do not put it in Heblo's Key
Vault, App Settings, the repo or a chat.

## 5. Getting a token and connecting to `/mcp`

Client-credentials token (Entra tokens live 60–90 min; the runtime fetches a fresh one per run):

```bash
curl -s -X POST "https://login.microsoftonline.com/$TENANT_ID/oauth2/v2.0/token" \
  -d grant_type=client_credentials \
  -d client_id="$AGENT_APP_ID" \
  --data-urlencode client_secret="$AGENT_SECRET" \
  --data-urlencode scope="api://$API_APP_ID/.default" | jq -r .access_token > agent.token
python3 -c "import base64,json;p=open('agent.token').read().strip().split('.')[1];p+='='*(-len(p)%4);c=json.loads(base64.urlsafe_b64decode(p));print(json.dumps({k:c.get(k) for k in ['aud','azp','appid','oid','roles','scp','idtyp']},indent=2))"
```

Expected: `roles` = `["Marketing.Agent"]`, `scp` = `null`, `oid` = `$AGENT_SP_OID`.

Smoke test (staging):

```bash
curl -s -i -X POST https://heblo.stg.anela.cz/mcp \
  -H "Authorization: Bearer $(cat agent.token)" \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"smoke","version":"0"}}}'
```

Expected: HTTP 200 with an `Mcp-Session-Id` header. A 401 with `IDW10201` means the token has no
role (step 4.2 missing); a 403 means the AppUser is inactive.

Agent runtime MCP config (streamable HTTP; the runtime substitutes the token):

```json
{
  "mcpServers": {
    "heblo": {
      "type": "http",
      "url": "https://heblo.anela.cz/mcp",
      "headers": { "Authorization": "Bearer ${HEBLO_AGENT_TOKEN}" }
    }
  }
}
```

## 6. Putting the agent into `Marketing_Agent`

The first authenticated call creates the agent's AppUser automatically. App tokens carry no name,
so its email and display name are its object id (`$AGENT_SP_OID`).

1. `/admin/access` → Users → find the user whose email is `$AGENT_SP_OID`.
2. Add it to **`Marketing_Agent` only**. No other group, never `super_user`.
3. Effective immediately (editing a user drops their permission cache).

Check: over MCP the agent can call `ListAdAccounts` and `SubmitAdProposal`; `ListPendingAdProposals`
and `ApproveAdProposal` answer `[FORBIDDEN]`; 61 calls within a minute answer HTTP 429.

## 7. Revocation and rotation

- Stop an agent now: `az ad sp update --id "$AGENT_SP_OID" --set accountEnabled=false` (new tokens
  refused; issued tokens expire within 90 min) **and** deactivate its AppUser in `/admin/access`
  (takes effect at once on that instance, ≤ 5 min elsewhere).
- Rotate: create a new credential with `--append`, switch the runtime, then delete the old one
  (`az ad app credential delete --id "$AGENT_APP_ID" --key-id <old-key-id>`).

## 8. Known gaps

- **Staging E2E identity.** On Staging, `E2ETestController` turns a client-credentials token into a
  cookie session, and cookie sessions resolve to the Web channel (rule 4). If the E2E identity holds
  `marketing.ad_approvals.write` it can therefore approve. Never grant it that permission, and never
  point Staging at live ad accounts while that identity exists.
- **Cookie plus bearer header.** A request that carries a session cookie *and* an
  `Authorization: Bearer` header is treated as a bearer request (client checked against the lists
  above); an unknown client is refused. This fails closed.

