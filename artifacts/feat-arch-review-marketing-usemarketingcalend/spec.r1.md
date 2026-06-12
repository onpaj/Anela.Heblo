# Specification: Restore Type Safety in `useMarketingCalendar.ts` API Calls

## Summary
Eliminate the seven `(client as any).*` casts in `frontend/src/api/hooks/useMarketingCalendar.ts` so the generated NSwag TypeScript client's compile-time type checking once again protects the Marketing Calendar feature. The hook must continue to expose the same public API to its consumers, but every call into `ApiClient` will be fully typed, every payload will satisfy the generated request DTO interfaces, and the manual "verified at commit X" comment can be removed.

## Background
`frontend/src/api/hooks/useMarketingCalendar.ts` wraps seven generated `ApiClient` methods (`marketingCalendar_GetMarketingActions`, `_GetMarketingAction`, `_GetCalendar`, `_CreateMarketingAction`, `_UpdateMarketingAction`, `_DeleteMarketingAction`, `_ImportFromOutlook`) and casts the client to `any` on every call. The NSwag-generated `ApiClient` (in `frontend/src/api/generated/api-client.ts`) already exposes these methods with full type signatures — for example:

```ts
marketingCalendar_GetMarketingActions(
  pageNumber: number | undefined,
  pageSize: number | undefined,
  searchTerm: string | null | undefined,
  actionType: MarketingActionType | null | undefined,
  productCodePrefix: string | null | undefined,
  startDateFrom: Date | null | undefined,
  startDateTo: Date | null | undefined,
  endDateFrom: Date | null | undefined,
  endDateTo: Date | null | undefined,
  includeDeleted: boolean | undefined,
): Promise<GetMarketingActionsResponse>
```

The casts therefore disable a protection that the toolchain already provides. The author left a comment noting that argument positions are "last verified at commit 2f582c12" — proof that the team is aware the compiler can no longer catch regressions. Comparing the hook's locally-declared interfaces to the generated `ICreateMarketingActionRequest`, `IUpdateMarketingActionRequest`, and `IImportFromOutlookRequest` reveals the most likely original motivation for the cast: the hook's payload interfaces use `number` for `actionType` (the generated type is the `MarketingActionType` enum) and an inline `{ folderKey: string; folderType: number }` shape for `folderLinks` (the generated type is `MarketingFolderLinkRequest`). Resolving those mismatches restores type safety without changing runtime behavior.

This change is a refactor: no new feature, no behavioral change, no UI change. The motivation is correctness — preventing a class of silent runtime breakage when backend DTOs evolve.

## Functional Requirements

### FR-1: Remove all `as any` casts on the client in `useMarketingCalendar.ts`
Every `(client as any).marketingCalendar_*(...)` call in `frontend/src/api/hooks/useMarketingCalendar.ts` must be replaced by a directly-typed call on the `ApiClient` instance returned from `getAuthenticatedApiClient()`. No new `any`, `unknown`, or `as any` casts may be introduced anywhere in the file as a workaround.

