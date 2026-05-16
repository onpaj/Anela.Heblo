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

---

## Computing the HMAC

### Bash / Terminal

```bash
SECRET="your-webhook-secret"
BODY='{"event":"conversation.opened",...}'

echo -n "$BODY" | openssl dgst -sha256 -hmac "$SECRET" | awk '{print $2}'
```

### Python

```python
import hmac, hashlib

secret = b"your-webhook-secret"
body = b'{"event":"conversation.opened",...}'

sig = hmac.new(secret, body, hashlib.sha256).hexdigest()
print(sig)
```

### Postman Pre-request Script

Add this to the request's **Pre-request Script** tab. Store the secret as a Postman environment variable `smartsupp_webhook_secret`.

```javascript
const secret = pm.environment.get("smartsupp_webhook_secret");
const body = pm.request.body.raw;

const signature = CryptoJS.HmacSHA256(body, secret).toString(CryptoJS.enc.Hex);
pm.request.headers.upsert({ key: "X-Smartsupp-Hmac", value: signature });
```

---

## Envelope Structure

All events share the same root structure. The `data` object always contains a nested key (`conversation`, `message`, or `contact`) — it is **not** flat.

```json
{
  "event": "<event-name>",
  "timestamp": "2026-05-15T10:00:00Z",
  "account_id": "test-account-id",
  "app_id": "test-app-id",
  "data": {
    "conversation": { ... },
    "message": { ... },
    "contact": { ... }
  }
}
```

---

## Handled Events

### Contact Events

All contact events expect `data.contact`. The contact object fields:

| Field | Type | Notes |
|-------|------|-------|
| `id` | string | Required |
| `email` | string | |
| `name` | string | |
| `phone` | string | |
| `note` | string | |
| `banned_at` | datetime | Set for `contact.banned` |
| `banned_by` | string | Agent ID who banned |
| `gdpr_approved` | bool | |
| `tags` | array | |
| `properties` | object | |
| `created_at` | datetime | Required |
| `updated_at` | datetime | Required |

#### `contact.created`

```json
{
  "event": "contact.created",
  "timestamp": "2026-05-15T10:00:00Z",
  "account_id": "test-account-id",
  "app_id": "test-app-id",
  "data": {
    "contact": {
      "id": "contact-001",
      "email": "jana.novakova@example.com",
      "name": "Jana Nováková",
      "phone": "+420123456789",
      "note": null,
      "banned_at": null,
      "banned_by": null,
      "gdpr_approved": true,
      "tags": [],
      "properties": {},
      "created_at": "2026-05-15T10:00:00Z",
      "updated_at": "2026-05-15T10:00:00Z"
    }
  }
}
```

#### `contact.updated`

Same structure as `contact.created`, `event` = `contact.updated`.

#### `contact.acquired`

Same structure as `contact.created`, `event` = `contact.acquired`.

#### `contact.banned`

```json
{
  "event": "contact.banned",
  "timestamp": "2026-05-15T11:00:00Z",
  "account_id": "test-account-id",
  "app_id": "test-app-id",
  "data": {
    "contact": {
      "id": "contact-001",
      "email": "jana.novakova@example.com",
      "name": "Jana Nováková",
      "phone": null,
      "note": null,
      "banned_at": "2026-05-15T11:00:00Z",
      "banned_by": "agent-001",
      "gdpr_approved": false,
      "tags": [],
      "properties": {},
      "created_at": "2026-05-15T10:00:00Z",
      "updated_at": "2026-05-15T11:00:00Z"
    }
  }
}
```

#### `contact.unbanned`

Same as `contact.banned` but `banned_at` and `banned_by` are `null`, `event` = `contact.unbanned`.

---

### Conversation Events

Most conversation events expect `data.conversation`. The conversation object fields:

| Field | Type | Notes |
|-------|------|-------|
| `id` | string | Required |
| `ext_id` | string | |
| `subject` | string | |
| `status` | string | `"open"`, `"closed"`, `"pending"` |
| `unread` | bool | |
| `is_offline` | bool | |
| `is_served` | bool | |
| `contact_id` | string | |
| `visitor_id` | string | |
| `finished_at` | datetime | |
| `domain` | string | |
| `referer` | string | |
| `channel` | object | `{ "type": "chat" }` |
| `last_message_at` | datetime | |
| `last_message_text` | string | |
| `tags` | array | |
| `variables` | object | |
| `assigned_agent_ids` | array | |
| `rating` | int | |
| `rating_text` | string | |
| `close_type` | string | |
| `closed_by_agent_id` | string | |
| `last_closed_at` | datetime | |
| `contact_name` | string | |
| `contact_email` | string | |
| `contact_avatar_url` | string | |
| `location_country` | string | |
| `location_city` | string | |
| `location_ip` | string | |
| `location_code` | string | |
| `created_at` | datetime | Required |
| `updated_at` | datetime | Required |

