# Architecture Review: Marketing Calendar Drag/Resize Silently Deletes Folder Links

## Skip Design: true

Backend bug fix plus one frontend hook rewiring. Verified `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx` (`handleEventMove`, lines 207–223): the change is a straight swap of one mutation call for another — same call site, same arguments shape passed down to `MarketingMonthCalendar`, no new component, no new visual state, no changed styling or layout. `handleEventResize` (lines 225–230) already delegates to `handleEventMove` unchanged. No screen, modal, or interaction pattern is added or altered from the user's point of view — a drag/resize looks and behaves exactly the same; only the network call underneath changes. This is pure wiring.

## Architectural Fit Assessment

This fits the existing Marketing module cleanly and requires no new architectural concepts. The module already follows the "Complex Feature" shape described in `docs/architecture/filesystem.md` (`UseCases/{UseCase}/{UseCase}Handler.cs` + `Contracts/{UseCase}Request.cs`), and it already has five sibling use cases (`CreateMarketingAction`, `UpdateMarketingAction`, `DeleteMarketingAction`, `GetMarketingAction(s)`, `GetMarketingCalendar`, `ImportFromOutlook`) that this change directly imitates.

The root cause is a Single Responsibility violation at the handler level, exactly as diagnosed in the brief: `UpdateMarketingActionHandler` (`backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/UpdateMarketingAction/UpdateMarketingActionHandler.cs:95–98`) unconditionally calls `action.ReplaceProductAssociations(...)` and `action.ReplaceFolderLinks(...)`, both of which are documented on the domain entity (`backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs:164–166` and `:129–133`) as treating `null` as "clear everything." That's correct, intentional behavior for the full-edit modal (`MarketingActionModal`) which always loads the complete `MarketingActionDto`. It is wrong for the calendar drag/resize path, which only ever has a `MarketingActionCalendarDto` (no `folderLinks` field at all — see `Contracts/MarketingActionCalendarDto.cs`).

The fix pattern — give the date-only operation its own use case rather than adding conditional logic to the shared handler — is consistent with how this module already separates concerns (separate handlers per verb: Create/Update/Delete/Get, rather than one mega-handler with an operation-type flag). No conditional "is this a partial update" branching is introduced anywhere; the new use case simply never has access to the fields it shouldn't touch, which is the correct way to make the bug structurally impossible to reintroduce (confirmed: `MoveMarketingActionRequest` has no `Title`/`AssociatedProducts`/`FolderLinks` members).

I verified `UpdateMarketingActionHandler`'s actual execution order against the spec's description of it — they match: `UpdateDetails` → Outlook sync (create-or-update) → `ReplaceProductAssociations`/`ReplaceFolderLinks` → persist. The new handler is a strict subsequence of this (drop the two `Replace*` calls), which is the simplest possible diff against a working, tested reference implementation.

## Proposed Architecture

### Component Overview

```
Frontend                         Backend
────────                         ───────
MarketingCalendarPage.tsx
  handleEventMove/Resize
        │
        ▼
  useMoveMarketingAction()  ──PATCH /api/MarketingCalendar/{id}/move──►  MarketingCalendarController
  (new hook, useMarketingCalendar.ts)                                     .MoveMarketingAction(id, request)
                                                                                 │  (MediatR.Send)
                                                                                 ▼
                                                                     MoveMarketingActionHandler
                                                                       (Application/Features/Marketing/
                                                                        UseCases/MoveMarketingAction/)
                                                                                 │
                                                              ┌──────────────────┼───────────────────┐
                                                              ▼                  ▼                   ▼
                                                   ICurrentUserService  IMarketingActionRepository  IOutlookCalendarSync
                                                     (auth check)        .GetByIdAsync/UpdateAsync    .UpdateEventAsync
                                                                          .SaveChangesAsync            (only if OutlookEventId set)
                                                                                 │
                                                                                 ▼
                                                                     MarketingAction.UpdateDetails(...)
                                                                     (no ReplaceProductAssociations,
                                                                      no ReplaceFolderLinks — never called)

Existing, unchanged:
MarketingActionModal (full edit) ──PUT /{id}──► UpdateMarketingActionHandler (still full-replace semantics)
```

The new use case is additive: it does not modify `UpdateMarketingActionHandler`, `MarketingAction`, or `MarketingActionCalendarDto`. It reuses `UpdateDetails` (already side-effect-free on the two collections) rather than adding a new domain method, and reuses `IMarketingActionRepository`/`IOutlookCalendarSync` as-is — no new repository methods, no new domain methods, no new external dependencies.

### Key Design Decisions

