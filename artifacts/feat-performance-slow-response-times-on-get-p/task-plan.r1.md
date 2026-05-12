# Photobank GetTags Performance Fix — Final Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate the per-tag correlated `SELECT COUNT(*)` subquery in `PhotobankRepository.GetTagsWithCountsAsync` by rewriting it as a `GroupJoin` + `LEFT JOIN`/`GROUP BY`, add `.AsNoTracking()`, and lock the new SQL shape down with an integration test against a real PostgreSQL container.

**Architecture:** Single repository-method rewrite inside the existing Photobank vertical slice. The cache wrapper, DI registration, handler cache-aside logic, mutating-handler invalidation, options binding, `IX_PhotoTags_TagId` index, model snapshot entry, and `appsettings.json` section are already in place at HEAD (`9dfba28f`). The only behavior delta is the SQL the query emits.

**Tech Stack:** .NET 8, EF Core 8 + Npgsql, xUnit + FluentAssertions, Testcontainers.PostgreSql 3.6.0 (already a test dependency).

---

## State at HEAD — Read This Before Starting

The architecture review's "what's missing" list is **partly wrong**. I verified each claim against the worktree at commit `9dfba28f`:

| Arch review claim | Actual state |
|---|---|
| ❌ `t.PhotoTags.Count` still in repository | ✅ **Confirmed broken.** `PhotobankRepository.cs:141-148` uses `Select(t => new TagCount(t.Id, t.Name, t.PhotoTags.Count))`. EF Core 8 emits this as a correlated subquery per tag row. **This is the only real perf bug remaining.** |
| ❌ No migration `IX_PhotoTags_TagId` | ❌ **FALSE.** The index is created by `20260424122851_AddPhotobankTables.cs:141-145` — EF auto-generated it as the FK shadow index for `PhotoTag.TagId`. Do **not** create a duplicate migration. |
| ❌ `PhotoTagConfiguration.HasIndex(x => x.TagId)` missing | ❌ **FALSE.** `PhotoTagConfiguration.cs:16-19` configures `.HasForeignKey(x => x.TagId)`, which is what causes EF to emit the index. Snapshot at `ApplicationDbContextModelSnapshot.cs:2292` already has `b.HasIndex("TagId")`. No change needed. |
| ❌ No `AsNoTracking()` | ✅ **Confirmed missing.** Adding it is defensive — current `Select` projects to a non-entity record so `ChangeTracker.Entries<Tag>()` is already empty, but the spec's FR-4 says "no tracking", so include it. |
| ❌ `appsettings.json` lacks `Photobank:TagsCache:TtlSeconds` | ❌ **FALSE.** Already present at `backend/src/Anela.Heblo.API/appsettings.json:173-175`. |

Everything else from the spec (cache wrapper, options, DI, handler logic, mutating-handler invalidation, `TagWithCountDto` with `init` setters, `TagCount` domain record, repository signature, logging) is implemented and tested at HEAD.

**Net remaining work:** rewrite one method body, lock down the SQL shape with one new integration test, optionally tighten the existing in-memory test to assert no-tracking, build, format, commit.

---

## File Structure

**Modified files (1):**

- `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankRepository.cs` — rewrite `GetTagsWithCountsAsync` to `GroupJoin` + `AsNoTracking`.

**New files (1):**

- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankRepositoryGetTagsSqlShapeTests.cs` — Testcontainers.PostgreSql integration test that captures emitted SQL via `IDbCommandInterceptor` and asserts the query is a single `LEFT JOIN`/`GROUP BY` statement (not a correlated subquery).

**Existing test files that will be touched (additions, not rewrites):**

- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankRepositoryGetTagsTests.cs` — add one `ChangeTracker.Entries<Tag>()` assertion test (FR-4 lock-down).

No other files change. No migration, no configuration, no DI, no handler, no contract change.

---

## Tasks

### Task 1: Add the SQL shape integration test (RED)

**Goal:** Lock down the SQL shape: exactly one command, no correlated `SELECT COUNT(*)` subquery, `LEFT JOIN` + `GROUP BY` present. The test must fail against the current correlated-subquery implementation.

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankRepositoryGetTagsSqlShapeTests.cs`

- [ ] **Step 1: Write the new integration test file**

```csharp
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank;
using Anela.Heblo.Domain.Features.Photobank;
using Anela.Heblo.Persistence;
using DotNet.Testcontainers.Configurations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