#### `conversation.opened`

```json
{
  "event": "conversation.opened",
  "timestamp": "2026-05-15T10:00:00Z",
  "account_id": "test-account-id",
  "app_id": "test-app-id",
  "data": {
    "conversation": {
      "id": "conv-001",
      "ext_id": null,
      "subject": null,
      "status": "open",
      "unread": true,
      "is_offline": false,
      "is_served": false,
      "contact_id": "contact-001",
      "visitor_id": "visitor-001",
      "finished_at": null,
      "domain": "www.anela.cz",
      "referer": "https://www.anela.cz/",
      "channel": { "type": "chat" },
      "last_message_at": null,
      "last_message_text": null,
      "tags": [],
      "variables": {},
      "assigned_agent_ids": [],
      "contact_name": "Jana Nováková",
      "contact_email": "jana.novakova@example.com",
      "contact_avatar_url": null,
      "location_country": "CZ",
      "location_city": "Praha",
      "location_ip": "1.2.3.4",
      "location_code": "CZ",
      "created_at": "2026-05-15T10:00:00Z",
      "updated_at": "2026-05-15T10:00:00Z"
    }
  }
}
```

#### `conversation.closed`

Adds `close_type` and `agent_id` at the top level of `data` (alongside `conversation`).

```json
{
  "event": "conversation.closed",
  "timestamp": "2026-05-15T11:00:00Z",
  "account_id": "test-account-id",
  "app_id": "test-app-id",
  "data": {
    "close_type": "agent",
    "agent_id": "agent-001",
    "conversation": {
      "id": "conv-001",
      "status": "closed",
      "unread": false,
      "is_offline": false,
      "is_served": true,
      "contact_id": "contact-001",
      "visitor_id": "visitor-001",
      "finished_at": "2026-05-15T11:00:00Z",
      "domain": "www.anela.cz",
      "referer": "https://www.anela.cz/",
      "channel": { "type": "chat" },
      "last_message_at": "2026-05-15T10:55:00Z",
      "last_message_text": "Děkujeme za kontakt.",
      "tags": [],
      "variables": {},
      "assigned_agent_ids": ["agent-001"],
      "created_at": "2026-05-15T10:00:00Z",
      "updated_at": "2026-05-15T11:00:00Z"
    }
  }
}
```

#### `conversation.closed_by_contact`

```json
{
  "event": "conversation.closed_by_contact",
  "timestamp": "2026-05-15T11:00:00Z",
  "account_id": "test-account-id",
  "app_id": "test-app-id",
  "data": {
    "conversation": {
      "id": "conv-001",
      "status": "closed",
      "unread": false,
      "is_offline": false,
      "is_served": true,
      "contact_id": "contact-001",
      "visitor_id": "visitor-001",
      "finished_at": "2026-05-15T11:00:00Z",
      "domain": "www.anela.cz",
      "referer": "https://www.anela.cz/",
      "channel": { "type": "chat" },
      "last_message_at": "2026-05-15T10:55:00Z",
      "last_message_text": "Díky, na shledanou.",
      "tags": [],
      "variables": {},
      "assigned_agent_ids": [],
      "created_at": "2026-05-15T10:00:00Z",
      "updated_at": "2026-05-15T11:00:00Z"
    }
  }
}
```

#### `conversation.rated`

Adds `rating_value` (int) and `rating_text` (string) at the top level of `data`.

```json
{
  "event": "conversation.rated",
  "timestamp": "2026-05-15T11:05:00Z",
  "account_id": "test-account-id",
  "app_id": "test-app-id",
  "data": {
    "rating_value": 5,
    "rating_text": "Skvělá pomoc!",
    "conversation": {
      "id": "conv-001",
      "status": "closed",
      "unread": false,
      "is_offline": false,
      "is_served": true,
      "contact_id": "contact-001",
      "visitor_id": "visitor-001",
      "domain": "www.anela.cz",
      "channel": { "type": "chat" },
      "tags": [],
      "variables": {},
      "assigned_agent_ids": ["agent-001"],
      "created_at": "2026-05-15T10:00:00Z",
      "updated_at": "2026-05-15T11:05:00Z"
    }
  }
}
```

#### `conversation.agent_assigned`

Adds `assigned` (agent ID string) at the top level of `data`.

