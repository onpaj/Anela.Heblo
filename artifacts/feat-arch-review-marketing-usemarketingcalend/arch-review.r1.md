I have what I need. The root cause is an integer-versus-string-enum mismatch that the spec touches but doesn't fully resolve — let me write the review.

# Architecture Review: Restore Type Safety in `useMarketingCalendar.ts`

## Skip Design: true

## Architectural Fit Assessment

The feature is a localized refactor inside the standard frontend data-access layer (`frontend/src/api/hooks/`). It does not introduce a new component, module boundary, or cross-cutting concern. It removes a deviation (`as any`) from the project's established pattern: generated NSwag client + thin TanStack Query hook wrappers. As such it improves architectural fit, not redesigns it.

The spec is *underspecified in one critical respect*: it treats the cast as a positional-arguments problem and assumes "preserve runtime behavior" + "use `MarketingActionType` directly" are independently satisfiable. They aren't, because:

- Generated `MarketingActionType` is a **string enum** (`"SocialMedia" | "Blog" | ... | "Meeting"`), backed by `JsonStringEnumConverter` globally in `backend/src/Anela.Heblo.API/Program.cs:142`.
- The current hook serializes `actionType` as an **integer** (0,1,2,3,4,99) because `MarketingActionModal.tsx:188` and `MarketingCalendarPage.tsx:217` build numeric payloads (via `ACTION_TYPE_TO_INT` and inline integer constants). The `(client as any)` cast is what lets a `number` flow into a parameter typed as the string enum.
- The backend's `JsonStringEnumConverter` happens to accept both names and numeric values on input, so the current numeric-on-the-wire format works *by tolerance, not by contract*. Responses are always string-form.

The same dynamic applies, on a smaller scale, to `folderLinks.folderType` (`MarketingFolderType` is a string enum; the modal builds integers 0..3,99 via `FOLDER_TYPE_OPTIONS`).

Removing the cast forces the team to pick a wire format. This is an architectural choice that belongs in the review, not in the implementer's hands at coding time.

## Proposed Architecture