[Trait("Category", "Integration")]
public class PhotobankRepositoryGetTagsSqlShapeTests : IAsyncLifetime
{
    static PhotobankRepositoryGetTagsSqlShapeTests()
    {
        // Required on macOS with Podman: the Ryuk ResourceReaper container
        // cannot bind to the Docker socket and throws a NullReferenceException.
        TestcontainersSettings.ResourceReaperEnabled = false;
    }

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .Build();

    private readonly CapturingCommandInterceptor _interceptor = new();
    private ApplicationDbContext _context = null!;
    private PhotobankRepository _repository = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Minimal schema — only the two tables the query touches, no FKs to keep seeding simple.
        await using (var conn = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE public."PhotobankTags" (
                    "Id"   serial NOT NULL PRIMARY KEY,
                    "Name" varchar(100) NOT NULL
                );
                CREATE UNIQUE INDEX "IX_PhotobankTags_Name" ON public."PhotobankTags" ("Name");

                CREATE TABLE public."PhotoTags" (
                    "PhotoId"   integer NOT NULL,
                    "TagId"     integer NOT NULL,
                    "Source"    varchar(20) NOT NULL,
                    "CreatedAt" timestamp NOT NULL,
                    CONSTRAINT "PK_PhotoTags" PRIMARY KEY ("PhotoId", "TagId")
                );
                CREATE INDEX "IX_PhotoTags_TagId" ON public."PhotoTags" ("TagId");

                INSERT INTO public."PhotobankTags" ("Name") VALUES ('summer'), ('winter'), ('orphan');
                INSERT INTO public."PhotoTags" ("PhotoId","TagId","Source","CreatedAt") VALUES
                    (1, 1, 'Manual', now()),
                    (2, 1, 'Manual', now()),
                    (1, 2, 'Manual', now());
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .AddInterceptors(_interceptor)
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new PhotobankRepository(_context);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task GetTagsWithCountsAsync_EmitsExactlyOneSqlCommand()
    {
        _interceptor.Reset();

        await _repository.GetTagsWithCountsAsync(CancellationToken.None);

        _interceptor.Commands.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetTagsWithCountsAsync_UsesLeftJoinAndGroupBy_NotCorrelatedSubquery()
    {
        _interceptor.Reset();

        await _repository.GetTagsWithCountsAsync(CancellationToken.None);

        var sql = _interceptor.Commands.Single();
        sql.Should().Contain("LEFT JOIN", "the rewrite should join PhotoTags rather than scan it per tag");
        sql.Should().Contain("GROUP BY", "counts should be produced by a single GROUP BY aggregation");
        sql.Should().NotMatchRegex(
            @"\(\s*SELECT\s+COUNT\s*\(",
            "a correlated COUNT subquery is the perf bug we just removed");
    }

    private sealed class CapturingCommandInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = new();

        public void Reset() => Commands.Clear();

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
```

- [ ] **Step 2: Run the new test to verify it FAILS on the current implementation**

Run from the worktree root:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~PhotobankRepositoryGetTagsSqlShapeTests" \
  --logger "console;verbosity=normal"
```

Expected: `GetTagsWithCountsAsync_UsesLeftJoinAndGroupBy_NotCorrelatedSubquery` **FAILS** because the current query emits a correlated `(SELECT COUNT(*) FROM "PhotoTags" AS "p" WHERE "p"."TagId" = "t"."Id")` subquery, which matches the regex we explicitly forbid and contains no `LEFT JOIN`/`GROUP BY`.

The single-command test (`GetTagsWithCountsAsync_EmitsExactlyOneSqlCommand`) is expected to PASS already — correlated subqueries are still one SQL statement. Keep it; it pins down the contract.

If Docker / Podman isn't available locally, the test will be skipped at the container start step. The CI / staging-validation path will exercise it. Note skipping in your status update if applicable.

- [ ] **Step 3: Do not commit yet — proceed to Task 2.**

---

### Task 2: Rewrite `GetTagsWithCountsAsync` (GREEN)

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankRepository.cs:141-148`

- [ ] **Step 1: Replace the method body**

Old body (already in file, lines 141-148):

```csharp
public async Task<IReadOnlyList<TagCount>> GetTagsWithCountsAsync(CancellationToken cancellationToken)
{
    return await _context.PhotobankTags
        .Select(t => new TagCount(t.Id, t.Name, t.PhotoTags.Count))
        .OrderByDescending(x => x.Count)
        .ThenBy(x => x.Name)
        .ToListAsync(cancellationToken);
}
```

New body:

```csharp
public async Task<IReadOnlyList<TagCount>> GetTagsWithCountsAsync(CancellationToken cancellationToken)
{
    return await _context.PhotobankTags
        .GroupJoin(
            _context.PhotoTags,
            t => t.Id,
            pt => pt.TagId,
            (t, pts) => new TagCount(t.Id, t.Name, pts.Count()))
        .OrderByDescending(x => x.Count)
        .ThenBy(x => x.Name)
        .AsNoTracking()
        .ToListAsync(cancellationToken);
}
```

Use Edit with `old_string` matching the full old body (including signature line) so the change is unambiguous.

- [ ] **Step 2: Run the SQL shape integration test to verify it PASSES now**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~PhotobankRepositoryGetTagsSqlShapeTests" \
  --logger "console;verbosity=normal"
```

Expected: both tests PASS. The generated PostgreSQL statement should look like:

```sql
SELECT t."Id", t."Name", COUNT(pt0."TagId")::int AS "Count"
FROM "PhotobankTags" AS t
LEFT JOIN "PhotoTags" AS pt0 ON t."Id" = pt0."TagId"
GROUP BY t."Id", t."Name"
ORDER BY COUNT(pt0."TagId")::int DESC, t."Name"
```

(Exact column ordering / casts may vary slightly across Npgsql versions; only the substring assertions above are contractual.)

- [ ] **Step 3: Run the existing in-memory repository tests to verify no behavioral regression**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~PhotobankRepositoryGetTagsTests" \
  --logger "console;verbosity=normal"
```

Expected: all four existing tests pass (`ReturnsAllTagsIncludingOrphansWithZeroCount`, `OrdersByCountDescThenNameAsc`, `ReturnsProjectionsNotEntities`, `ReturnsTagCountRecord`).

**Note on the in-memory provider:** `GroupJoin` is supported by EF Core's InMemory provider and produces the same logical results as PostgreSQL for this query, so these tests remain a valid regression check.

- [ ] **Step 4: Do not commit yet — proceed to Task 3.**

---

### Task 3: Lock down FR-4 (no tracking) with an explicit assertion

**Goal:** The spec's FR-4 says `ChangeTracker.Entries<Tag>()` must be empty after the handler executes. The current code already satisfies this (projection to a record), but the lack of an explicit test makes a future regression easy. Add one assertion to the existing in-memory test file.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankRepositoryGetTagsTests.cs`

- [ ] **Step 1: Add a new test method to the existing class**

Append the following test method inside the `PhotobankRepositoryGetTagsTests` class (immediately before the closing `}` of the class, after `GetTagsWithCountsAsync_ReturnsTagCountRecord`):

```csharp
    [Fact]
    public async Task GetTagsWithCountsAsync_DoesNotTrackTagEntities()
    {
        _ = await _repository.GetTagsWithCountsAsync(CancellationToken.None);

        _context.ChangeTracker.Entries<Tag>().Should().BeEmpty(
            "FR-4 requires the read path to project without entity tracking");
    }
```

- [ ] **Step 2: Run the test**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~PhotobankRepositoryGetTagsTests.GetTagsWithCountsAsync_DoesNotTrackTagEntities" \
  --logger "console;verbosity=normal"
```

Expected: PASS. Projection to `TagCount` already keeps `ChangeTracker` empty; `AsNoTracking()` cements it.

- [ ] **Step 3: Do not commit yet — proceed to Task 4.**

---

### Task 4: Validate the full Photobank test suite + build

**Goal:** Confirm no other Photobank tests regressed, the solution builds clean, and formatting is consistent.

- [ ] **Step 1: Run the full Photobank test slice**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Features.Photobank" \
  --logger "console;verbosity=normal"
```

Expected: every test passes. The cache-hit/miss tests (`PhotobankTagsCacheTests`), invalidation tests (`PhotobankTagsCacheInvalidationTests`), handler tests (`GetTagsHandlerTests`), and the two repository tests files above must all be green.

- [ ] **Step 2: Build the backend solution**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: build succeeds with zero errors. Warnings are acceptable only if they existed before this change.

- [ ] **Step 3: Run `dotnet format` and confirm the diff is empty (or trivial whitespace)**

```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```

If it reports changes, run without `--verify-no-changes` and inspect the resulting diff before committing.

- [ ] **Step 4: Do not commit yet — proceed to Task 5.**

---

### Task 5: Commit and prepare the manual-migration note

**Goal:** Single conventional commit. Update release notes / docs to make the manual-migration story explicit, even though the index already exists in production — the deployment runbook should mention that the index was already provisioned by `20260424122851_AddPhotobankTables.cs` so the on-call engineer doesn't go looking for one.

- [ ] **Step 1: Confirm `IX_PhotoTags_TagId` is already on production**

This is a verification step, not a code change. Connect to staging (or check the latest production migration applied via the manual migration log noted in `CLAUDE.md`) and verify `IX_PhotoTags_TagId` exists on `public."PhotoTags"`. Expected query:

```sql
SELECT indexname FROM pg_indexes
WHERE schemaname = 'public' AND tablename = 'PhotoTags';
```

Expected output includes `IX_PhotoTags_TagId`. If it is missing on staging or production (because `20260424122851_AddPhotobankTables` was never applied there), stop and flag this — it is a deployment gap, not a code gap.

- [ ] **Step 2: Append a release-notes line**

Look for `docs/release-notes.md` or the latest unreleased section under `docs/`. If a release-notes file exists, append:

```markdown
- perf(photobank): rewrite `GET /api/photobank/tags` query as `LEFT JOIN`/`GROUP BY` (was a per-tag correlated `SELECT COUNT(*)` subquery). No migration required — `IX_PhotoTags_TagId` was already created by the initial Photobank migration `20260424122851_AddPhotobankTables.cs`.
```

If no release-notes file is present, skip this step. Do not create one for a single line.

- [ ] **Step 3: Stage and commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankRepository.cs \
        backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankRepositoryGetTagsTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankRepositoryGetTagsSqlShapeTests.cs

# Add release notes only if Step 2 modified them:
# git add docs/release-notes.md

git commit -m "$(cat <<'EOF'
perf(photobank): replace correlated tag-count subquery with GROUP BY

GetTagsWithCountsAsync was emitting a correlated SELECT COUNT(*) per tag
row. Rewrite as GroupJoin + AsNoTracking so PostgreSQL produces a single
LEFT JOIN/GROUP BY backed by the existing IX_PhotoTags_TagId index.

Lock the SQL shape down with a Testcontainers.PostgreSql integration
test (CapturingCommandInterceptor) and add a ChangeTracker assertion to
the in-memory repository tests for FR-4.
EOF
)"
```

- [ ] **Step 4: Verify the commit landed cleanly**

```bash
git status
git log -1 --stat
```

Expected: working tree clean, single commit with three (or four, if release-notes touched) files changed.

---

## Self-Review

**Spec coverage:**

- FR-1 (single SQL statement, ordering, response shape, zero-count tags): covered by Task 1's `EmitsExactlyOneSqlCommand` + `UsesLeftJoinAndGroupBy_NotCorrelatedSubquery`, plus existing in-memory tests in `PhotobankRepositoryGetTagsTests` (`ReturnsAllTagsIncludingOrphansWithZeroCount`, `OrdersByCountDescThenNameAsc`).
- FR-2 (index): already satisfied in production by `20260424122851_AddPhotobankTables.cs:141-145`. No code change required. Task 5 includes a verification step against staging/production.
- FR-3 (cache with explicit invalidation): already implemented at HEAD; covered by `PhotobankTagsCacheInvalidationTests.cs`. No new work.
- FR-4 (projection, no tracking): rewrite includes `.AsNoTracking()` (Task 2); explicit `ChangeTracker.Entries<Tag>()` assertion added in Task 3.
- FR-5 (logging on cache miss): already in `GetTagsHandler` at HEAD. No new work.
- NFR-1 (perf budgets): can only be measured end-to-end on staging; the SQL shape lock-down is the proxy in code. Manual confirmation step belongs to the staging rollout, not this plan.
- NFR-2 (security / parameterization): EF Core parameterizes by default; no change to that property.
- NFR-3 (backward compat): response contract untouched. No OpenAPI regeneration needed.
- NFR-4 (testability, 80% coverage): all four touched test files exist; new test adds coverage rather than reducing it.

**Placeholder scan:** No `TBD`, no `implement later`, every code block contains real code, every command has expected output.

**Type consistency:** `TagCount(int Id, string Name, int Count)` is used identically in old and new repository bodies and in tests. `IPhotobankRepository.GetTagsWithCountsAsync` signature unchanged.

**Risk if Docker is unavailable in dev:** the new integration test in Task 1 will fail to start the container. Mitigation: the existing in-memory tests still exercise the projection's correctness (Task 2 Step 3, Task 4 Step 1). The SQL-shape regression test exists to catch future EF behavior changes; CI / staging will run it with Docker available.
