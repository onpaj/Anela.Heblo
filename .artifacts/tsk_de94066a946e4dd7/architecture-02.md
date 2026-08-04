# Architecture review (re-verification) — Remove dead DateFrom/DateTo from GetProductMarginsRequest

## Verdict

**Approved as designed.** This is a re-verification pass over `architecture-01.md`, prompted by `plan-02.md`/`design-02.md` reaffirming the same "remove, not implement" decision. The live tree was re-read fresh in this step rather than trusting prior artifacts' quotes. Zero drift found. No changes requested; ready for implementation.

## Verification performed (live code, this step)

- `GetProductMarginsRequest.cs` — still a `class` (not record, per `CLAUDE.md`'s mandatory DTO rule), still declares only `DateTime? DateFrom` / `DateTime? DateTo` beyond the six kept properties (`ProductCode`, `ProductName`, `ProductType`, `PageNumber`, `PageSize`, `SortBy`, `SortDescending`).
- `GetProductMarginsHandler.cs` — grepped for `DateFrom`/`DateTo`: **zero matches**. Still fully dead code, not partially wired.
- `ProductMarginsController.cs` — plain `[FromQuery] GetProductMarginsRequest request` binding, no per-field logic; deleting the two properties requires no controller change.
- `useProductMargins.ts` — still the 9-parameter hook signature (7 real filters + trailing `dateFrom?: Date`, `dateTo?: Date`), both still in the `queryKey` array and passed positionally (`dateFrom || null`, `dateTo || null`) into `apiClient.productMargins_GetProductMargins(...)`.
- Generated client `api-client.ts:10809` — `productMargins_GetProductMargins(...)` signature confirmed to still carry trailing `dateFrom: Date | null | undefined, dateTo: Date | null | undefined` params, consistent with "generated, not hand-authored — will shrink on regeneration" being the correct mental model.
- `ProductMarginsList.tsx:48-56` — sole call site, confirmed still passing exactly 7 positional args (`productCodeFilter` → `sortDescending`), never `dateFrom`/`dateTo`. Source-compatible with the trimmed 7-arg hook with zero edits.
- Repo-wide search for `useProductMarginsQuery` — still exactly 3 files: the hook itself, `ProductMarginsList.tsx`, and `ProductMarginsList.test.tsx`. No other caller.
- `GetProductMarginsHandlerTests.cs` — re-read all five `new GetProductMarginsRequest` constructions (empty-constructor and object-initializer forms at lines 48, 87, 120, 156, 192): none sets `DateFrom`/`DateTo`. The suite only exercises the hardcoded `AddMonths(-13)` window (asserted explicitly at lines 31/36/37). Confirms the deletion is genuinely zero-behavior-change against the existing test surface.

Every claim in `design-02.md`/`plan-02.md` checks out byte-for-byte against the current tree — nothing landed or drifted between steps.

## Alignment with existing patterns and invariants

- **DTOs are classes, never records** (`CLAUDE.md`): unaffected — `GetProductMarginsRequest` stays a class; the change is a property deletion, not a shape change.
- **Vertical Slice / MediatR contract ownership**: request DTO stays in `UseCases/GetProductMargins/`, no relocation.
- **Generated-client discipline**: `api-client.ts` is correctly treated as derived-only; the only hand-touched files are the request DTO and the hook, matching precedent.
- **API hooks use absolute URLs** (`CLAUDE.md`): not implicated — this change touches parameter count, not URL construction.
- **Precedent consistency**: `#3486` (`TopProductCount`) and `#3487` (`IncludeDetailedBreakdown`) both resolved the identical defect shape (bound-but-unread request parameter) via removal within this same module. This change is the third instance of the same pattern — removal keeps the module internally consistent rather than introducing a one-off exception.
- **No DB/domain impact**: `CatalogAggregate.Margins` and the background `RefreshMarginData` window remain untouched and out of scope — correctly recognized as structurally incompatible with a request-time range without a materially larger redesign (pre-aggregated `Averages` don't carry per-request date-filtering hooks).

## Risks and mitigations (unchanged from architecture-01.md, re-confirmed still valid)

1. **Frontend build/typecheck breakage if any caller passes `dateFrom`/`dateTo` positionally.** Mitigation: re-confirmed only one call site exists, and it uses 7 args today — trimming the hook to 7 params is source-compatible with zero edits there. Final gate: `npm run build`.
2. **Client regeneration ordering.** Sequence must stay: backend DTO change → regenerate OpenAPI client → update hook. Doing the hook edit first would type-error against the stale (still 9-param) generated client.
3. **No orphaned validation code.** No `IValidator<GetProductMarginsRequest>` or other validation touches these fields — confirmed again this step; nothing else to clean up.

No prerequisites are blocking implementation. The design can proceed exactly as written in `design-02.md`.