### Component Overview
```
┌─────────────────────────────────────────────────────────────┐
│ Components (MarketingActionModal, MarketingCalendarPage,    │
│             MobileAgendaView, MarketingActionFilters)       │
│                                                             │
│  - Build payload using MarketingActionType (string enum)    │
│  - Build folderLinks using MarketingFolderType (string enum)│
└──────────────────────┬──────────────────────────────────────┘
                       │ typed enum values
                       ▼
┌─────────────────────────────────────────────────────────────┐
│ useMarketingCalendar.ts (TanStack Query hook layer)         │
│                                                             │
│  - Local payload interfaces re-use generated I*Request      │
│    field types (no looser `number` aliases)                 │
│  - Hook body calls client.marketingCalendar_*(...) directly │
│    against generated, typed signatures                      │
└──────────────────────┬──────────────────────────────────────┘
                       │ ICreateMarketingActionRequest /
                       │ IUpdateMarketingActionRequest /
                       │ IImportFromOutlookRequest (typed)
                       ▼
┌─────────────────────────────────────────────────────────────┐
│ Generated ApiClient (frontend/src/api/generated/api-client) │
│  - HTTP POST /api/MarketingCalendar/...                     │
│  - actionType serialized as STRING ("SocialMedia", etc.)    │
└──────────────────────┬──────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────┐
│ Backend (JsonStringEnumConverter, already accepts strings)  │
└─────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Wire format for `actionType` and `folderType`
**Options considered:**
- **A.** Standardize on the string enum end-to-end. Update callers to pass `MarketingActionType` / `MarketingFolderType` values; delete `ACTION_TYPE_TO_INT` (no longer used); change `MarketingActionModal`'s form state from `number` to the string enum.
- **B.** Keep callers passing integers; do `int → MarketingActionType` mapping inside the hook before invoking the generated method. Local payload types stay `number`; hook performs the translation.
- **C.** Keep callers passing integers; expose an adapter helper (e.g. `actionTypeFromInt(n): MarketingActionType`) that callers invoke before calling the hook.

**Chosen approach:** **A.** Standardize on string enums; update callers.

**Rationale:**
- The generated client, backend OpenAPI contract, list responses, and existing `MarketingActionFilters`/`marketingActionTypeLabels.ts` already use the string enum. Integer codes are a legacy island in `MarketingActionModal`.
- Option B contradicts the spec's FR-2.1 ("use the generated enum type directly in local interfaces"). It also relocates a mapping table into the hook, which is the wrong layer — the hook should be a transport-thin wrapper.
- Option C preserves the integer island, which is the very thing that made `as any` necessary. Future contributors will keep building integer payloads and the mismatch returns.
- Option A is a one-time consolidation that aligns the codebase with the contract the backend already publishes, and (architecturally) eliminates the dual representation that caused the bug class.
- The wire body changes from `{"actionType": 0}` to `{"actionType": "SocialMedia"}`. This is **semantically identical** to the backend (`JsonStringEnumConverter` accepts both), and it makes request/response symmetric (responses are already strings). This is a *normalization*, not a behavior change — but it is a wire change, so it must be tested explicitly (smoke test all five flows on staging).

#### Decision 2: Plain object vs. generated class instance at the call site
**Options considered:**
- **A.** Pass a plain object literal that satisfies `ICreateMarketingActionRequest` / `IUpdateMarketingActionRequest` / `IImportFromOutlookRequest`.
- **B.** Construct `new CreateMarketingActionRequest({...})` etc.

**Chosen approach:** **A** if the generated method accepts the structural type; **B** only if TypeScript refuses A.

**Rationale:** Looking at the generated signatures (`api-client.ts:7458`, `7554`, `7702`), the parameter is typed as the *class* (`CreateMarketingActionRequest`, etc.), not the `I*Request` interface. TypeScript structural typing will accept a literal whose shape matches the class's public fields, **as long as the literal does not include any class-only members** (none of these classes have extra public members beyond fields). The serialized body is built by the generated method via `body_ = JSON.stringify(request)`, which on a plain object produces exactly the same wire shape as on a class instance with the same fields. Prefer plain objects — they avoid the allocation, the `init`/`fromJS`/`toJSON` boilerplate, and the conceptual question of "why am I instantiating a class to throw it at JSON.stringify." Fall back to `new …Request(payload)` only if TypeScript rejects the literal.

#### Decision 3: Caller update scope
**Options considered:**
- **A.** Update only the hook; let callers continue passing integers and patch with mapping in the hook.
- **B.** Update the hook *and* the two consumer files that build integer payloads (`MarketingActionModal.tsx`, `MarketingCalendarPage.tsx` — specifically `handleSubmit` and `handleEventMove`).

**Chosen approach:** **B.**

**Rationale:** Per Decision 1 we standardize on string enums; the modal's form-state `actionType: number` field must become `MarketingActionType`, and `handleEventMove`'s `ACTION_TYPE_TO_INT[event.actionType] ?? 99` becomes `event.actionType as MarketingActionType` (or a typed pass-through). Spec FR-2 acceptance permits this as long as the consumer files are listed; this review amends the spec to list them (see Specification Amendments).

The `(detailQuery.data as any)` / `(listQuery.data as any)` casts in the same consumer files are **out of scope** — they exist because consumers map response fields to a different shape (`description → detail`, `startDate → dateFrom`) and that mapping concern is unrelated to the request-side type-safety fix.

## Implementation Guidance

### Directory / Module Structure
No new files. Edits confined to:
- `frontend/src/api/hooks/useMarketingCalendar.ts` — primary change.
- `frontend/src/components/marketing/detail/MarketingActionModal.tsx` — `actionType` and `folderLinks[].folderType` change from `number` to the respective string enum in form state and submission.
- `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx` — `handleEventMove` (line 217) drops the `ACTION_TYPE_TO_INT[...]` lookup; passes the enum value directly.
- `frontend/src/api/hooks/__tests__/useImportFromOutlook.test.ts` — line 35–37 mock typing (`as any` on the mock client object is unavoidable for a partial mock and can stay; but if `MarketingActionType` flows into a new test case, use the enum).
- **Possibly** `frontend/src/components/marketing/calendar/fullcalendarAdapters.ts` — if `ACTION_TYPE_TO_INT` becomes unreferenced, delete the constant. **Verify with `grep` before deleting.**

### Interfaces and Contracts

Replace the local payload interfaces in `useMarketingCalendar.ts` with:

```ts
import {
  MarketingActionType,
  MarketingFolderType,
  ICreateMarketingActionRequest,
  IUpdateMarketingActionRequest,
  IImportFromOutlookRequest,
  IMarketingFolderLinkRequest,
} from "../generated/api-client";

