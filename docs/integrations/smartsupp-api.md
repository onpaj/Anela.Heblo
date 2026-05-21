# Smartsupp REST API — Integration Findings

> **Living document.** Every new finding about the Smartsupp REST API MUST be added here before it is used elsewhere.
> API reference: https://docs.smartsupp.com/rest-api
> No sandbox — all calls hit the live account configured by `Smartsupp:ApiToken`.

---

## 1. Overview

Smartsupp exposes a REST API at `https://app.smartsupp.com/api/v2/`. The project uses
`Anela.Heblo.Adapters.Smartsupp.SmartsuppApiClient` for all REST operations.

Base URL is configured via `Smartsupp:BaseUrl` (appsettings). Default: `https://app.smartsupp.com/api/v2/`.

---

## 2. Authentication

- **Bearer token** in `Authorization` header.
- Token is stored in `Smartsupp:ApiToken` (user-secrets / Azure Key Vault, never in source).

---

## 3. Endpoints

### GET /conversations

List conversations. Supports `status`, `page`, `pageSize` query params.

Response: `{ items: ConversationObject[], total: int }`

### GET /conversations/{id}/messages

Get messages for a conversation. Supports `size` query param.

Response: `{ items: MessageObject[], total: int }`

### POST /conversations/{id}/messages

**Send a message in a conversation on behalf of an agent.**

**Verified production behavior (2026-05-21):** Including the `agent` block triggers
`sub_type=agent` on Smartsupp's side and makes `agent_id` mandatory. Smartsupp returns:

```
HTTP 422 Unprocessable Entity
{"code":"invalid_parameters","message":"Property agent_id is required when sub_type is \"agent\""}
```

We send **`agent_id` only — never the `agent` block**. The recipient sees the
sender name resolved from the Smartsupp agent profile.

**Request body:**
```json
{
  "content": {
    "type": "text",
    "text": "Message content here"
  },
  "agent_id": "<smartsupp_agent_id>"
}
```

`SmartsuppApiClient.SendMessageAsync` accepts `agentId` as a parameter. The
`SendMessageHandler` resolves it from `SmartsuppSendMessageOptions.AgentMap`
(keyed by Heblo user email). Users missing from the map cannot send messages
and receive `SmartsuppAgentMappingNotFound`. When `agentId` is null, the
message is attributed to the API token's default sender (used by future
automatic-reply paths).

**Response (assumed):**
```json
{
  "id": "message-id",
  "created_at": "2026-05-20T10:00:00Z"
}
```

**Success:** 2xx
**Failure codes observed:**
- `422` `invalid_parameters` — `agent` block sent without `agent_id` (see above)

---

## 4. Known quirks

- `created_at` is returned as UTC ISO 8601 but `DateTime` deserialized without `DateTimeKind.Utc` — normalised to `DateTimeKind.Unspecified` via `DateTime.SpecifyKind` in `SmartsuppApiClient` to match the pattern used for all other date fields.
- Rate limit: Polly `ResiliencePipeline` applies exponential back-off on 429 responses (configured via `Smartsupp:RetryAfter` header).
