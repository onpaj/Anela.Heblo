# Review: Remove hidden `DateTime.UtcNow` from `RecurringJobConfiguration`

## Verdict: done

## Checked

- **Entity purity** — `RecurringJobConfiguration.cs`: verified all five `DateTime.UtcNow` call sites (constructor + `Enable`/`Disable`/`UpdateCronExpression`/`UpdateConfiguration`) are gone, replaced by an explicit trailing `DateTime` parameter (`lastModifiedAt`/`modifiedAt`). No default values on the new parameters, so a missed call site would be a compile error, not a silent fallback — matches architecture-01.md's mitigation.
- **Handlers own the clock** — `UpdateRecurringJobStatusHandler` and `UpdateRecurringJobCronHandler` both inject `TimeProvider` via constructor (with null guard), compute `now` exactly once per `Handle`, and pass it into the entity mutator. Matches the existing `GetRecurringJobHandler` pattern as intended.
- **Seeder** — `RecurringJobSeeder` passes `DateTime.UtcNow` inline at both call sites, no `TimeProvider` injection, per the finding's own explicit guidance and the approved design.
- **Test double convention** — both handler test classes use `Mock<TimeProvider>` fixed to a constant `DateTimeOffset`, consistent with the codebase's existing convention (no new `FakeTimeProvider` dependency introduced).
- **The concrete acceptance criterion** — `UpdateRecurringJobStatusHandlerTests.cs:84` was tightened from `BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5))` to exact `result.LastModifiedAt.Should().Be(FixedUtcNow.UtcDateTime)`. This was the explicit, load-bearing proof point called out in both plan and architecture docs, and it's present.
- **Call-site completeness** — grepped all of `backend/` for `new RecurringJobConfiguration(`, `.Enable(`, `.Disable(`, `.UpdateCronExpression(`, `.UpdateConfiguration(`: exactly the 12 files in the architecture inventory are touched, and every call site now supplies the trailing timestamp argument (constructor calls are 8-arg, matching the new signature). No stale 7-arg calls remain that would fail to compile.
- **Scope discipline** — no HTTP/API contract, DB schema, or repository interface changes; `RecurringJobsControllerTests.cs` correctly left untouched (builds DTOs directly).

## Notes (non-blocking)

- `dotnet` SDK is unavailable in this sandbox, so the actual build/test run could not be executed here (confirmed by trying `which dotnet` — not found). The dev step's manual verification (argument-count parsing, brace-balance checks, full-file reads) is a reasonable substitute given the constraint, but `dotnet build && dotnet test --filter "FullyQualifiedName~BackgroundJobs"` should still be run before merge, as already flagged in development-01.md.
