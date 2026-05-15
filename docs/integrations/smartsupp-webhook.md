# Smartsupp Webhook — Postman Testing Guide

## Endpoint

```
POST /api/webhooks/smartsupp
```

- No Bearer token required (`[AllowAnonymous]`)
- Request size limit: 1 MB
- Always returns `200 OK` (even on downstream errors — fire-and-forget pattern)

---

## Required Headers

| Header | Value |
|--------|-------|
| `Content-Type` | `application/json` |
| `X-Smartsupp-Hmac` | HMAC-SHA256 of the raw request body, hex-encoded lowercase, signed with `WebhookSecret` |

The signature check is always enforced — there is no bypass mode. You must compute and include a valid `X-Smartsupp-Hmac` header for every request, using the same secret configured in `secrets.json` under `Smartsupp:WebhookSecret`.

---

## Computing the HMAC

### Bash / Terminal

```bash
SECRET="your-webhook-secret"
BODY='{"event":"conversation.created",...}'

echo -n "$BODY" | openssl dgst -sha256 -hmac "$SECRET" | awk '{print $2}'
```

### Python

```python
import hmac, hashlib

secret = b"your-webhook-secret"
body = b'{"event":"conversation.created",...}'

sig = hmac.new(secret, body, hashlib.sha256).hexdigest()
print(sig)
```

### Postman Pre-request Script

Add this to the request's **Pre-request Script** tab (replace `SECRET` with your value or use a Postman variable):

```javascript
const secret = pm.environment.get("smartsupp_webhook_secret");
const body = pm.request.body.raw;

const signature = CryptoJS.HmacSHA256(body, secret).toString(CryptoJS.enc.Hex);
pm.request.headers.upsert({ key: "X-Smartsupp-Hmac", value: signature });
```

---

## Supported Events

### 1. `conversation.created`

```json
{
  "event": "conversation.created",
  "timestamp": "2026-05-15T10:00:00Z",
  "account_id": "test-account-id",
  "app_id": "test-app-id",
  "data": {
    "id": "conv-001",
    "ext_id": null,
    "status": "open",
    "unread": true,
    "is_offline": false,
    "is_served": false,
    "contact_id": "contact-001",
    "visitor_id": "visitor-001",
    "finished_at": null,
    "domain": "www.anela.cz",
    "referer": "https://www.anela.cz/",
    "last_message_at": "2026-05-15T10:00:00Z",
    "last_message_text": "Dobrý den, mám dotaz.",
    "created_at": "2026-05-15T10:00:00Z",
    "updated_at": "2026-05-15T10:00:00Z"
  }
}
```

---

### 2. `conversation.updated`

Same structure as `conversation.created`. Use when an existing conversation changes (e.g., a new agent picks it up).

```json
{
  "event": "conversation.updated",
  "timestamp": "2026-05-15T10:05:00Z",
  "account_id": "test-account-id",
  "app_id": "test-app-id",
  "data": {
    "id": "conv-001",
    "ext_id": null,
    "status": "open",
    "unread": false,
    "is_offline": false,
    "is_served": true,
    "contact_id": "contact-001",
    "visitor_id": "visitor-001",
    "finished_at": null,
    "domain": "www.anela.cz",
    "referer": "https://www.anela.cz/",
    "last_message_at": "2026-05-15T10:04:00Z",
    "last_message_text": "Hned se na to podíváme.",
    "created_at": "2026-05-15T10:00:00Z",
    "updated_at": "2026-05-15T10:05:00Z"
  }
}
```

---

### 3. `conversation.closed`

Same structure but `status` = `"resolved"` and `finished_at` is set.

```json
{
  "event": "conversation.closed",
  "timestamp": "2026-05-15T11:00:00Z",
  "account_id": "test-account-id",
  "app_id": "test-app-id",
  "data": {
    "id": "conv-001",
    "ext_id": null,
    "status": "resolved",
    "unread": false,
    "is_offline": false,
    "is_served": true,
    "contact_id": "contact-001",
    "visitor_id": "visitor-001",
    "finished_at": "2026-05-15T11:00:00Z",
    "domain": "www.anela.cz",
    "referer": "https://www.anela.cz/",
    "last_message_at": "2026-05-15T10:55:00Z",
    "last_message_text": "Děkujeme za kontakt.",
    "created_at": "2026-05-15T10:00:00Z",
    "updated_at": "2026-05-15T11:00:00Z"
  }
}
```