interface GetMarketingActionsParams {
  pageNumber?: number;
  pageSize?: number;
  searchTerm?: string;
  actionType?: MarketingActionType;
  productCodePrefix?: string;
  startDateFrom?: Date;
  startDateTo?: Date;
  endDateFrom?: Date;
  endDateTo?: Date;
  includeDeleted?: boolean;
}

type CreateMarketingActionPayload = ICreateMarketingActionRequest;
type UpdateMarketingActionPayload = IUpdateMarketingActionRequest;
type ImportFromOutlookPayload    = IImportFromOutlookRequest;
```

Re-export them from the hook file so consumer imports do not break:
```ts
export type {
  CreateMarketingActionPayload,
  UpdateMarketingActionPayload,
  ImportFromOutlookPayload,
};
```

The hook public signatures (per spec section *API / Interface Design*) remain identical; only the field *types* on the payload narrow.

### Data Flow

For the canonical create flow (others are analogous):

1. `MarketingActionModal.handleSubmit` (line 181) builds `payload: ICreateMarketingActionRequest` — `actionType` is now a `MarketingActionType` value (e.g. `MarketingActionType.SocialMedia`), `folderLinks[].folderType` is now a `MarketingFolderType`.
2. `useCreateMarketingAction().mutateAsync(payload)` accepts the typed payload (no `any`).
3. Inside `mutationFn`, the hook calls `client.marketingCalendar_CreateMarketingAction(payload)` — TypeScript matches the structural shape against `CreateMarketingActionRequest`.
4. The generated method's `JSON.stringify` emits `{"title": "...", "actionType": "SocialMedia", ...}` to `POST /api/MarketingCalendar`.
5. Backend `JsonStringEnumConverter` parses the string into the C# enum and proceeds normally.

For the calendar drag-and-resize flow (`handleEventMove`, line 209): `event.actionType` is already a string (`'SocialMedia'`, etc.) per `CalendarEvent` in `fullcalendarAdapters.ts:6`. Just pass `event.actionType as MarketingActionType` (or, better, change `CalendarEvent.actionType` to `MarketingActionType` so the cast disappears too). Drop the `ACTION_TYPE_TO_INT[...] ?? 99` lookup.

For the modal's `FOLDER_TYPE_OPTIONS` (line 24–30): change `value: number` to `value: MarketingFolderType` so the form binds enum strings directly. The `<select>` element will serialize them as strings without any change to the JSX.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Wire format changes from integer to string for `actionType` / `folderType` and a backend deployment somewhere accepts only integers | Medium | Backend uses `JsonStringEnumConverter` globally (`Program.cs:142`), which accepts both forms. Verified by reading source. Run the spec's manual smoke test against staging before merging. |
| `ACTION_TYPE_TO_INT` is referenced from a file not surfaced by grep, deletion breaks a build | Low | Search with `grep -rn "ACTION_TYPE_TO_INT" frontend/src` before removing. If any reference remains outside the marketing module, leave the constant in place (it costs nothing). |
| `MarketingActionModal`'s `resolveOptionValue` helper (line 32) was tolerating mixed string/number inputs from legacy data | Low | After the change, `existingAction.actionType` is already a `MarketingActionType` string from the typed response; `resolveOptionValue` collapses to "find option by `backendName === raw`." Validate with the existing modal test (`MarketingActionModal.test.tsx`). |
| Consumer files (`MarketingCalendarPage`, `MobileAgendaView`) still cast `query.data as any` for unrelated reasons | Low | Explicitly out of scope. The hook now *returns* `GetMarketingActionsResponse` etc., so the read-side typing is available to a future cleanup. |
| TypeScript rejects a plain-object literal where the generated method demands a `…Request` class instance | Low | Fall back to `new CreateMarketingActionRequest(payload)` etc. The generated constructor accepts the `I*Request` shape via `for (var property in data) (<any>this)[property] = ...` (api-client.ts:29887–29893). |
| The `useImportFromOutlook.test.ts` mock (`as any` on line 37) is an inevitable partial-mock workaround, not a violation of FR-1 | None | FR-1 scopes "no new `as any`" to `useMarketingCalendar.ts`, not its tests. Leave the test mock unchanged; only the production hook file is bound by FR-1. |

## Specification Amendments

1. **Add consumer files to scope (FR-2 acceptance bullet 3).** The spec leaves "OR the spec's 'Out of Scope' list explicitly notes the consumer files that must update" as a fork in the road. Architecturally the correct fork is to update callers (see Decision 1 rationale). Amend the spec to declare these files **in scope**:
   - `frontend/src/components/marketing/detail/MarketingActionModal.tsx` — form state `actionType: number` → `MarketingActionType`; `FolderLinkInput.folderType: number` → `MarketingFolderType`; `FOLDER_TYPE_OPTIONS[i].value` → `MarketingFolderType`; `ACTION_TYPE_OPTIONS[i].value` → `MarketingActionType`; submit payload aligned accordingly.
   - `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx` — `handleEventMove` (line 209) passes `event.actionType` (typed as `MarketingActionType`) directly; remove the `ACTION_TYPE_TO_INT` lookup.
   - `frontend/src/components/marketing/calendar/fullcalendarAdapters.ts` — narrow `CalendarEvent.actionType: string` to `MarketingActionType`; if `ACTION_TYPE_TO_INT` becomes unreferenced, delete it.

2. **Acknowledge wire-format normalization under FR-3.** "Identical HTTP requests" is true for field names, method, URL, and query parameters, but `actionType` and `folderType` JSON values change from integer to string. Both forms are accepted by the backend's `JsonStringEnumConverter`, and response bodies are already strings. The amendment makes this normalization explicit and requires the spec's smoke test to run against staging before merge (it already does).

3. **FR-5 wording: clarify the open question is resolved.** The original `as any` was driven by the int↔string enum mismatch in local payload types, not by an NSwag generator gotcha. No change to `docs/development/api-client-generation.md` is needed. The spec already says "no documentation changes required … unless"; this review confirms the "unless" branch does not apply.

4. **Remove `ImportFromOutlookResult` reaffirmation.** Spec NFR-4 lists `ImportFromOutlookResult` among public exports. It is currently used as a local shape for consumer-side normalization, not as a hook return type. The hook's `useImportFromOutlook` returns `UseMutationResult<ImportFromOutlookResponse, ...>` (the generated type). Keep `ImportFromOutlookResult` exported unchanged — it is a consumer-side projection, orthogonal to this refactor.

## Prerequisites

None. No migrations, infrastructure, or configuration changes required. The generated client already exposes the correct types, the backend already accepts the canonical string form via `JsonStringEnumConverter`, and `frontend/src/api/client.ts` already returns a typed `ApiClient` from `getAuthenticatedApiClient()`. Implementation can begin immediately.