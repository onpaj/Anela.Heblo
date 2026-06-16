telemetry-signal: req-5xx:SmartsuppWebhook/Receive:500

## Signal

`POST SmartsuppWebhook/Receive` returned HTTP 500 **6 times** in a 4-second burst at **2026-06-13 12:25 UTC** (all within `12:25:28–12:25:32`).

## Root cause

```
Microsoft.EntityFrameworkCore.DbUpdateException
  → Npgsql.PostgresException: 22001: value too long for type character varying(500)
```

App call stack (innermost app frames):

```
SmartsuppWebhookController.Receive
  → ProcessWebhookEventHandler.Handle
    → SmartsuppRepository.SaveChangesAsync   ← throws here
```

A Smartsupp webhook payload contained a string field (likely a conversation message or note) that exceeded the `varchar(500)` column limit in the Smartsupp persistence schema. EF Core threw `DbUpdateException`; the controller returned 500. The 4-second burst (6 requests to 3 distinct `operation_Id` values) suggests Smartsupp retried the webhook on failure.

## Data — P7D window (2026-06-07 → 2026-06-14)

| Metric | Value |
|---|---|
| `POST SmartsuppWebhook/Receive` 500s | 6 |
| Time of burst | 2026-06-13 12:25:28 – 12:25:32 UTC |
| Exception | `Npgsql.PostgresException 22001` |
| Constraint violated | `character varying(500)` |
| Distinct operation_Ids | 3 (likely 3 retried webhook events) |
| Fix merged in-window? | No |

## Correlation hypothesis

No code change is deployed between Jun 12–13 that would affect the Smartsupp schema. The trigger is a live Smartsupp event (agent note, message, or tag) whose content exceeded 500 characters — a content event rather than a code regression. The burst pattern is consistent with Smartsupp automatically retrying webhook delivery after receiving 500.

## Minimal next step

1. Identify which column in the Smartsupp schema has the `varchar(500)` limit — check `Anela.Heblo.Persistence/Smartsupp/` migrations and the entity model for the field mapped in `SmartsuppRepository.SaveChangesAsync`.
2. Either: (a) add a migration to increase the column size (e.g. `varchar(2000)` or `text`), or (b) truncate the offending field in the application layer before persisting. Option (a) is safer and avoids silent data loss.
3. Once fixed, resubmit the failed webhook via the Smartsupp dashboard or the replay tool (`tools/SmartsuppWebhookReplay`) if the original payload is recoverable.