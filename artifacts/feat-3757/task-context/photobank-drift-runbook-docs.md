### task: photobank-drift-runbook-docs

**Files:**
- Modify: `docs/development/setup.md` (the existing "Diagnostic SQL for suspected schema drift" section)
- Modify: `memory/gotchas/ef-migration-codebase-drift.md` (the existing "Known limitation" section)

- [ ] **Step 1: Extend the diagnostic-SQL section in `docs/development/setup.md`**

Find the existing "Diagnostic SQL for suspected schema drift" section (introduced for the `DqtRuns`
rename incident — table-existence drift). Immediately after its closing "These diagnostic queries
are read-only and safe to run against any environment." line, append this new subsection:

```markdown
### Photobank column-type drift (distinct from the table-rename case above)

The `DqtRuns` case above is a *table-existence* drift (a table was renamed). Photobank's regression
(#3757, following #3444/#3330) is a *column-type* drift instead: a `DateTime` column mapped as
`timestamp` (without time zone) in the EF model can still be `timestamp with time zone` physically,
if its converting migration was never applied to a given environment. Use this query pair instead of
the table-existence pair above when investigating a repeat of
`System.ArgumentException: Cannot write DateTime with Kind=Unspecified to PostgreSQL type 'timestamp with time zone'`
from `PhotobankIndexJob`:

Migration history check:

```sql
SELECT "MigrationId", "ProductVersion"
FROM "__EFMigrationsHistory"
WHERE "MigrationId" LIKE '%AlignPhotoTimestampsWithoutTimeZone%'
   OR "MigrationId" LIKE '%AlignPhotobankIndexRootTimestampWithoutTimeZone%'
ORDER BY "MigrationId";
```

Physical column-type check:

```sql
SELECT table_name, column_name, data_type
FROM information_schema.columns
WHERE table_schema = 'public'
  AND ((table_name = 'Photos' AND column_name IN ('TakenAt','IndexedAt','ModifiedAt','LastAutoTaggedAt'))
    OR (table_name = 'PhotobankIndexRoots' AND column_name IN ('CreatedAt','LastIndexedAt'))
    OR (table_name = 'PhotoTags' AND column_name = 'CreatedAt'));
```

Interpret: every listed migration present in history AND every listed column reporting
`timestamp without time zone` → code and DB are consistent (the exception, if still occurring, is
not a schema-drift issue — look at the actual failing parameter in a live trace instead). Any
migration missing from history, or any column still reporting `timestamp with time zone` → drift;
apply the missing migration via the standard manual procedure. This exact check is also automated at
runtime by `PhotobankSchemaHealthCheck` under `GET /health/ready` (tags `ready`, `db`, `schema`) —
prefer checking that endpoint first before running this SQL by hand.
```

- [ ] **Step 2: Update the "Known limitation" note in `memory/gotchas/ef-migration-codebase-drift.md`**

Find the existing "Known limitation of the safeguard" section (currently ends with: "Broader
coverage is tracked as a follow-up; do not assume the probe protects against drift on any other
entity."). Append this sentence to that section (do not remove or rewrite the existing text — only
append):

```markdown

Photobank's `DateTime` columns (`Photos`, `PhotobankIndexRoots`, `PhotoTags` — see #3757) are now
covered by a sibling safeguard, `PhotobankSchemaHealthCheck` (registered as `photobank-schema` under
`/health/ready`), for the column-type-drift variant of this failure class (as opposed to this file's
own table-existence variant). Other tables remain uncovered.
```

- [ ] **Step 3: Commit**

```bash
git add docs/development/setup.md memory/gotchas/ef-migration-codebase-drift.md
git commit -m "docs(photobank): extend schema-drift diagnostic runbook to cover Photobank column-type drift"
```

---
