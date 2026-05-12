# Photobank `GET /api/photobank/photos` performance migration — deployment runbook

**Migration:** `20260512150628_OptimizePhotobankPhotoQuery`
**SLO target:** p50 ≤ 200 ms, p95 ≤ 800 ms for `GET /api/photobank/photos`.

## Prerequisites (run BEFORE merging the PR)

1. **Confirm `pg_trgm` is allowed on the Azure PostgreSQL Flexible Server.**
   Open Azure Portal → the target server → Server parameters → search for `azure.extensions`. The comma-separated list must include `PG_TRGM` (case-insensitive). If missing, add it and **restart the server** (the parameter is static).

2. **Confirm the application connection-string role has `CREATE` on the target database.** If not, an admin must run `CREATE EXTENSION IF NOT EXISTS pg_trgm;` manually as a one-time step **before** running the migration, and the `migrationBuilder.Sql("CREATE EXTENSION ...")` line in `Up()` should be replaced with a `DO` block that no-ops if the extension is present.

3. **Capture baseline latency** for `GET /api/photobank/photos` from Application Insights (24 h window). Record p50 / p95 / max — these are the numbers the post-deploy validation compares against.

## Deploy

1. Merge the PR. The single Docker image is pushed by CI.
2. SSH / cloud shell into the Azure environment with the migration tooling. From the application directory:

   ```bash
   cd backend
   dotnet ef database update \
     --project src/Anela.Heblo.Persistence \
     --startup-project src/Anela.Heblo.API \
     --connection "<production connection string>"
   ```

   Expected output: one new migration applied, no errors. The GIN index build acquires a `SHARE` lock on `Photos` — Photobank writes during this window are only emitted by the indexer job, so schedule outside the indexer window.

3. Refresh planner statistics immediately:

   ```sql
   ANALYZE public."Photos";
   ANALYZE public."PhotoTags";
   ANALYZE public."PhotobankTags";
   ```

4. Restart the Azure Web App for Containers so the new application image is live alongside the new schema. (Schema is backward-compatible — old code can read the new indexes; new indexes are not visible to old code but cause no errors.)

## Post-deploy validation

1. Hit the endpoint via the gallery UI and confirm 200 OK + photos render.

2. Run `EXPLAIN ANALYZE` for the two representative shapes and verify index usage:

   ```sql
   EXPLAIN ANALYZE
   SELECT * FROM public."Photos"
   ORDER BY "ModifiedAt" DESC, "Id" DESC
   LIMIT 48;
   -- expect: Index Scan using "IX_Photos_ModifiedAt_Id"

   EXPLAIN ANALYZE
   SELECT * FROM public."Photos"
   WHERE LOWER("FolderPath" || '/' || "FileName") LIKE '%produkty%'
   ORDER BY "ModifiedAt" DESC, "Id" DESC
   LIMIT 48;
   -- expect: Bitmap Heap Scan ... using "IX_Photos_PathTrgm"
   ```

3. Watch Application Insights for 24 h. The 10 s "GET Photobank/GetPhotos" alert must not fire. p50 should land near 200 ms; p95 near or below 800 ms.

## Rollback

The migration is reversible:

```bash
dotnet ef database update 20260512150626 \
  --project src/Anela.Heblo.Persistence \
  --startup-project src/Anela.Heblo.API
```

`Down()` drops the GIN, `IX_Photos_ModifiedAt_Id`, and `IX_PhotoTags_TagId_PhotoId` indexes. `pg_trgm` is **not** dropped (other features may depend on it). After rollback, the application code reverts to the old query shape via redeploy of the previous image.
