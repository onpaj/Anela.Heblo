# Article RequestedBy backfill

One-off migration to convert `Articles.RequestedBy` from display names (legacy)
to stable Entra identifiers (current). After the ownership-fix release ships,
existing rows still hold display names — without this backfill, their original
authors cannot submit feedback on their own articles.

## Prerequisites

1. **Row-count baseline.** Run on production (or the target environment):
   ```sql
   SELECT count(*) FROM "Articles" WHERE "RequestedBy" IS NOT NULL;
   ```
   If the count exceeds ~10,000, contact engineering before running the
   backfill — the current implementation loads all rows into memory in a
   single batch.

2. **Backup the Articles table.** The backfill writes display names out
   in-place; the original values are not preserved post-write. Take a
   `pg_dump` of the `Articles` table immediately before running.
   ```bash
   pg_dump -Fc -t '"Articles"' \
     -h <host> -U <user> -d <db> \
     -f articles-backup-$(date +%Y%m%d-%H%M).dump
   ```

3. **Confirm the Entra group.** Find the group ID whose members historically
   generated articles (typically the marketing group). Verify membership in
   Azure Portal → Microsoft Entra ID → Groups.

4. **Confirm Graph permissions.** The application identity needs
   `GroupMember.Read.All` (or `User.Read.All`) — already granted in production
   per `GraphService` usage. Re-verify in the target tenant's app
   registration before running.

5. **Confirm SuperUser role.** The endpoint is gated by the `super_user` role
   on the calling user.

## Running the backfill

The backfill is an authenticated HTTP POST to the production API. Acquire an
access token for an account that holds the `super_user` role.

### Step 1 — Dry run (preview)

```bash
curl -X POST \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"groupId":"<entra-group-id>","dryRun":true}' \
  https://<api-host>/api/articles/admin/backfill-requested-by
```

The response includes:
- `total`: how many rows have a non-null `RequestedBy`
- `alreadyMigrated`: rows whose stored value already looks like an identifier
  (GUID or contains `@`) — skipped
- `resolved`: rows that mapped to exactly one group member
- `ambiguous`: rows whose display name matched multiple group members —
  left untouched, included in `unresolvedRows`
- `unresolved`: rows whose display name matched no group member — left
  untouched, included in `unresolvedRows`
- `wasDryRun`: `true` for this call

Review `unresolvedRows`. For each row, decide whether to triage manually
(see "Manual triage" below) before the write run.

### Step 2 — Write run

```bash
curl -X POST \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"groupId":"<entra-group-id>","dryRun":false}' \
  https://<api-host>/api/articles/admin/backfill-requested-by
```

Persists every `resolved` row in a single `SaveChanges`. Ambiguous and
unresolved rows are still left untouched.

### Step 3 — Verify

```sql
SELECT count(*) FROM "Articles"
WHERE "RequestedBy" IS NOT NULL
  AND "RequestedBy" !~ '^[0-9a-fA-F\-]{36}$'
  AND "RequestedBy" NOT LIKE '%@%';
```

Should equal `ambiguous + unresolved` from the last write-run response.

## Idempotency

The handler skips any row whose `RequestedBy` is GUID-shaped or contains `@`.
Re-running the backfill after a successful run is a no-op for already-migrated
rows; only rows still holding a display name are reconsidered.

## Manual triage

For ambiguous rows, decide via context (which marketing person worked on
that topic, when, etc.) which OID to write, then:

```sql
UPDATE "Articles" SET "RequestedBy" = '<oid>' WHERE "Id" = '<article-id>';
```

For unresolved rows (e.g. authors who have left the org), either find their
old OID in Azure Portal's deleted-users view, or leave the row owner-less.
Leaving it owner-less means the original author cannot submit feedback even
if they later rejoin under the same OID — accepted per the spec.

## Rollback

There is no in-product rollback. Restore the `Articles` table from the
backup taken in Prerequisites step 2:

```bash
pg_restore -h <host> -U <user> -d <db> --data-only -t '"Articles"' \
  articles-backup-<timestamp>.dump
```