#### Decision 1: New use case vs. conditional logic in `UpdateMarketingActionHandler`
**Options considered:**
1. Add a "partial update" flag/nullable-sentinel convention to `UpdateMarketingActionRequest` so the handler can distinguish "field omitted" from "field explicitly emptied," then branch inside the existing handler.
2. Change `ReplaceFolderLinks`/`ReplaceProductAssociations` domain semantics so `null` means "no change" instead of "clear all," and have the frontend send an explicit empty array `[]` when the user really wants to clear.
3. Introduce a dedicated `MoveMarketingAction` use case (brief's proposal) that structurally cannot carry collection data.

**Chosen approach:** Option 3.

**Rationale:** Option 1 requires either nullable-wrapper types (`Optional<T>`) that don't exist elsewhere in this codebase, or magic sentinel values — both add incidental complexity to a handler that's supposed to represent one operation, and the "did the client mean to omit vs. clear" ambiguity re-appears at every future call site of `UpdateMarketingAction`. Option 2 is a breaking change to `ReplaceFolderLinks`' contract (it's explicitly documented and has dedicated domain tests — `MarketingActionReplaceFolderLinksTests.cs`, `MarketingActionReplaceProductAssociationsTests.cs`) that would require touching the full-edit modal's payload construction too, for no benefit beyond fixing this one call site — and it inverts a documented, tested domain invariant instead of just not invoking it. Option 3 costs one small new file set but keeps every existing contract, domain method, and test unchanged, matches the project's established "one handler per operation" convention, and makes the bug's root cause (wrong handler used for a narrower operation) structurally unreachable rather than merely patched.

#### Decision 2: Reuse `UpdateDetails` vs. add a new `MoveDates` domain method
**Options considered:**
1. Add `MarketingAction.MoveDates(DateTime startDate, DateTime? endDate, string userId, string? username, DateTime utcNow)` as a narrower domain method that only touches date/audit fields.
2. Reuse the existing `UpdateDetails(title, description, actionType, startDate, endDate, ...)`, re-supplying the entity's own current `Title`/`Description`/`ActionType` unchanged.

**Chosen approach:** Option 2 (per spec).

**Rationale:** `UpdateDetails` (`MarketingAction.cs:235–253`) already does not touch `ProductAssociations`/`FolderLinks` — the bug is entirely in the *handler*, not in this domain method. Adding a second domain method with near-identical behavior (set fields, bump `ModifiedAt`/`ModifiedBy`) duplicates logic and doubles the domain test surface for no behavioral gain. This is a case where the pragmatic reuse is also the more correct design — a "move" is simply an `UpdateDetails` call where the caller happens to pass through the unchanged title/description/type. If a future requirement needs `MoveDates` to diverge from `UpdateDetails` (e.g., different validation), split then; don't speculatively split now.