```json
{
  "event": "conversation.agent_assigned",
  "timestamp": "2026-05-15T10:02:00Z",
  "account_id": "test-account-id",
  "app_id": "test-app-id",
  "data": {
    "assigned": "agent-001",
    "conversation": {
      "id": "conv-001",
      "status": "open",
      "unread": true,
      "is_offline": false,
      "is_served": true,
      "contact_id": "contact-001",
      "visitor_id": "visitor-001",
      "domain": "www.anela.cz",
      "channel": { "type": "chat" },
      "assigned_agent_ids": ["agent-001"],
      "tags": [],
      "variables": {},
      "created_at": "2026-05-15T10:00:00Z",
      "updated_at": "2026-05-15T10:02:00Z"
    }
  }
}
```

#### `conversation.agent_unassigned`

```json
{
  "event": "conversation.agent_unassigned",
  "timestamp": "2026-05-15T10:10:00Z",
  "account_id": "test-account-id",
  "app_id": "test-app-id",
  "data": {
    "conversation": {
      "id": "conv-001",
      "status": "open",
      "unread": true,
      "is_offline": false,
      "is_served": false,
      "contact_id": "contact-001",
      "visitor_id": "visitor-001",
      "domain": "www.anela.cz",
      "channel": { "type": "chat" },
      "assigned_agent_ids": [],
      "tags": [],
      "variables": {},
      "created_at": "2026-05-15T10:00:00Z",
      "updated_at": "2026-05-15T10:10:00Z"
    }
  }
}
```

#### `conversation.agent_joined` / `conversation.agent_left`

Received but trigger no action (no-op). Any minimal body is accepted:

```json
{
  "event": "conversation.agent_joined",
  "timestamp": "2026-05-15T10:03:00Z",
  "account_id": "test-account-id",
  "app_id": "test-app-id",
  "data": {
    "conversation_id": "conv-001",
    "agent_id": "agent-001"
  }
}
```

---

### Conversation + Message Events

These events carry both a `conversation` and a `message` in `data`. The message fields:

| Field | Type | Notes |
|-------|------|-------|
| `id` | string | Required |
| `conversation_id` | string | |
| `sub_type` | string | `"agent"`, `"bot"`, `"contact"`, `"system"`, `"trigger"` |
| `type` | string | |
| `content` | string or object | String for agent/contact; `{ "text": "..." }` for bot/trigger |
| `author_name` | string | |
| `trigger_name` | string | |
| `trigger_id` | string | |
| `page_url` | string | |
| `agent_id` | string | |
| `visitor_id` | string | |
| `delivery_status` | string | |
| `delivered_at` | datetime | |
| `is_offline` | bool | |
| `is_reply` | bool | |
| `is_first_reply` | bool | |
| `response_time` | int | Seconds |
| `attachments` | array | |
| `created_at` | datetime | Required |
| `updated_at` | datetime | Required |

#### `conversation.contact_replied`

```json
{
  "event": "conversation.contact_replied",
  "timestamp": "2026-05-15T10:01:00Z",
  "account_id": "test-account-id",
  "app_id": "test-app-id",
  "data": {
    "conversation": {
      "id": "conv-001",
      "status": "open",
      "unread": true,
      "is_offline": false,
      "is_served": false,
      "contact_id": "contact-001",
      "visitor_id": "visitor-001",
      "domain": "www.anela.cz",
      "channel": { "type": "chat" },
      "last_message_at": "2026-05-15T10:01:00Z",
      "last_message_text": "Dobrý den, mám dotaz na produkt.",
      "tags": [],
      "variables": {},
      "assigned_agent_ids": [],
      "created_at": "2026-05-15T10:00:00Z",
      "updated_at": "2026-05-15T10:01:00Z"
    },
    "message": {
      "id": "msg-001",
      "conversation_id": "conv-001",
      "sub_type": "contact",
      "content": "Dobrý den, mám dotaz na produkt.",
      "author_name": "Jana Nováková",
      "page_url": "https://www.anela.cz/produkt/krem",
      "agent_id": null,
      "visitor_id": "visitor-001",
      "delivery_status": "delivered",
      "delivered_at": "2026-05-15T10:01:00Z",
      "is_offline": false,
      "is_reply": false,
      "is_first_reply": false,
      "response_time": null,
      "attachments": [],
      "created_at": "2026-05-15T10:01:00Z",
      "updated_at": "2026-05-15T10:01:00Z"
    }
  }
}
```

#### `conversation.agent_replied`

