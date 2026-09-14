# Architecture Review: Journal `Title` field missing `[Required]` annotation in request DTOs

## Skip Design: true

## Architectural Fit Assessment
This is a minimal, surgical annotation fix that brings two existing DTO properties into line with a convention **already established in the same files**: `Content` on both `CreateJournalEntryRequest` and `UpdateJournalEntryRequest` already carries `[Required]` alongside `[MaxLength(...)]`. `Title` is the odd one out, despite being validated as required at every other layer (EF Core `IsRequired()`, handler `IsNullOrWhiteSpace` check, domain semantics). There is no new pattern being introduced — the fix is a direct application of the codebase's own existing convention to a property that was missed.

Per `docs/architecture/development_guidelines.md`, DTOs live in each module's `Contracts/` folder and are owned by that module (confirmed: both files are under `Features/Journal/Contracts/`). No FluentValidation validators exist for the Journal module (`AbstractValidator` search returned nothing under `Features/Journal/`), so `System.ComponentModel.DataAnnotations` attributes are the actual, sole validation-and-OpenAPI-schema mechanism for these two DTOs — confirming the suggested fix is the correct (and only) mechanism available here, not one of several competing options.

Swagger generation (`AddSwaggerServices` in `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`) uses stock `Swashbuckle.AspNetCore` `AddSwaggerGen` with no custom schema filter overriding `[Required]` handling, so Swashbuckle's default DataAnnotations-aware schema generation will correctly mark `title` as a required OpenAPI property once the attribute is added, which then flows through NSwag's TypeScript generation (`docs/development/api-client-generation.md`) to a non-optional TS property.

## Proposed Architecture

### Component Overview
No new components. The change touches exactly two existing leaf nodes in the request pipeline; nothing else in the pipeline is touched or needs to be:

```
[Client / generated TS client]
        │  POST/PUT JSON body
        ▼
JournalController (ASP.NET Core, [ApiController])
        │  model binding + automatic ModelState validation  ← [Required] now enforced here too
        ▼
CreateJournalEntryRequest / UpdateJournalEntryRequest        ← FIX: add [Required] to Title (this PR)
        │  (unchanged) MediatR dispatch
        ▼
CreateJournalEntryHandler / UpdateJournalEntryHandler        ← unchanged: still checks IsNullOrWhiteSpace(Title)
        │
        ▼
JournalEntry (domain) ──► JournalEntryConfiguration (EF Core, already IsRequired())   ← unchanged
```

### Key Design Decisions

#### Decision 1: Apply `[Required]` exactly as the issue suggests, with no additional changes
**Options considered:**
1. Add `[Required]` to `Title` in both DTOs only (as suggested in the issue).
2. Additionally suppress or customize ASP.NET Core's automatic `[ApiController]` model-validation response for these actions, so a missing `Title` still reaches the handler and returns the existing `ErrorCodes.InvalidJournalTitle` `BaseResponse` shape instead of the framework's default `ValidationProblemDetails`.
3. Do nothing beyond documenting the gap (reject the fix).

**Chosen approach:** Option 1 — add `[Required]` to `Title` on both DTOs, nothing else.

**Rationale:** `Content` already has `[Required]` today and is already subject to exactly the same automatic-model-validation behavior described in Option 2's concern. That behavior is therefore not a new risk this fix introduces — it is a pre-existing, already-accepted characteristic of these two DTOs and of `[ApiController]`-decorated controllers generally in this codebase (no evidence of `ApiBehaviorOptions.SuppressModelStateInvalidFilter` or a custom `InvalidModelStateResponseFactory` anywhere in `backend/src`, meaning this is the codebase's uniform, deliberate-by-omission choice, not something specific to Journal). Changing that global behavior is a separate, much larger architectural decision entirely out of scope for a one-field annotation fix, and is not requested by the issue. Option 3 leaves a genuine type-safety gap in place with no offsetting benefit.

## Implementation Guidance

### Directory / Module Structure
No new files, no new directories. Edit exactly two existing files in place:
- `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/CreateJournalEntryRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/UpdateJournalEntryRequest.cs`

### Interfaces and Contracts
On `Title` in both DTOs, add `[Required]` immediately above the existing `[MaxLength(200)]`, matching the attribute ordering already used on `Content` in the same files:

```csharp
[Required]
[MaxLength(200)]
public string Title { get; set; } = null!;
```

`System.ComponentModel.DataAnnotations` is already imported in both files (used for the existing `[MaxLength]`/`[Required]` attributes on other properties) — no new `using` directive is needed.

No other property, method, constructor, or class in either file changes. `UpdateJournalEntryRequest.Id` (int, no annotation) is untouched.

### Data Flow
Unchanged end-to-end except for one additional short-circuit point: a request whose JSON body has `title` missing, `null`, or an empty/whitespace-only string will now fail ASP.NET Core's automatic model-state validation at the controller boundary and return a 400 before MediatR dispatch, rather than reaching `CreateJournalEntryHandler`/`UpdateJournalEntryHandler` and returning their `ErrorCodes.InvalidJournalTitle` domain error. This mirrors exactly what already happens for a missing/blank `content` today (see Risks below). All other data flow (successful creates/updates, `AssociatedProducts`, `TagIds`, `EntryDate` handling, EF persistence) is untouched.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Response body shape for a missing/blank `title` changes from the handler's `BaseResponse` (`errorCode: InvalidJournalTitle`, HTTP 400) to ASP.NET Core's default `ValidationProblemDetails` (HTTP 400, `errors: { title: [...] }`) for requests that reach the API without client-side validation having already caught it. | Low | Not a new risk — `Content` already exhibits this exact behavior today via its own `[Required]`. `JournalEntryForm.tsx` already validates `Title` non-empty client-side before submit, so the normal UI path never sends a blank `title`. No mitigation needed beyond noting it; do not change controller/`ApiBehaviorOptions` behavior as part of this fix (out of scope, see Decision 1). |
| `Handle_WhenTitleIsNullOrWhitespace_ShouldReturnInvalidJournalTitleError` and the analogous Update test call the handler directly via unit test (bypassing MVC model binding), so they are unaffected by this change and will continue to pass. | Informational | No action needed; confirmed by reading both test files — they instantiate the handler and request object directly, never going through `JournalController`/ASP.NET Core model validation. |
| Regenerated TypeScript client (`frontend/src/api/generated/api-client.ts`) changes `title?: string` to a non-optional form for both DTOs, which could reveal latent frontend code that assumed `title` could be `undefined`. | Low | `frontend/src/components/JournalEntryForm.tsx` and `frontend/src/api/hooks/useJournal.ts` are the only two non-generated consumers (confirmed by search) and both already always supply a trimmed, non-empty `title` string when constructing these requests. No frontend code change is required or in scope. |

## Specification Amendments
None. `spec.r1.md` already documents the `[ApiController]`/automatic-validation nuance discussed above (see its Open Questions section) and correctly scopes the fix to the two DTO files with no other code changes.

## Prerequisites
None. No database migration is needed (the database column is already `NOT NULL` via `JournalEntryConfiguration`), no configuration change, no infrastructure change. The fix is ready to implement directly against the current `spec.r1.md`.
