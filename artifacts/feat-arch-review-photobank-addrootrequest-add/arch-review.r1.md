I have enough context. Writing the architecture review now.

# Architecture Review: Decouple Photobank MediatR Requests from HTTP Body DTOs

## Skip Design: true

Backend-only refactor. No UI components, screens, layouts, or visual design decisions. The HTTP wire contract is unchanged, so no frontend UX work is involved beyond the auto-generated client regeneration.

## Architectural Fit Assessment

This work is a **conformance fix**, not a new design. The Photobank module already has the target pattern implemented for 4 of 7 POST endpoints (`AddPhotoTag`, `BulkAddPhotoTag`, `CreateTag`, `BulkAddPhotoTagByIds`), all backed by `*Body` types under `Contracts/`. The three offending endpoints (`AddRoot`, `AddRule`, `RetagPhotos`) are the only outliers in `PhotobankController.cs`. The proposal aligns perfectly with:

- `docs/architecture/development_guidelines.md` — *"DTO objects for API (Request, Response) live in `contracts/` of the specific module."*
- ADR-005 (User Identity Resolution) — explicitly cites *"Request DTOs must not carry client-settable `UserId` / `ModifiedBy` — these are server-resolved, never trusted from the client (spoofing hole)."* Decoupling `*Body` from `*Request` is the structural enabler for that rule.

The only integration point is the auto-generated OpenAPI TypeScript client. The frontend currently bypasses generated client methods for these endpoints (raw `apiPost` calls with inline JSON in `usePhotobank.ts` and `usePhotobankSettings.ts`), so even the generated type renames have **no runtime impact** on the frontend.

## Proposed Architecture

### Component Overview

```
HTTP boundary (API project)               Application module (Photobank feature)
─────────────────────────────             ─────────────────────────────────────
PhotobankController.AddRoot                Contracts/                       UseCases/AddRoot/
  [FromBody] AddRootBody body  ──── maps ──▶ AddRootBody (NEW)              AddRootRequest  ── MediatR ──▶ AddRootHandler
                                                                              ▲
PhotobankController.AddRule                Contracts/                       UseCases/AddRule/
  [FromBody] AddRuleBody body  ──── maps ──▶ AddRuleBody (NEW)              AddRuleRequest  ── MediatR ──▶ AddRuleHandler

PhotobankController.RetagPhotos            Contracts/                       UseCases/RetagPhotos/
  [FromBody] RetagPhotosBody body ─ maps ──▶ RetagPhotosBody (NEW)          RetagPhotosRequest ── MediatR ▶ RetagPhotosHandler
```

The three new `*Body` types are pure DTOs (no behavior, no MediatR markers). The existing `*Request` types under `UseCases/` keep their `IRequest<TResponse>` contract — they remain the internal MediatR boundary that handlers depend on. The controller is the single mapping seam.

### Key Design Decisions

#### Decision 1: Folder casing — `Contracts/` (PascalCase), not `contracts/`
**Options considered:**
- Use lowercase `contracts/` as the brief and spec text suggest.
- Use PascalCase `Contracts/` to match the existing directory in this module.

**Chosen approach:** Place new files under the existing `backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/` folder (PascalCase) and namespace `Anela.Heblo.Application.Features.Photobank.Contracts`.

**Rationale:** The Photobank module's existing `*Body` DTOs (`AddPhotoTagBody.cs`, `BulkAddPhotoTagBody.cs`, `CreateTagBody.cs`, `BulkAddPhotoTagByIdsBody.cs`) all live under `Contracts/` with PascalCase. The spec's lowercase mention is a citation of the global guideline, not a directive to rename the folder. Consistency with the four well-formed siblings wins.

#### Decision 2: Mapping style — inline object initializer in the controller
**Options considered:**
- Inline object initializer in each action method (e.g. `new AddRootRequest { SharePointPath = body.SharePointPath, ... }`).
- Private mapper method on the controller (`AddRootRequest ToRequest(AddRootBody body)`).
- AutoMapper / dedicated mapper class in `Contracts/Mappers/`.

**Chosen approach:** Inline object initializer, matching the 4 existing well-formed endpoints in the same controller (e.g. `BulkAddPhotoTag` lines 161–166, `BulkAddPhotoTagByIds` lines 185–189).

**Rationale:** The mappings are 2–3 properties each, no transformations, no conditional logic. Adding a helper method or AutoMapper config for three trivial copies is over-engineering and breaks visual consistency with the existing controller. YAGNI applies.