**Acceptance criteria:**
- `grep -n "as any" frontend/src/api/hooks/useMarketingCalendar.ts` returns no matches.
- `npm run lint` and `tsc --noEmit` (i.e. the project's `npm run build`) succeed with zero errors and zero new warnings attributable to this file.
- All seven hooks (`useMarketingActions`, `useMarketingAction`, `useMarketingCalendar`, `useCreateMarketingAction`, `useUpdateMarketingAction`, `useDeleteMarketingAction`, `useImportFromOutlook`) call the generated method directly on the typed `client`.

### FR-2: Align local payload types with generated request DTOs
The local `CreateMarketingActionPayload`, `UpdateMarketingActionPayload`, and `ImportFromOutlookPayload` interfaces declare fields whose types are looser than what the generated DTOs expect (e.g. `actionType: number` vs. `MarketingActionType`, inline `folderLinks` literal vs. `MarketingFolderLinkRequest[]`). These local interfaces must be reconciled so the call site can construct a value that satisfies the generated request type without `any`.

The reconciliation must:
1. Use the generated enum type (`MarketingActionType` for `actionType`, `MarketingFolderType` for `folderType`) directly in the local interfaces.
2. Replace inline `folderLinks` shape with the generated interface (`IMarketingFolderLinkRequest` or `MarketingFolderLinkRequest[]`) — pick whichever lets the call compile without runtime construction overhead.
3. Construct an actual `CreateMarketingActionRequest` / `UpdateMarketingActionRequest` / `ImportFromOutlookRequest` instance at the call site if the method signature demands a class instance and TypeScript refuses a plain object literal. Use the generated class constructor (`new CreateMarketingActionRequest(payload)`) — these constructors accept the `I*Request` shape directly.

**Acceptance criteria:**
- The hook payload interfaces import enum and DTO types from `../generated/api-client` rather than declaring loose `number` aliases.
- Each mutation hook constructs the request object using the generated type (either via plain object satisfying the `I*Request` interface, or via the generated class constructor) — no structural casts.
- Existing callers of these hooks (components in `frontend/src/components/marketing/` and any other consumer) continue to compile without changes to their call sites, OR the spec's "Out of Scope" list explicitly notes the consumer files that must update.

### FR-3: Preserve runtime behavior of the seven hooks
The change is type-only. Every hook must produce identical HTTP requests (URL, method, query parameters, body shape, headers) as the current implementation, and return the same response objects. Query invalidation behavior in mutation hooks must be unchanged.

**Acceptance criteria:**
- Existing unit/integration/E2E coverage that exercises Marketing Calendar continues to pass without modification (other than fixing any test that itself relied on `as any`).
- Manual smoke test of the five user-visible flows passes against staging: list marketing actions, view single action, view calendar range, create action, update action, delete action, import from Outlook (`dryRun: true` and `dryRun: false`).

### FR-4: Remove the obsolete "last verified at commit" comment
Once the typed signatures enforce argument order, the comment at lines 51–53 of the current file becomes both inaccurate and unnecessary. It must be deleted as part of this change.

**Acceptance criteria:**
- The phrase "Positional args match the generated signature" no longer appears in the file.
- The replacement, if any, is one short comment per CLAUDE.md's "default to no comments" rule — preferably none, since well-typed call sites are self-documenting.

### FR-5: Verify and document the fix in the regenerate-client workflow
The hook had to be cast to `any` because at least one local interface diverged from the generated DTO. The spec must ensure this divergence cannot silently return. If `npm run build` regenerates the client, the typed call must continue to compile; if a future backend DTO change breaks the call, the TypeScript compiler must flag it.

**Acceptance criteria:**
- After re-running `npm run build` (which regenerates `frontend/src/api/generated/api-client.ts`), the hook file still compiles without `as any`.
- No documentation changes required in `docs/development/api-client-generation.md` unless the implementer discovers a generator-template issue that warranted the cast (see Open Questions).

## Non-Functional Requirements

### NFR-1: Performance
No measurable performance impact. The change is type-system only; the emitted JavaScript should be equivalent (or differ only by the removal of unnecessary type-assertion expressions, which are erased at compile time anyway). No additional runtime allocations are acceptable beyond what the generated class constructors already perform.

### NFR-2: Security
No security impact. Request/response payloads, authentication flow, and authorization checks are unchanged. The change strengthens the team's ability to catch incorrect parameter binding (which could in principle send the wrong field to the wrong query parameter) at compile time rather than runtime — a small defensive-coding improvement.

### NFR-3: Maintainability
The diff must be limited to:
- `frontend/src/api/hooks/useMarketingCalendar.ts` (primary change)
- At most: any test file that itself uses `(client as any)` on these same methods
- At most: caller components if FR-2's reconciliation requires them to pass the enum type instead of a numeric literal

No unrelated cleanup, no formatting passes, no renaming. Per CLAUDE.md "surgical changes" rule.

### NFR-4: Backwards compatibility
The public exports of the hook file (`useMarketingActions`, `useMarketingAction`, `useMarketingCalendar`, `useCreateMarketingAction`, `useUpdateMarketingAction`, `useDeleteMarketingAction`, `useImportFromOutlook`, `ImportFromOutlookResult`) must remain exported with names and call signatures compatible with current consumers. Internal payload interfaces may evolve as long as consumer call sites still compile (or are updated within this change).

## Data Model
No data model changes. The hook continues to work against the existing entities:

- **`MarketingAction`** — the calendar entry, exposed through `GetMarketingActionResponse` / `GetMarketingActionsResponse`.
- **`MarketingCalendar`** — the range view, exposed through `GetMarketingCalendarResponse`.
- **`CreateMarketingActionRequest` / `UpdateMarketingActionRequest`** — write-side DTOs; their fields (`title`, `description`, `actionType: MarketingActionType`, `startDate: Date`, `endDate?: Date`, `associatedProducts?: string[]`, `folderLinks?: MarketingFolderLinkRequest[]`) become the source of truth for the local hook payload types.
- **`ImportFromOutlookRequest` / `ImportFromOutlookResponse`** — sync DTOs; the response's `unmappedCategories?` remains normalized to `string[]` by the existing `?? []` logic.
- **Enums** — `MarketingActionType`, `MarketingFolderType` are the generated enums the hook payloads must reference.

## API / Interface Design

### Existing public hook signatures (preserved)
```ts
useMarketingActions(params?: GetMarketingActionsParams): UseQueryResult<GetMarketingActionsResponse>
useMarketingAction(id: number): UseQueryResult<GetMarketingActionResponse>
useMarketingCalendar(params: GetMarketingCalendarParams): UseQueryResult<GetMarketingCalendarResponse>
useCreateMarketingAction(): UseMutationResult<CreateMarketingActionResponse, unknown, CreateMarketingActionPayload>
useUpdateMarketingAction(): UseMutationResult<UpdateMarketingActionResponse, unknown, { id: number; request: Omit<UpdateMarketingActionPayload, "id"> }>
useDeleteMarketingAction(): UseMutationResult<DeleteMarketingActionResponse, unknown, number>
useImportFromOutlook(): UseMutationResult<ImportFromOutlookResponse, unknown, ImportFromOutlookPayload>
```

Response types now flow through from the generated client rather than being widened to `Promise<any>` by the cast.

### Internal call pattern (new)
```ts
const client = await getAuthenticatedApiClient();
return await client.marketingCalendar_GetMarketingActions(
  params.pageNumber,
  params.pageSize,
  params.searchTerm,
  params.actionType,
  params.productCodePrefix,
  params.startDateFrom,
  params.startDateTo,
  params.endDateFrom,
  params.endDateTo,
  params.includeDeleted,
);
```

For mutations that take a request DTO, prefer (in priority order):
1. A plain object satisfying the `I*Request` interface, passed directly if the generated method accepts it.
2. If TypeScript rejects the structural match (because the method signature names the class, not the interface), construct the class explicitly: `new CreateMarketingActionRequest({ ... })`. The generated constructors accept the `I*Request` shape.

### Out-of-scope interfaces
The component-facing payload interfaces (`GetMarketingActionsParams`, `GetMarketingCalendarParams`, `CreateMarketingActionPayload`, `UpdateMarketingActionPayload`, `ImportFromOutlookPayload`) remain in the hook file. Their field types tighten (`actionType` becomes `MarketingActionType`, `folderLinks` references the generated `IMarketingFolderLinkRequest`/`MarketingFolderLinkRequest`), but the field names and overall shape are preserved.

## Dependencies
- **`frontend/src/api/generated/api-client.ts`** (NSwag-generated). Source of typed `ApiClient` and request/response DTOs. Regenerated by `npm run build`.
- **`frontend/src/api/client.ts`** — exports `getAuthenticatedApiClient()` returning a typed `ApiClient`. No changes required.
- **`@tanstack/react-query`** — `useQuery`/`useMutation`. No version change required.
- **Consumer components** — anything importing the affected hooks (typically `frontend/src/components/marketing/`). They may need to pass the `MarketingActionType` enum instead of a numeric literal; the implementer must grep for usages and update them if necessary.

## Out of Scope
- Refactoring `frontend/src/api/hooks/useMarketingCalendar.ts` beyond the type-safety fix (e.g. splitting into multiple files, switching state libraries, renaming hooks).
- Auditing or fixing `(client as any)` usage in other hook files. The arch-review finding is scoped to Marketing Calendar; other hooks are addressed by separate findings/PRs if they exist.
- Changing the NSwag generator configuration or upgrading the generator version, **unless** Open Question #1 reveals a generator bug that forced the original cast.
- Backend changes to `MarketingCalendar` controller DTOs.
- Adding new tests for marketing calendar; the existing test coverage is presumed adequate to detect regressions in this refactor. New tests are written only if existing coverage is materially absent.
- Updating `docs/development/api-client-generation.md` unless FR-5's investigation surfaces a generator gotcha.

## Open Questions
None.

## Status: COMPLETE