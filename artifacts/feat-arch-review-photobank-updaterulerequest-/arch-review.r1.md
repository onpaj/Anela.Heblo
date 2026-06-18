# Architecture Review: Fix UpdateRule Endpoint Body Contract

## Skip Design: true

Backend-only HTTP contract cleanup. No UI components, screens, or visual changes — only the OpenAPI schema and auto-generated TypeScript client *type* shift. No frontend hand-written callers exist (verified: no references to `photobank_UpdateRule` outside `frontend/src/api/generated/api-client.ts`).

## Architectural Fit Assessment

The proposed change is an exact alignment with the Photobank module's established pattern. Verified against the existing codebase:

- **`Anela.Heblo.Application/Features/Photobank/Contracts/`** already contains five `*Body` types (`CreateTagBody`, `AddPhotoTagBody`, `BulkAddPhotoTagBody`, `BulkAddPhotoTagByIdsBody`, plus DTOs). All are plain `class`, all in namespace `Anela.Heblo.Application.Features.Photobank.Contracts`.
- **`UpdateRuleRequest`** is the only write-endpoint MediatR command that surfaces in a `[FromBody]` slot today (`PhotobankController.cs:301`). Adding `UpdateRuleBody` retires the outlier.
- **No conflicting validator behavior**: `UpdateRuleRequestValidator` runs against the MediatR command (post-construction in the controller), so it keeps seeing `Id` populated from the route. The `RuleFor(x => x.Id).GreaterThan(0)` rule continues to function identically.
- **No frontend churn**: a wider grep (`updateRule`, `photobank_UpdateRule`) found no hand-written callers — the regenerated client compiles into nothing but itself.

Integration points: `PhotobankController.UpdateRule` (changed signature only), OpenAPI generator (NSwag/Swashbuckle picks up the new type automatically), TypeScript client regeneration on build.

## Proposed Architecture

### Component Overview

```
HTTP PUT /api/photobank/settings/rules/{id}
        │
        ▼
[FromBody] UpdateRuleBody  ◄── NEW: public contract (no Id)
        │  (controller maps route id + body → command)
        ▼
UpdateRuleRequest (MediatR command, unchanged — still has Id)
        │
        ▼
UpdateRuleRequestValidator (unchanged, validates Id > 0 from route)
        │
        ▼
UpdateRuleHandler → UpdateRuleResponse (unchanged)
```

The dotted boundary between the HTTP contract layer and the MediatR command layer is now consistent across every write endpoint in the module.

### Key Design Decisions

