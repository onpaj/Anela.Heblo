# Code Review: fix-authorization-header-logging-leak

## Summary
The implementation removes `logging.RequestHeaders.Add("Authorization");` from `AddCrossCuttingServices` exactly as specified in `spec.r1.md` (FR-1) and `design.r1.md`, replacing it with an explanatory comment matching the design's suggested wording. A new self-contained regression test (`HttpLoggingAuthorizationRedactionTests`, 4 tests) verifies the header is no longer in the allow-list while `User-Agent`, `Content-Type`, and `HttpLoggingFields.All` remain unchanged. All acceptance criteria in the task context are met.

## Review Result: PASS

### task: fix-authorization-header-logging-leak
**Status:** PASS

Verification performed:
- Diff matches the task context's Step 3 replacement exactly (line-for-line), and matches `design.r1.md`'s proposed code.
- `dotnet test ... --filter FullyQualifiedName~HttpLoggingAuthorizationRedactionTests` → 4/4 passed.
- `dotnet build Anela.Heblo.sln` → Build succeeded, 0 errors (13 pre-existing nullability warnings and one pre-existing, unrelated `AccessMatrixGen` post-build tool crash treated as a non-fatal MSBuild warning — both present on unmodified code, not introduced by this change).
- `dotnet test Anela.Heblo.sln` → 187 failures, all pre-existing and unrelated: `Anela.Heblo.Adapters.Flexi.Tests` / `Anela.Heblo.Adapters.Shoptet.Tests` integration tests require live external API credentials unavailable in this sandbox (per `docs/integrations/shoptet-api.md`), and the 102 `Anela.Heblo.Tests` failures are all `*IntegrationTests`/`*SqlShapeTests` failing with `Docker is either not running or misconfigured` (Testcontainers/PostgreSQL unavailable in this sandbox). None reference `ServiceCollectionExtensions`, HTTP logging, or `Authorization`, and none are new relative to what this sandbox would produce on unmodified code.
- Spec compliance: FR-1 (Authorization no longer in `RequestHeaders`) ✓, FR-2 (rest of logging config unchanged — `LoggingFields.All`, `User-Agent`, `Content-Type`, body limits, `SuppressHealthHttpLogging` all untouched) ✓, FR-3 (no change needed to `RequestLoggingMiddleware`, which was already correct) ✓.
- Architecture adherence: matches arch-review's Decision 1 (omission-based fix, not interceptor-based) exactly; no scope creep into Decision 2's optional consolidation, as intended.
- Surgical-change compliance (CLAUDE.md): only the two files named in the task context were touched.

## Docs to Update
(None — this is an internal logging-configuration fix with no public API, CLI, or operational-behavior change requiring documentation updates.)

## Overall Notes
No blocking issues. The regression test is well-targeted (DI-level, no host required) and will catch any future accidental re-addition of `Authorization` (or, by the same pattern, another sensitive header) to the built-in logging allow-list.
