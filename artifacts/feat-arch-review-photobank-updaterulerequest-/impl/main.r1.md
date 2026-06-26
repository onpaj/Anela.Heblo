All 5 tasks complete. Writing the implementation artifact.

---

# Implementation: Fix PhotobankController.UpdateRule Body Contract

## What was implemented

Replaced the misleading `[FromBody] UpdateRuleRequest` parameter on `PUT /api/photobank/settings/rules/{id}` with a dedicated `UpdateRuleBody` HTTP contract. The OpenAPI schema and auto-generated TypeScript client no longer advertise an `id` body field. Runtime behavior is identical — `Id` still flows from the route parameter into the MediatR command.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/UpdateRuleBody.cs` — New HTTP body contract: `PathPattern`, `TagName`, `IsActive`, `SortOrder`. No `Id`. Matches neighboring `*Body` class conventions (block namespace, `= null!` initializers, `public class` not record).
- `backend/src/Anela.Heblo.API/Controllers/PhotobankController.cs` — `UpdateRule` method: `[FromBody] UpdateRuleRequest request` → `[FromBody] UpdateRuleBody body`; four `request.XYZ` assignments → `body.XYZ`. `HandleResponse(response)` and all attributes unchanged.
- `frontend/src/api/generated/api-client.ts` — Regenerated via NSwag on `npm run build`. `UpdateRuleRequest` (had `id`) replaced by `UpdateRuleBody` (no `id`). `photobank_UpdateRule` parameter renamed `body: UpdateRuleBody`.

## Tests

No new tests added — this is a type-level change with no runtime behavior change. No existing `UpdateRule*Tests.cs` files exist in the project. The verifiable check is the regenerated `api-client.ts` schema (confirmed: `id` absent, 4 correct fields present). 155 existing Photobank tests passed with no regressions.

## How to verify

```bash
# Backend builds clean
dotnet build backend/Anela.Heblo.sln

# Frontend builds clean with regenerated client
cd frontend && npm run build && npm run lint && cd ..

# Confirm id is gone from the body type
grep -A 15 "class UpdateRuleBody" frontend/src/api/generated/api-client.ts

# Confirm UpdateRuleRequest.Id still exists on the MediatR command
cat backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/UpdateRule/UpdateRuleRequest.cs

# See the 3 implementation commits on top of the planning artifacts
git log --oneline main..HEAD
```

## Notes

- `return HandleResponse(response)` was preserved — the spec sample showed `return Ok(result)` but that would have silently changed error status code behavior (arch-review Risk #1 / Decision #3).
- `AddRule` (immediate sibling at lines ~280–289, same anti-pattern) was intentionally left untouched per spec Out-of-Scope.
- 4 pre-existing planning commits (`agent: upload artifacts/...`) on the branch are from the prior arch-review session — not part of this implementation.
- `dotnet format` made no changes; no formatter commit needed.

## PR Summary

Fixes the `PUT /api/photobank/settings/rules/{id}` endpoint body contract by introducing a dedicated `UpdateRuleBody` class that omits the `id` field. Previously, the endpoint bound `[FromBody] UpdateRuleRequest` directly, causing the OpenAPI schema (and the auto-generated TypeScript client) to advertise an `id` body field that the controller silently ignored in favour of the route value — a debugging trap. The fix aligns `UpdateRule` with every other write endpoint in the Photobank module (`AddPhotoTagBody`, `CreateTagBody`, `BulkAddPhotoTagBody`), which all use dedicated `*Body` contracts. Runtime behaviour is identical.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/UpdateRuleBody.cs` — new HTTP body contract (4 fields, no `id`)
- `backend/src/Anela.Heblo.API/Controllers/PhotobankController.cs` — `UpdateRule` parameter type changed from `UpdateRuleRequest` to `UpdateRuleBody`
- `frontend/src/api/generated/api-client.ts` — regenerated; `updateRule` body type now contains only `pathPattern`, `tagName`, `isActive`, `sortOrder`

## Status
DONE