#### Decision 1: Separate HTTP body contract from MediatR command
**Options considered:**
- A. Drop `Id` from `UpdateRuleRequest` directly and pass it as a separate `Send` parameter.
- B. Introduce a dedicated `UpdateRuleBody` and map in the controller (spec's choice).
- C. Add `[JsonIgnore]` to `UpdateRuleRequest.Id` to hide it from the schema.

**Chosen approach:** B — dedicated `UpdateRuleBody` in `Contracts/`.

**Rationale:** Matches the module's own convention used for every other `POST`/`PUT` endpoint (`AddPhotoTagBody`, `CreateTagBody`, etc.). Option A leaks a route concern into the MediatR command's public shape and breaks any existing internal callers of the command (and the validator). Option C produces a contract that still claims an `Id` field exists in C# while hiding it from JSON — surprising for anyone reading the type. The chosen approach maintains the clean separation: MediatR commands are internal handler inputs; `*Body` types are the public HTTP surface.

#### Decision 2: Keep `Id` on the MediatR command
**Options considered:**
- A. Remove `Id` from `UpdateRuleRequest`, thread it as a method parameter or wrapper.
- B. Keep `UpdateRuleRequest.Id` unchanged (spec's choice).

**Chosen approach:** B.

**Rationale:** The MediatR command is a self-contained, validatable unit of work for the handler. `UpdateRuleRequestValidator.RuleFor(x => x.Id).GreaterThan(0)` depends on `Id` being on the command — removing it would force a validator rewrite or a redundant pre-validator at the controller. The spec correctly scopes this as out-of-scope refactoring.

#### Decision 3: Preserve controller's existing return shape (`HandleResponse`)
**Observed:** Current controller uses `return HandleResponse(response);` (line 313), not `return Ok(result);` as the spec's sample shows.

**Chosen approach:** Keep `HandleResponse(response)` exactly as it is today.

**Rationale:** `HandleResponse` is the controller's shared response envelope (likely maps `Success`/`Error` to status codes and error contracts consistently across endpoints). Swapping to `Ok(...)` would silently change error status code behavior — outside the spec's "behavior preservation" requirement (FR-5). Treat the spec's sample as illustrative; the actual line to change is **the parameter type and the construction-source for the command's fields**, nothing else.

## Implementation Guidance

### Directory / Module Structure

**Create:**
- `backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/UpdateRuleBody.cs`

**Modify:**
- `backend/src/Anela.Heblo.API/Controllers/PhotobankController.cs` — only lines 299–311 (the `UpdateRule` action).

**Regenerate (build-time, automated):**
- `frontend/src/api/generated/api-client.ts` — produced by the OpenAPI pipeline on `npm run build` per `docs/development/api-client-generation.md`. Do not hand-edit.

**No changes:**
- `UseCases/UpdateRule/*` (handler, request, response).
- `Validators/UpdateRuleRequestValidator.cs`.
- Any test file under `backend/test/Anela.Heblo.Tests/Features/Photobank/` (no `UpdateRule*Tests.cs` exists today — confirmed by `ls`).

### Interfaces and Contracts

`UpdateRuleBody.cs` (verbatim target — namespace verified against neighboring files):

```csharp
namespace Anela.Heblo.Application.Features.Photobank.Contracts
{
    public class UpdateRuleBody
    {
        public string PathPattern { get; set; } = null!;
        public string TagName { get; set; } = null!;
        public bool IsActive { get; set; }
        public int SortOrder { get; set; }
    }
}
```

Controller method (only changed lines — keep all attributes, `HandleResponse`, and `_mediator` field name as-is):

```csharp
public async Task<ActionResult<UpdateRuleResponse>> UpdateRule(
    int id,
    [FromBody] UpdateRuleBody body,
    CancellationToken cancellationToken = default)
{
    var command = new UpdateRuleRequest
    {
        Id = id,
        PathPattern = body.PathPattern,
        TagName = body.TagName,
        IsActive = body.IsActive,
        SortOrder = body.SortOrder,
    };
    var response = await _mediator.Send(command, cancellationToken);
    return HandleResponse(response);
}
```

### Data Flow

1. Client serializes `{ pathPattern, tagName, isActive, sortOrder }` and `PUT /api/photobank/settings/rules/42`.
2. ASP.NET model-binds the JSON into `UpdateRuleBody`; binds `42` from the route into `int id`.
3. Controller constructs `UpdateRuleRequest { Id = 42, … body fields }`.
4. MediatR pipeline runs `UpdateRuleRequestValidator` against the command (Id=42 passes `> 0`).
5. `UpdateRuleHandler` executes, returns `UpdateRuleResponse`.
6. `HandleResponse(response)` maps to HTTP — unchanged status code / error envelope behavior.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Spec sample shows `return Ok(result)`; actual controller uses `HandleResponse(response)`. Following the sample literally would change error status codes. | Medium | Treat the spec sample as illustrative. Implement only the two changes called out in Decision 3 (parameter type + command field source). Diff review must show no change to the return expression. |
| `AddRule` also binds a MediatR request directly as `[FromBody]` (`PhotobankController.cs:282`) — same anti-pattern, explicitly out of scope here. | Low | Spec marks "Auditing other endpoints" as out of scope. File as a separate item via the daily arch-review routine (don't expand this PR). |
| Validator file is named `UpdateRuleRequestValidator.cs` and targets the MediatR command — easy to mistake for "needs renaming." | Low | Validators run on commands post-controller construction. Leave name and target type unchanged. |
| OpenAPI regeneration produces a new TypeScript type `UpdateRuleBody`, removing `UpdateRuleRequest` from the body slot. If any future frontend code imports `UpdateRuleRequest` for the body, it will compile-break. | Low | Verified today no hand-written code references the type. `npm run build` will catch any future regression. |
| Forgetting to commit the regenerated `api-client.ts` would leave the repo in an inconsistent state. | Medium | Run `npm run build` locally, then `git add frontend/src/api/generated/api-client.ts` explicitly before committing. CI build does not regenerate into the working tree. |
| Spec says `contracts/` (lowercase), actual folder is `Contracts/` (PascalCase). | Trivial | Use `Contracts/` to match every other file. See **Specification Amendments**. |

## Specification Amendments

1. **Folder casing**: spec refers to `contracts/`; the actual folder and namespace use PascalCase `Contracts`. Create the file at `backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/UpdateRuleBody.cs` in namespace `Anela.Heblo.Application.Features.Photobank.Contracts`. No braces vs. file-scoped namespace preference — match neighboring `CreateTagBody.cs` which uses **block-style** namespace (verified).
2. **Return statement**: spec's example uses `return Ok(result);`. The current controller uses `return HandleResponse(response);`. Keep `HandleResponse(response)` — this is the project's shared error/success envelope. Do not swap to `Ok(...)`.
3. **Existing tests**: no `UpdateRule*Tests.cs` files exist under `backend/test/Anela.Heblo.Tests/Features/Photobank/` (verified via `ls`). FR-5's "Existing unit/integration tests for UpdateRule pass without modification" is satisfied trivially — there are none to update.
4. **FR-4 is a no-op**: verified via grep that no hand-written frontend code calls `photobank_UpdateRule`. The change is closed at the C# layer plus a regenerated client file; no `.ts`/`.tsx` edits are needed.

## Prerequisites

None. No migrations, no configuration, no infrastructure. The implementation is:

1. Add `UpdateRuleBody.cs`.
2. Change two things in `PhotobankController.UpdateRule`: the `[FromBody]` parameter type and the right-hand side of the four field assignments in the `new UpdateRuleRequest { … }` initializer.
3. `dotnet build` → `dotnet format` → `npm run build` (regenerates `api-client.ts`) → `npm run lint`.
4. Commit C# changes **plus** the regenerated `frontend/src/api/generated/api-client.ts`.