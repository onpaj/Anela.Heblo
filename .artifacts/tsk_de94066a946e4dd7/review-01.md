# Review — Remove dead DateFrom/DateTo from GetProductMarginsRequest

## Verdict: done

## What was implemented

Per the finding's "decide which, don't leave both" directive, the chain (plan → design → architecture, re-verified across two rounds each) chose **removal** over honoring the filter, consistent with the accepted `#3486`/`#3487` precedent for the identical defect shape (bound-but-unread request parameter). The commit (`81a25c22`) removes `DateFrom`/`DateTo` from three places:

1. `GetProductMarginsRequest.cs` — deleted the two unread properties.
2. `frontend/src/api/generated/api-client.ts` — `productMargins_GetProductMargins(...)` trimmed to 7 params, `DateFrom=`/`DateTo=` query-string blocks removed.
3. `frontend/src/api/hooks/useProductMargins.ts` — `dateFrom?`/`dateTo?` params, `queryKey` entries, and call-site arguments removed.

## Verification performed this step (live re-check, not just trusting artifacts)

- Repo-wide grep for `DateFrom`/`DateTo` across the request DTO, controller, handler, MCP tool (`CatalogMcpTools.cs`), hook, and sole call site (`ProductMarginsList.tsx`) — zero stray references remain. The MCP tool's `GetProductMargins` never surfaced these params either, so no additional integration point was missed.
- `dotnet build Anela.Heblo.sln` — 0 errors (pre-existing warnings only, unrelated to this change).
- `dotnet test --filter "FullyQualifiedName~GetProductMarginsHandlerTests"` — 5/5 passed.
- `dotnet format --no-restore Anela.Heblo.sln --verify-no-changes` — clean, no formatting drift.
- `npm run build` (frontend) — compiled successfully.
- `ProductMarginsList.tsx` call site confirmed to pass exactly 7 positional args (matches trimmed hook signature) — no source break.
- Diff scope confirmed minimal: only the request DTO, one generated-client method, and the hook are touched; no unrelated regeneration drift leaked in (dev step explicitly hand-trimmed the generated file to avoid pulling in unrelated in-flight changes on the branch).

## Assessment

- **Conformance to spec**: meets the finding's requirement — the inert no-op parameter is gone from the contract, so the frontend can no longer build behavior on a filter that silently did nothing.
- **Architecture**: consistent with prior precedent in the same module, DTO-as-class rule respected, generated-client-is-derived-only discipline respected.
- **Completeness**: all call sites (controller, MCP tool, hook, sole call site, tests) checked; no orphaned references. No new tests needed — this is a pure contract-narrowing deletion with no new behavior; existing tests already cover the unaffected hardcoded 13-month window and pass unchanged.
- **Correctness**: zero-behavior-change deletion, confirmed by grep (handler never read these fields) and by full build/test/format pass.

No issues found. Approved.