#### Decision 3: Preserve action-specific response shapes
**Options considered:**
- Unify all three updated actions onto `HandleResponse(response)`.
- Preserve the current per-action response logic (`CreatedAtAction` for AddRoot/AddRule, `Accepted/BadRequest` for RetagPhotos).

**Chosen approach:** Preserve current per-action response logic exactly. Only the input-binding type and the mapping line change.

**Rationale:** The spec (FR-4) explicitly states *"HTTP route, verb, status codes, response type, attribute filters (auth, model validation, etc.), and action name remain unchanged."* `AddRoot` returns 201 via `CreatedAtAction(nameof(GetRoots), ...)`, `AddRule` does the same with `GetRules`, and `RetagPhotos` returns 202 via `Accepted(result)`. These status codes are part of the wire contract and must stay.

#### Decision 4: Do not refactor the MediatR request types
**Options considered:**
- Rename `AddRootRequest` → `AddRootCommand` and move them.
- Leave `UseCases/{X}/{X}Request.cs` intact.

**Chosen approach:** Leave `*Request` classes intact in `UseCases/`. They remain the MediatR internal contract.

**Rationale:** Spec out-of-scope explicitly forbids it. Handlers, validators, and tests already depend on these names; touching them blows up the blast radius for no architectural benefit.

## Implementation Guidance

### Directory / Module Structure

Add three files (no other structural changes):

```
backend/src/Anela.Heblo.Application/Features/Photobank/
└── Contracts/
    ├── AddPhotoTagBody.cs              (existing)
    ├── BulkAddPhotoTagBody.cs          (existing)
    ├── BulkAddPhotoTagByIdsBody.cs     (existing)
    ├── CreateTagBody.cs                (existing)
    ├── AddRootBody.cs                  ← NEW
    ├── AddRuleBody.cs                  ← NEW
    └── RetagPhotosBody.cs              ← NEW
```

Edit one file:

```
backend/src/Anela.Heblo.API/Controllers/PhotobankController.cs
  - AddRoot      (line 233) — bind AddRootBody, map to AddRootRequest
  - AddRule      (line 281) — bind AddRuleBody, map to AddRuleRequest
  - RetagPhotos  (line 203) — bind RetagPhotosBody, map to RetagPhotosRequest
```

All three new files use namespace `Anela.Heblo.Application.Features.Photobank.Contracts` (matching siblings) and live in the existing `Anela.Heblo.Application` project — no new csproj references needed.

### Interfaces and Contracts

```csharp
// Contracts/AddRootBody.cs
namespace Anela.Heblo.Application.Features.Photobank.Contracts
{
    public class AddRootBody
    {
        public string SharePointPath { get; set; } = null!;
        public string? DisplayName { get; set; }
        public string DriveId { get; set; } = null!;
    }
}

// Contracts/AddRuleBody.cs
namespace Anela.Heblo.Application.Features.Photobank.Contracts
{
    public class AddRuleBody
    {
        public string PathPattern { get; set; } = null!;
        public string TagName { get; set; } = null!;
        public int SortOrder { get; set; }
    }
}

// Contracts/RetagPhotosBody.cs
namespace Anela.Heblo.Application.Features.Photobank.Contracts
{
    public class RetagPhotosBody
    {
        public int[] PhotoIds { get; set; } = Array.Empty<int>();
        public bool ClearExistingAiTags { get; set; }
    }
}
```

Property names, types, nullability, and defaults are an exact 1:1 mirror of the current `*Request` shapes (verified against `AddRootRequest.cs`, `AddRuleRequest.cs`, `RetagPhotosRequest.cs`). This preserves the JSON wire contract bit-for-bit.

Controller body pattern (one example; the other two follow the same shape):

```csharp
public async Task<ActionResult<AddRootResponse>> AddRoot(
    [FromBody] AddRootBody body,
    CancellationToken cancellationToken = default)
{
    var request = new AddRootRequest
    {
        SharePointPath = body.SharePointPath,
        DisplayName = body.DisplayName,
        DriveId = body.DriveId,
    };
    var response = await _mediator.Send(request, cancellationToken);
    if (response.Success)
        return CreatedAtAction(nameof(GetRoots), response);
    return HandleResponse(response);
}
```

### Data Flow

For each of the three endpoints:

1. HTTP request hits the route (`POST /api/photobank/settings/roots`, etc.).
2. `[FeatureAuthorize]` runs — unchanged.
3. ASP.NET model binder deserializes the JSON body into the new `*Body` DTO.
4. Controller constructs a fresh `*Request` MediatR object from `body` (object initializer, no mutation).
5. `_mediator.Send(request, ct)` dispatches to the existing handler — unchanged.
6. Handler returns `*Response` — unchanged.
7. Controller picks the response status code as it does today (`CreatedAtAction` / `Accepted` / `HandleResponse`).

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Wire-shape drift between `*Body` and `*Request` introduced now or later | Medium | The new `*Body` is a 1:1 mirror today; add one controller-level test per endpoint asserting field-by-field copy (spec FR-6 covers this). A property added to `*Request` for server-side use must consciously be omitted from `*Body`. |
| Generated TypeScript client class rename (`AddRootRequest` → `AddRootBody`) breaks frontend imports | Low | Verified: the frontend never imports `AddRootRequest`, `AddRuleRequest`, or `RetagPhotosRequest` from `generated/api-client.ts`. All three endpoints are called via raw `apiPost` with inline JSON in `usePhotobank.ts` / `usePhotobankSettings.ts`. There is a locally-defined `RetagPhotosRequest` interface in `usePhotobank.ts:221` — it is unrelated to the generated client and unaffected. No frontend changes required. |
| Schema field renames (camelCase JSON) accidentally change due to NSwag inferring a different name from `*Body` | Low | All Photobank properties use trivial PascalCase that NSwag serializes deterministically to camelCase (`SharePointPath` → `sharePointPath`, etc.). Existing `*Body` siblings already exhibit this. Validate by running `dotnet build` and diffing `generated/api-client.ts` for the three request schemas. |
| OpenAPI `required` flags change (nullable behavior shift) | Medium | New `*Body` declarations must mirror nullability exactly: `string SharePointPath { get; set; } = null!;` (non-nullable, required), `string? DisplayName` (nullable, optional). Same for `AddRuleBody`, same for `RetagPhotosBody` (`int[] PhotoIds = Array.Empty<int>()` keeps the empty-array default). Diff the regenerated schema for `required: []` arrays. |
| `UpdateRule` (line 301) has the same coupling issue but is silently fixed or silently skipped | Low | Out of scope per spec. Explicitly do **not** touch `UpdateRule` in this PR. Arch-review routine will file it separately if it is a finding. |
| The `RetagPhotos` action currently uses `result.Success ? Accepted(result) : BadRequest(result)` — easy to drift to `HandleResponse` by mistake during the refactor | Low | Tester or reviewer to confirm 202/400 status codes survive in controller integration test. |

## Specification Amendments

1. **Folder casing**: The spec's `contracts/` references must be implemented as `Contracts/` (PascalCase) — that is the actual directory in this module, and all existing `*Body` siblings live there. Namespace is `Anela.Heblo.Application.Features.Photobank.Contracts`.

2. **Frontend impact is effectively zero, not "update generated type references"**: FR-5 acceptance criterion "any frontend call site that referenced the old generated type name is updated to use the new generated type name" is vacuously satisfied — the frontend does not reference `AddRootRequest`, `AddRuleRequest`, or `RetagPhotosRequest` from the generated client. The frontend calls these endpoints via raw `apiPost` with inline JSON payloads. The verification step still applies (regenerated client must compile cleanly, `npm run build` and `npm run lint` must pass), but no manual code-site updates are expected.

3. **Preserve `CreatedAtAction` / `Accepted` response semantics**: FR-4 should make explicit that `AddRoot` and `AddRule` keep their `CreatedAtAction(...)` on success / `HandleResponse(response)` on failure shape (current lines 238–240 and 286–288), and `RetagPhotos` keeps its `result.Success ? Accepted(result) : BadRequest(result)` shape (line 208). The spec's sample at "Controller pattern (target)" uses `Ok(result)` which would silently regress these status codes (201 → 200 for the Add* endpoints, 202 → 200 for RetagPhotos) — a wire contract break. The implementation must follow the pattern of the existing endpoints in the file (`CreatedAtAction` / `Accepted` are preserved), not the simplified sample.

4. **Test guidance — handler tests need no change**: FR-6 already implies this, but to be unambiguous: `AddRootHandlerTests`, `RetagPhotosHandlerTests`, etc. construct `*Request` directly and exercise the handler — they remain untouched. Only controller-level integration tests (if added per the per-endpoint mapping coverage requirement) consume the new `*Body` types.

## Prerequisites

None. No migrations, no config changes, no infrastructure work, no new packages, no new Azure Key Vault secrets. The change is purely additive (three new files) plus one controller edit. `dotnet build` regenerates the OpenAPI TypeScript client automatically per existing project configuration. Implementation can start immediately.