```json
{
  "event": "conversation.agent_replied",
  "timestamp": "2026-05-15T10:03:00Z",
  "account_id": "test-account-id",
  "app_id": "test-app-id",
  "data": {
    "conversation": {
      "id": "conv-001",
      "status": "open",
      "unread": false,
      "is_offline": false,
      "is_served": true,
      "contact_id": "contact-001",
      "visitor_id": "visitor-001",
      "domain": "www.anela.cz",
      "channel": { "type": "chat" },
      "last_message_at": "2026-05-15T10:03:00Z",
      "last_message_text": "Dobrý den, rádi vám pomůžeme!",
      "tags": [],
      "variables": {},
      "assigned_agent_ids": ["agent-001"],
      "created_at": "2026-05-15T10:00:00Z",
      "updated_at": "2026-05-15T10:03:00Z"
    },
    "message": {
      "id": "msg-002",
      "conversation_id": "conv-001",
      "sub_type": "agent",
      "content": "Dobrý den, rádi vám pomůžeme!",
      "author_name": "Anela Support",
      "page_url": null,
      "agent_id": "agent-001",
      "visitor_id": null,
      "delivery_status": "delivered",
      "delivered_at": "2026-05-15T10:03:00Z",
      "is_offline": false,
      "is_reply": true,
      "is_first_reply": true,
      "response_time": 120,
      "attachments": [],
      "created_at": "2026-05-15T10:03:00Z",
      "updated_at": "2026-05-15T10:03:00Z"
    }
  }
}
```

#### `conversation.bot_replied`

`content` is an object `{ "text": "..." }` for bot messages.

```json
{
  "event": "conversation.bot_replied",
  "timestamp": "2026-05-15T10:00:30Z",
  "account_id": "test-account-id",
  "app_id": "test-app-id",
  "data": {
    "conversation": {
      "id": "conv-001",
      "status": "open",
      "unread": true,
      "is_offline": false,
      "is_served": false,
      "contact_id": null,
      "visitor_id": "visitor-001",
      "domain": "www.anela.cz",
      "channel": { "type": "chat" },
      "last_message_at": "2026-05-15T10:00:30Z",
      "last_message_text": "Vítejte! Jak vám mohu pomoci?",
      "tags": [],
      "variables": {},
      "assigned_agent_ids": [],
      "created_at": "2026-05-15T10:00:00Z",
      "updated_at": "2026-05-15T10:00:30Z"
    },
    "message": {
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
      "attachments": [],
      "created_at": "2026-05-15T10:00:30Z",
      "updated_at": "2026-05-15T10:00:30Z"
    }
  }
}
```

---

### Message Delivery Events

Only `data.message.id` is used.

#### `conversation.message_delivered`

```json
{
  "event": "conversation.message_delivered",
  "timestamp": "2026-05-15T10:03:05Z",
  "account_id": "test-account-id",
  "app_id": "test-app-id",
  "data": {
    "message": {
      "id": "msg-002",
      "conversation_id": "conv-001",
      "created_at": "2026-05-15T10:03:00Z",
      "updated_at": "2026-05-15T10:03:05Z"
    }
  }
}
```

#### `conversation.message_delivery_failed`

```json
{
  "event": "conversation.message_delivery_failed",
  "timestamp": "2026-05-15T10:03:05Z",
  "account_id": "test-account-id",
  "app_id": "test-app-id",
  "data": {
    "message": {
      "id": "msg-002",
      "conversation_id": "conv-001",
      "created_at": "2026-05-15T10:03:00Z",
      "updated_at": "2026-05-15T10:03:05Z"
    }
  }
}
```

---

## Unhandled Events

These events return `200 OK` but are not processed:

| Event | Behaviour |
|-------|-----------|
| `conversation.created` | Not implemented — use `conversation.opened` |
| `conversation.updated` | Not implemented |
| `message.created` | Not implemented — use `conversation.contact_replied` / `conversation.agent_replied` |
| `visitor.*` | Observed (logged, no action) |
| `app.*` | Ignored |
| Anything else | Unknown (logged) |

---

## Expected Responses

| Scenario | HTTP Status |
|----------|-------------|
| Valid signature, handled event | 200 OK |
| Valid signature, unhandled event | 200 OK |
| Invalid or missing `X-Smartsupp-Hmac` | 401 Unauthorized |
| `WebhookSecret` not configured | 401 Unauthorized |
| `app_id` mismatch (when `WebhookAppId` is set) | 401 Unauthorized |
| Malformed JSON | 200 OK |

---

## Local Testing Setup

1. Start the backend: `dotnet run` from `backend/src/Anela.Heblo.API/`
2. Default local URL: `http://localhost:5000/api/webhooks/smartsupp`
3. Set `Smartsupp:WebhookSecret` in `secrets.json` to a known value
4. Compute `X-Smartsupp-Hmac` from the request body using the same secret (or use the Postman pre-request script)
5. If `WebhookAppId` is configured, the `app_id` field must match it exactly

---

## `app_id` Verification

If `Smartsupp:WebhookAppId` is set, every request's `app_id` must match exactly (case-sensitive). Leave empty to accept any `app_id`.
