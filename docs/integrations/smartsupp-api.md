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

> ⚠️ **UNVERIFIED** — This shape was inferred from the Smartsupp API reference and implemented speculatively.
> Verify against https://docs.smartsupp.com/rest-api on the first staging deployment.
> Update this document once confirmed.

**Request body (assumed):**
```json
{
  "content": {
    "type": "text",
    "text": "Message content here"
  },
  "agent": {
    "name": "Ondřej"
  }
}
```

The `agent` field is optional. If omitted, the message is sent without an agent name.

**Response (assumed):**
```json
{
  "id": "message-id",
  "created_at": "2026-05-20T10:00:00Z"
}
```

**Success:** 2xx  
**Failure codes observed:** TBD (verify on staging)

---

## 4. Known quirks

- `created_at` is returned as UTC ISO 8601 but `DateTime` deserialized without `DateTimeKind.Utc` — normalised to `DateTimeKind.Unspecified` via `DateTime.SpecifyKind` in `SmartsuppApiClient` to match the pattern used for all other date fields.
- Rate limit: Polly `ResiliencePipeline` applies exponential back-off on 429 responses (configured via `Smartsupp:RetryAfter` header).
