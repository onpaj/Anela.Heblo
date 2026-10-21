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

**Response:** `200` with the full message object (same shape as items from
`GET /conversations/{id}/messages`, including `attachments`), not just
`id` / `created_at`. Verified 2026-10-16.

**Success:** 2xx
**Failure codes observed:**
- `422` `invalid_parameters` — `agent` block sent without `agent_id` (see above)

---

### POST /conversations/{id}/messages with attachments

**Status:** Verified 2026-10-16 against the live account (test conversation `coTaX8UFWh4Sz`).

Text and files travel in **one message**. `attachments` holds at most **5** items.

**Request body:**
```json
{
  "agent_id": "1257997",
  "content": { "type": "text", "text": "Test příloha 🙂" },
  "attachments": [
    { "type": "upload", "token": "QlTT4XXa2", "file_name": "test.png", "file_type": "image/png" }
  ]
}
```

`token` is the upload token from `GET /files/upload/token` (see below). `200` — the image rendered in
the customer widget. The response (and later message reads) carry the file in `attachments`:

```json
[{"type":"image","file_name":"test.png","file_type":"image/png","url":"https://files.smartsuppcdn.com/…png","size":177,"width":64,"height":64,"thumb_400":{"url":"…_thumb400.jpg","width":64,"height":64}}]
```

The `conversation.agent_replied` webhook echo carries the same `attachments` array (verified in prod:
`SmartsuppMessages.AttachmentsJson` for `msSGp4tJDyvVx`), so the echo is enough to persist sent files.
An agent file sent from the Smartsupp app arrives with `type:"file"` and no thumb.

---

### GET /files/upload/token

**Status:** Verified 2026-10-16. Requires scope `files:upload`; the prod `Smartsupp--ApiToken` has it.

**Response:** `200`
```json
{"token":"KfJ0DU-bQ","url":"https://files.smartsupp.com/upload/KfJ0DU-bQ"}
```

---

### POST <upload url>

**Status:** Verified 2026-10-16.

Upload the file to the `url` returned by `GET /files/upload/token`: `multipart/form-data`, field name
`file`, **no Authorization header** (the token in the URL is the credential).

**Response:** `200`
```json
{"size":177,"type":"image/png","name":"test.png",
 "path":"/accounts/924500/attachments/7m8eef-Fr.png",
 "url":"https://files.smartsuppcdn.com/files/accounts/924500/attachments/7m8eef-Fr.png",
 "expireAt":"2026-10-17T07:54:14.899Z","height":64,"width":64,
 "thumb400":{"path":"…_thumb400.jpg","width":64,"height":64,"expireAt":"…"},"ok":true}
```

- An upload that is never attached to a message expires after **24 h**.
- Once attached, the CDN serves it with `cache-control: public, max-age=31557600`.
- CDN URLs are public (no auth), so a browser can use them directly in `<img src>` / `<a href>`.
- Size and type limits are not published and were not probed.
- **Filename must be raw UTF-8 (verified 2026-10-21).** files.smartsupp.com reads only `filename="..."` and does not
  decode RFC 2047 (`=?utf-8?B?...?=`) or `filename*=`, which is what .NET emits for non-ASCII names. The stored name
  becomes the encoded garbage and the CDN url loses its extension. Send `filename="faktura č.1.pdf"` as raw UTF-8 like a
  browser (the upload response then has `"name":"faktura č.1.pdf"`).
- **The upload's name wins over the message's `file_name` (verified 2026-10-21).** `file_name` on the message send is
  ignored; the echoed `attachments[].file_name` uses the name given at upload.
- **An attachment-only message must omit `content` entirely (verified 2026-10-21).** `"content": null` returns
  `422 {"path":"content","code":"type","message":"must be object"}`. The agent echo of such a message has
  `content:{type:"text",text:null,data:null}` plus `attachments[]`.

---

### Message text formatting

**Status:** Verified 2026-10-16.

- **No bold/italic exists.** `*tucne*` typed in the Smartsupp agent app showed the asterisks verbatim in
  the widget; the Smartsupp app has no formatting either.
- **`content.html` is flattened.** Only `<a>` and `<br>` are allowed, and Smartsupp stores it as
  `text [url]`, e.g. `Odkaz anela.cz [https://www.anela.cz]\ndruhý řádek`, which reads worse. Send plain text.
- **The widget auto-links plain URLs** in `content.text` and shows a link-preview card; no send-side work needed.
- **Emoji work end to end.** Unicode emoji in `text` reach the widget unchanged.

---

### GET /agents

Returns the list of agents configured in the Smartsupp account.

**Status:** Verified 2026-05-21 against live account.

**Response:**
```json
{
  "agents": [
    { "id": "1257997", "name": "Ondra Pajgrt", "email": "ondra@anela.cz", "avatar": "...", "role": "admin", "status": "offline", "active": true }
  ]
}
```

Used by `SmartsuppAgentCache` to build an `agentId → displayName` lookup at runtime (lazy, cached 1 h).
Not polled on startup — first request to `GetConversation` triggers the fetch.

---

### PATCH /conversations/{id}/close

**Close (resolve) a conversation.**

**Status:** Verified against the live OpenAPI spec at `https://api.smartsupp.com/v2/specs/open-api.json` (2026-05-27).

**Background:** The first implementation used `PATCH /conversations/{id}` with body `{ "status": "closed" }`. That endpoint exists for partial updates (group_id, variables, virtual_agent) and does not accept a `status` field, so it returned `422 { "code": "invalid_parameters", "message": "Not provided any valid data" }`. The dedicated action endpoint below is the correct one.

**Request:**
```http
PATCH https://api.smartsupp.com/v2/conversations/{id}/close
Authorization: Bearer {token}
```

No body. No `Content-Type` header (no content).

**Success response:** HTTP 200. The conversation transitions to `"closed"` status (mapped to `SmartsuppConversationStatus.Resolved` in our domain via `SmartsuppPayloadMapper.cs:17`).

**Error codes:**
- `401` — missing or invalid Bearer token
- `404` — conversation not found
- `422` — unprocessable entity (rare for this action endpoint; would indicate an invalid state transition)
- `429` — rate limited (Polly `RetryAfter` header handled by `SmartsuppApiClient`)

**Implementation note:** Use `PATCH` via `HttpMethod.Patch` against `conversations/{id}/close` with no request content. Treat any non-2xx as `HttpRequestException`. `CloseConversationHandler` maps only 5xx / network / timeout failures to `SmartsuppCloseConversationUnavailable`; 4xx propagates so contract regressions surface immediately.

**Known conversation status values** (returned in `GET /conversations/{id}` and used in search filters):
- `"open"` — open conversation
- `"served"` — conversation being served by an agent
- `"pending"` — pending (waiting for agent)
- `"closed"` — closed/resolved

---

## 4. Known quirks

- `created_at` is returned as UTC ISO 8601 but `DateTime` deserialized without `DateTimeKind.Utc` — normalised to `DateTimeKind.Unspecified` via `DateTime.SpecifyKind` in `SmartsuppApiClient` to match the pattern used for all other date fields.
- Rate limit: Polly `ResiliencePipeline` applies exponential back-off on 429 responses (configured via `Smartsupp:RetryAfter` header).