---

### 4. `message.created` — contact message

```json
{
  "event": "message.created",
  "timestamp": "2026-05-15T10:01:00Z",
  "account_id": "test-account-id",
  "app_id": "test-app-id",
  "data": {
    "id": "msg-001",
    "conversation_id": "conv-001",
    "sub_type": "contact",
    "content": "Dobrý den, mám dotaz na produkt.",
    "author_name": "Jana Nováková",
    "trigger_name": null,
    "trigger_id": null,
    "page_url": "https://www.anela.cz/produkt/krem",
    "agent_id": null,
    "visitor_id": "visitor-001",
    "delivery_status": "delivered",
    "delivered_at": "2026-05-15T10:01:00Z",
    "is_offline": false,
    "is_reply": false,
    "is_first_reply": false,
    "response_time": null,
    "created_at": "2026-05-15T10:01:00Z",
    "updated_at": "2026-05-15T10:01:00Z"
  }
}
```

---

### 5. `message.created` — agent reply

```json
{
  "event": "message.created",
  "timestamp": "2026-05-15T10:03:00Z",
  "account_id": "test-account-id",
  "app_id": "test-app-id",
  "data": {
    "id": "msg-002",
    "conversation_id": "conv-001",
    "sub_type": "agent",
    "content": "Dobrý den, rádi vám pomůžeme!",
    "author_name": "Anela Support",
    "trigger_name": null,
    "trigger_id": null,
    "page_url": null,
    "agent_id": "agent-001",
    "visitor_id": null,
    "delivery_status": "delivered",
    "delivered_at": "2026-05-15T10:03:00Z",
    "is_offline": false,
    "is_reply": true,
    "is_first_reply": true,
    "response_time": 120,
    "created_at": "2026-05-15T10:03:00Z",
    "updated_at": "2026-05-15T10:03:00Z"
  }
}
```

---

### 6. `message.created` — bot message

The `content` field is an object with a `text` property for bot messages.

```json
{
  "event": "message.created",
  "timestamp": "2026-05-15T10:00:30Z",
  "account_id": "test-account-id",
  "app_id": "test-app-id",
  "data": {
    "id": "msg-bot-001",
    "conversation_id": "conv-001",
    "sub_type": "bot",
    "content": { "text": "Vítejte! Jak vám mohu pomoci?" },
    "author_name": null,
    "trigger_name": "welcome-trigger",
    "trigger_id": "trigger-001",
    "page_url": "https://www.anela.cz/",
    "agent_id": null,
    "visitor_id": "visitor-001",
    "delivery_status": null,
    "delivered_at": null,
    "is_offline": false,
    "is_reply": false,
    "is_first_reply": false,
    "response_time": null,
    "created_at": "2026-05-15T10:00:30Z",
    "updated_at": "2026-05-15T10:00:30Z"
  }
}
```

---

## Expected Responses

| Scenario | HTTP Status | Body |
|----------|-------------|------|
| Valid signature, known event handled | 200 OK | _(empty)_ |
| Valid signature, unknown event | 200 OK | _(empty)_ |
| Invalid or missing `X-Smartsupp-Hmac` | 401 Unauthorized | _(empty)_ |
| `WebhookSecret` not configured | 401 Unauthorized | _(empty)_ |
| `app_id` mismatch (when `WebhookAppId` is configured) | 401 Unauthorized | _(empty)_ |
| Malformed JSON | 200 OK | _(empty — fire-and-forget, avoids webhook retries)_ |

> **Note:** The controller always returns `200 OK` for processing errors (downstream handler failures). Only signature and app ID mismatches return `401`.

---

## Local Testing Setup

1. Start the backend: `dotnet run` from `backend/src/Anela.Heblo.API/`
2. Default local URL: `http://localhost:5001/api/webhooks/smartsupp`
3. Set `Smartsupp:WebhookSecret` in `secrets.json` to a known test value (e.g., `"test-secret"`)
4. Compute `X-Smartsupp-Hmac` from the request body using the same secret (see scripts above)
5. If `WebhookAppId` is set in config, the `app_id` field in your request body must match it exactly

---

## `app_id` Verification

If `Smartsupp:WebhookAppId` is configured, every incoming request's `app_id` field must match it exactly (case-sensitive). Mismatch returns `401`. Leave `WebhookAppId` empty to accept any `app_id`.