#### Decision 3: Controller route shape — new endpoint vs. reusing `PUT {id}`
**Options considered:**
1. Keep `PUT /api/MarketingCalendar/{id}` and have the calendar page send a "full" payload by first fetching the complete `MarketingActionDto` before every drag/resize (fetch-then-update).
2. Add `PATCH /api/MarketingCalendar/{id}/move` as a new, narrower endpoint (brief's proposal).

**Chosen approach:** Option 2.

**Rationale:** Option 1 avoids new backend code but adds an extra round-trip (`GetMarketingAction` fetch) on every single drag/resize frame-to-drop event, coupling the calendar's perceived responsiveness to an extra network call, and it still doesn't fix the underlying SRP problem — it just works around it by feeding the greedy handler more data than the calendar view needs. `PATCH .../move` is the correct HTTP semantic for a partial, single-concern update (RFC 5789), matches the project's `HttpPut`/`HttpDelete` per-verb controller action pattern already used in this exact controller, and needs no new authorization surface (reuses `[FeatureAuthorize(Feature.Marketing_MarketingCalendar, AccessLevel.Write)]`, identical to `PUT {id}` and `DELETE {id}`).

## Implementation Guidance

### Directory / Module Structure

New files (all additive, nothing existing is deleted):

```
backend/src/Anela.Heblo.Application/Features/Marketing/
├── Contracts/
│   └── MoveMarketingActionRequest.cs      # NEW — MoveMarketingActionRequest + MoveMarketingActionResponse
└── UseCases/
    └── MoveMarketingAction/
        └── MoveMarketingActionHandler.cs  # NEW

backend/src/Anela.Heblo.API/Controllers/
└── MarketingCalendarController.cs         # MODIFIED — add MoveMarketingAction action

frontend/src/api/hooks/
└── useMarketingCalendar.ts                # MODIFIED — add useMoveMarketingAction

frontend/src/components/marketing/pages/
└── MarketingCalendarPage.tsx              # MODIFIED — handleEventMove uses the new hook

backend/test/Anela.Heblo.Tests/Application/Marketing/
└── MoveMarketingActionHandlerTests.cs     # NEW — follow UpdateMarketingActionHandlerTests.cs conventions
```

This exactly mirrors the existing `UseCases/UpdateMarketingAction/` + `Contracts/UpdateMarketingActionRequest.cs` split — no deviation from the established pattern. No new `Validators/` file is needed: `[Required]` data annotations on `StartDate` are sufficient and match how `UpdateMarketingActionRequest`/`CreateMarketingActionRequest` validate today (no FluentValidation validators exist for this module currently — don't introduce one for this single field).

Put the new handler test in `backend/test/Anela.Heblo.Tests/Application/Marketing/` (where `UpdateMarketingActionHandlerTests.cs` lives), not in `backend/test/Anela.Heblo.Tests/Features/Marketing/` — the module's tests are inconsistently split across both folders already; match the sibling you're modeling against (`UpdateMarketingActionHandlerTests.cs`), don't add to the split.

MediatR handler registration requires no change — `MarketingModule.AddMarketingModule` registers no handlers explicitly ("MediatR handlers are auto-registered by assembly scan," confirmed in `MarketingModule.cs`), so `MoveMarketingActionHandler` is picked up automatically once it implements `IRequestHandler<MoveMarketingActionRequest, MoveMarketingActionResponse>`.

### Interfaces and Contracts

```csharp
// Contracts/MoveMarketingActionRequest.cs
public class MoveMarketingActionRequest : IRequest<MoveMarketingActionResponse>
{
    public int Id { get; set; }

    [Required]
    public DateTime StartDate { get; set; }

    public DateTime? EndDate { get; set; }
}

public class MoveMarketingActionResponse : BaseResponse
{
    public int Id { get; set; }
    public DateTime ModifiedAt { get; set; }
    public string Message { get; set; } = "Marketing action moved successfully";

    public MoveMarketingActionResponse() : base() { }
    public MoveMarketingActionResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
```

This is a class (per the project's DTO rule — never a C# record), `IRequest<TResponse>` (not the brief's illustrative bare `IRequest`), and `BaseResponse`-derived — identical shape to `UpdateMarketingActionRequest`/`Response` and `DeleteMarketingActionRequest`/`Response`, just with a narrower field set. All required `ErrorCodes` values already exist (`UnauthorizedMarketingAccess = 2302`, `MarketingActionNotFound = 2301`, `MarketingCalendarAccessDenied = 2303`, `MarketingCalendarSyncFailed = 2304`, `DatabaseError = 0011` — verified in `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`) — no new error code is needed.

Controller contract (new action on the existing `MarketingCalendarController`, same class, same `[FeatureAuthorize(Feature.Marketing_MarketingCalendar)]` base attribute the class already carries):

```csharp
[HttpPatch("{id:int}/move")]
[FeatureAuthorize(Feature.Marketing_MarketingCalendar, AccessLevel.Write)]
[ProducesResponseType(typeof(MoveMarketingActionResponse), StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
public async Task<ActionResult<MoveMarketingActionResponse>> MoveMarketingAction(
    int id,
    [FromBody] MoveMarketingActionRequest request)
{
    request.Id = id;
    var response = await _mediator.Send(request);
    return HandleResponse(response);
}
```

Frontend hook contract, mirroring `useUpdateMarketingAction` exactly (same invalidation keys):

```ts
export const useMoveMarketingAction = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, startDate, endDate }: { id: number; startDate: Date; endDate?: Date }) => {
      const client = await getAuthenticatedApiClient();
      return await client.marketingCalendar_MoveMarketingAction(
        id,
        new MoveMarketingActionRequest({ id, startDate, endDate }),
      );
    },
    onSuccess: (_, { id }) => {
      queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.marketingCalendar, "actions"] });
      queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.marketingCalendar, "calendar"] });
      queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.marketingCalendar, "action", id] });
    },
  });
};
```

`MoveMarketingActionRequest` (the TS class) and `marketingCalendar_MoveMarketingAction` are both auto-generated from the OpenAPI spec on `npm run build`/`dotnet build` per `docs/development/api-client-generation.md` — do not hand-write these into `frontend/src/api/generated/api-client`; that file is regenerated, not edited.

### Data Flow

1. User drags/resizes an event in `MarketingMonthCalendar`; FullCalendar fires its move/resize callback with `(id, dateFrom, dateTo)`.
2. `handleEventMove` in `MarketingCalendarPage.tsx` calls `useMoveMarketingAction().mutate({ id, startDate: new Date(dateFrom), endDate: new Date(dateTo) })` — no lookup into `calendarEvents` for `title`/`actionType`/`associatedProducts` is needed anymore, since the new payload doesn't carry them (today's code reads `event.title`, `event.actionType`, `event.associatedProducts` off the in-memory `CalendarEvent` purely to satisfy `UpdateMarketingActionRequest`'s required fields — that lookup goes away entirely with the new hook).
3. `PATCH /api/MarketingCalendar/{id}/move` → `MarketingCalendarController.MoveMarketingAction` → `MoveMarketingActionHandler.Handle`.
4. Handler: auth check → `GetByIdAsync` → `action.UpdateDetails(action.Title, action.Description, action.ActionType, request.StartDate, request.EndDate, currentUser.Id, currentUser.Name, now)` → conditional Outlook `UpdateEventAsync` only if `OutlookEventId` already set → `UpdateAsync` + `SaveChangesAsync`. `FolderLinks`/`ProductAssociations` collections are never touched — not cleared, not re-set, not read.
5. On success, `onSuccess` invalidates `actions`/`calendar`/`action/{id}` query keys — identical to today's behavior, so the calendar view still reflects the new date immediately and the (still-open, if any) edit modal for that action would refetch fresh data rather than show stale dates.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| A developer later adds a field to `MoveMarketingActionRequest` "just to be convenient" (e.g. `title`), reopening the SRP violation | Medium | Code review discipline; the type's whole purpose is narrowness — the PR description/spec should be the enforcement point. No test can prevent a future field addition, but `MoveMarketingActionHandlerTests` asserting collections are untouched will catch it if someone forgets to *also* wire the new field into a `Replace*` call incorrectly. |
| Outlook sync branch: unlike `UpdateMarketingActionHandler`, this handler must NOT create a new Outlook event when `OutlookEventId` is absent (per spec) | Medium | Explicit test case: "no `OutlookEventId` → `CreateEventAsync` never invoked." This is a behavioral divergence from the sibling handler, easy to get wrong by copy-pasting `UpdateMarketingActionHandler`'s Outlook block verbatim (which has both branches). Call this out in the PR description too. |
| `EndDate` nullability: `MoveMarketingActionRequest.EndDate` is `DateTime?`, but calendar drag/resize always computes both dates (`handleEventResize`/`handleEventMove` always pass `dateTo`) — a null `EndDate` on this endpoint is a request-contract allowance, not a real UI path | Low | No code change needed; documented in spec's "Out of scope" — matches `UpdateMarketingActionRequest.EndDate`'s existing nullability, so no divergence in contract shape. |
| Concurrent edit: user has full edit modal open (with fetched `folderLinks`) while another tab drags the same event, then saves the modal — `UpdateMarketingActionHandler`'s full-replace would overwrite the just-moved dates with the modal's stale dates | Low | Out of scope per spec (`ModifiedAt`-based concurrency is explicitly deferred). Pre-existing risk class (same race exists between two full-edit-modal saves today); this change does not make it worse and does not need to fix it. |
| Frontend: removing the `calendarEvents.find(...)` lookup in `handleEventMove` changes the hook's dependency array | Low | `useCallback` deps for the new `handleEventMove` drop `calendarEvents` (no longer read) and gain the new mutation object — straightforward, verify via `npm run lint`/existing component tests if any cover this handler. |

## Specification Amendments

None required — the spec (`spec.r1.md`) is architecturally sound and already reflects the codebase accurately (verified: `UpdateMarketingActionHandler`'s real execution order, `MarketingAction.UpdateDetails`/`ReplaceFolderLinks`/`ReplaceProductAssociations` signatures, `ErrorCodes` values, and controller/hook patterns all match what the spec describes). Two small clarifications worth calling out for the implementing developer, not changes to scope:

1. **Test file location**: place `MoveMarketingActionHandlerTests.cs` under `backend/test/Anela.Heblo.Tests/Application/Marketing/` (alongside `UpdateMarketingActionHandlerTests.cs`), not `.../Features/Marketing/` — the spec's FR-5 says "matching existing test conventions if present," and the directly-analogous sibling test lives in the `Application/Marketing` folder.
2. **`useCallback` dependency cleanup**: when `handleEventMove` stops reading `event.title`/`event.actionType`/`event.associatedProducts`, the `calendarEvents.find(...)` lookup becomes dead code and should be removed, not left as an unused lookup — this is directly required by "surgical changes" (don't leave the entity fetch in place if nothing uses the result).

## Prerequisites

None. No migrations, no config, no new infrastructure, no new feature flags. `Feature.Marketing_MarketingCalendar` (write access level) already exists and is reused unchanged. The OpenAPI/TypeScript client regeneration is automatic on build per `docs/development/api-client-generation.md` and requires no manual step beyond running the normal build.
