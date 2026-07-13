# Gotcha: Smartsupp staged-contact DateTime.Kind must be Utc (dropped Messenger chats)

## Symptom

Some Smartsupp conversations — most visibly **Facebook Messenger** ones (`channel.type = "facebook_messenger"` / `"default"`, `referer = l.facebook.com`) — appeared in the Smartsupp inbox but never showed up in the app's **Customer Support → Smartsupp Chats** view. Website ("chat") conversations mostly worked.

The webhook audit table (`public."SmartsuppWebhookAuditEntries"`) showed the affected events with `SignatureStatus = Valid (0)` but `ProcessingStatus = HandlerException (3)` and this error, repeated ~134×/30d:

```
System.ArgumentException: Cannot write DateTime with Kind=Unspecified to PostgreSQL type
'timestamp with time zone', only UTC is supported.
  at ...SmartsuppRepository.UpsertContactAsync(...) :line 61
  at ...SmartsuppRepository.TryFetchAndStageContactAsync(...) :line 326
  at ...SmartsuppRepository.UpsertConversationAsync(...) :line 102
```

## Root cause

Webhooks are the **only** live Smartsupp ingestion path (the REST `SearchConversationsAsync` sync has no callers / no endpoint / no job). When a conversation event references a contact not yet stored, `UpsertConversationAsync` fetches it via REST and stages it through `MapContactDataToEntity` → `UpsertContactAsync`.

`UpsertContactAsync` writes via `ExecuteSqlInterpolatedAsync`, and EF/Npgsql types a **bare `DateTime` parameter as `timestamp with time zone`** regardless of the physical column (which is `timestamp without time zone`). Npgsql then **accepts `Kind=Utc` but throws on `Kind=Unspecified`**. See the sibling gotcha [raw-sql DateTime timestamptz inference].

The two callers of `UpsertContactAsync` disagreed on Kind:
- Webhook contact events → `SmartsuppPayloadMapper.MapContact` → **Utc** → works (contact.created was 60/60 success).
- REST-staged contact (`MapContactDataToEntity`) → **Unspecified** → throws, which propagates and **drops the entire conversation**.

Messenger conversations reference Facebook contacts that are usually fetched on demand (no preceding `contact.*` webhook), so they hit the staged path — and the exception — on nearly every event (~81% failure vs ~23% for other channels).

## Fix

`MapContactDataToEntity` now stamps `CreatedAt`/`UpdatedAt`/`SyncedAt`/`BannedAt` as `DateTimeKind.Utc` (relabel, not shift — Smartsupp returns UTC, prod PG session TZ is UTC), matching the webhook path. Guarded by `SmartsuppContactMappingTests`.

**Any DateTime handed to `ExecuteSqlInterpolated` in this codebase must be `Kind=Utc`** (or explicitly wrapped in an `NpgsqlParameter { NpgsqlDbType = Timestamp }`). Keep all callers of a shared raw-SQL upsert consistent on Kind.

## Data recovery

The fix only prevents future loss. Conversations already dropped won't re-webhook. To recover them after deploy, replay the failed audit rows:
`POST /api/admin/smartsupp/webhooks/{id}/replay` for entries with `ProcessingStatus = 3` (HandlerException). The raw payloads are retained in the audit table.
