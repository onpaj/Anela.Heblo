# Architecture review — Remove dead DateFrom/DateTo from GetProductMarginsRequest

## Verdict

**Approved as designed.** The design in `design-01.md` matches the live codebase exactly, follows the two directly analogous precedents already merged in this module, and respects every relevant invariant in `docs/architecture/development_guidelines.md`. No changes requested.

## Verification performed

Re-read the actual files rather than trusting the plan/design artifacts' quotes, to confirm nothing drifted since planning:

- `GetProductMarginsRequest.cs` — confirmed `DateTime? DateFrom` / `DateTime? DateTo` are the only two properties beyond the six the design keeps; class (not record), matching the project's mandatory DTO convention (`CLAUDE.md` → "DTOs are classes, never C# records").
- `GetProductMarginsHandler.cs` — grepped for `DateFrom`/`DateTo`: zero matches. Confirms the parameters are truly dead code, not a partially-wired feature.
- `ProductMarginsController.cs` — plain `[FromQuery] GetProductMarginsRequest request` binding with no per-field logic, so deleting the two properties requires no controller change, consistent with the design's claim.
- `useProductMargins.ts` — confirmed current 9-parameter signature (7 real + `dateFrom`/`dateTo` trailing), and that `dateFrom`/`dateTo` flow into both the React Query `queryKey` and the generated client call, exactly as the design describes.
- `ProductMarginsList.tsx:48-56` — confirmed the sole call site passes exactly 7 positional args, never `dateFrom`/`dateTo`. It is source-compatible with the trimmed 7-parameter hook signature with zero edits.
- Repo-wide grep for `productMargins_GetProductMargins` / `useProductMarginsQuery` — only 4 hits total: the hook itself, `api-client.ts` (generated), `ProductMarginsList.tsx`, and its test. No other caller anywhere in `frontend/src`. Confirms FR-5.
- `ProductMarginsList.test.tsx` — mocks `useProductMarginsQuery`'s **return value** via `jest.MockedFunction`; it does not assert on call arguments/arity. The design's "verify at implementation time" caveat about the test mock is real but low-risk — the mock will not break on the signature change, only need to typecheck.
- `GetProductMarginsHandlerTests.cs` — grepped for `DateFrom`/`DateTo`/`new GetProductMarginsRequest`: all five request constructions use empty constructor or object-initializer syntax that never sets `DateFrom`/`DateTo`. The suite exercises only the hardcoded 13-month window. Confirms FR-2 — the removal is a genuine zero-behavior-change deletion, not just "probably fine."
- No C# integration test uses the generated `AnelaHebloApiClient` against `ProductMargins`, so the backend client's regeneration carries no additional test-breakage risk.
- `docs/architecture/development_guidelines.md` "Contracts and DTOs Rules" — module-owned DTOs, no API-project ownership, no client-settable server-resolved fields. None of these are implicated by a pure field deletion; no violation either before or after.
- `docs/development/api-client-generation.md` — confirms client regeneration is a standard NSwag build-triggered step (Debug post-build for the C# client; presumably an equivalent frontend step per the plan), matching FR-4's "regenerated, not hand-edited" requirement.
- Precedent commits `99dd69e5` (#3486) and `68206106` (#3487) — confirmed both exist in `git log` and are titled exactly as cited: dead-parameter removals from sibling `ProductMargins`-family endpoints (`GetProductMarginSummary`, `GetMarginReport`). This design's three-component shape (request DTO → generated client → hook) mirrors that precedent's diff shape.

## Alignment with existing patterns

- **Vertical Slice / MediatR contract ownership**: request DTO lives in the module's `UseCases/GetProductMargins/` folder, unchanged location — the fix only trims fields, doesn't relocate anything.
- **Generated-client discipline**: the design correctly treats `api-client.ts` as derived, not hand-edited — the only two touched-by-hand files are the request DTO and the hook, exactly matching how #3486/#3487 were structured.
- **No DB/domain impact**: `CatalogAggregate.Margins` and the background `RefreshMarginData` window are explicitly out of scope and untouched — correctly identified as architecturally separate (pre-aggregated data can't retroactively honor a request-time range without a materially larger redesign, which the plan correctly declined to take on unrequested).

## Risks and mitigations

1. **Frontend build/typecheck could fail if any file passes `dateFrom`/`dateTo` positionally past the new end of the hook signature.** Mitigation: already exhaustively grepped — only one call site exists and it uses 7 args today. Re-run `npm run build` after the edit as the final gate (already in the validation plan).
2. **Client regeneration ordering.** The design correctly sequences: change backend DTO → regenerate OpenAPI spec/clients → update hook. Doing it out of order (e.g., hand-editing the hook before regenerating) would cause a transient type mismatch against the stale generated client. Not a design flaw, just an implementation-order note worth keeping explicit in the impl step.
3. **None of the removed query-string values are validated/parsed anywhere else** (e.g., no `IValidator<GetProductMarginsRequest>` for these fields was found) — no orphaned validation code to clean up.

No prerequisites are blocking implementation; the design can proceed as written.
