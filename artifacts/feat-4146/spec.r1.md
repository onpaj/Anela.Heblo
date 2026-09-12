# Specification: Journal `Title` field missing `[Required]` annotation in request DTOs

## Summary
`CreateJournalEntryRequest` and `UpdateJournalEntryRequest` both treat `Title` as required at the domain/handler level and at the database schema level, but the DTOs themselves are missing the `[Required]` data annotation on `Title`. This causes the auto-generated OpenAPI spec — and therefore the generated TypeScript client — to describe `title` as an optional property, silently disagreeing with the actual server-side contract. This spec covers adding the missing annotation so the generated contract matches reality.

## Background
Verified directly against the current codebase (2026-09-12):

- `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/CreateJournalEntryRequest.cs:11-12` — `Title` carries `[MaxLength(200)]` only.
- `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/UpdateJournalEntryRequest.cs:13-14` — same gap.
- In both DTOs, `Content` correctly carries `[Required]` alongside `[MaxLength(10000)]` (Create lines 14-16, Update lines 16-18), establishing the existing convention this fix brings `Title` in line with.
- `backend/src/Anela.Heblo.Persistence/Journal/JournalEntryConfiguration.cs:16-18` — EF Core configuration marks `Title` `.HasMaxLength(200).IsRequired()`.
- `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/CreateJournalEntry/CreateJournalEntryHandler.cs:39-43` and `.../UpdateJournalEntry/UpdateJournalEntryHandler.cs:48-53` both explicitly check `string.IsNullOrWhiteSpace(request.Title)` and return `ErrorCodes.InvalidJournalTitle` as a domain error (routed through `BaseApiController.HandleResponse`, which maps it to an HTTP 400 with a `BaseResponse`-shaped body carrying `errorCode`/`params`).
- `frontend/src/api/generated/api-client.ts:24578` (generated `CreateJournalEntryRequest` class) currently emits `title?: string;` (optional) versus `content!: string;` (required) — confirming the OpenAPI-spec-to-TypeScript optionality gap described in the issue. The same pattern exists in the generated `UpdateJournalEntryRequest`.
- `frontend/src/components/JournalEntryForm.tsx:112-113,170-171` already enforces `Title` as required client-side (`"Název je povinný"`) before submit, and separately handles the `ErrorCodes.InvalidJournalTitle` domain error from the API. This existing UI logic is unaffected by the fix.
- `backend/src/Anela.Heblo.API/Controllers/JournalController.cs:11` — the controller carries `[ApiController]`, which enables ASP.NET Core's automatic model-state validation. No `ApiBehaviorOptions.SuppressModelStateInvalidFilter` or custom `InvalidModelStateResponseFactory` is configured anywhere in `backend/src` (verified by search), so the default pipeline behavior applies. This is a pre-existing characteristic of the DTOs today via `Content`'s `[Required]`; adding `[Required]` to `Title` extends that same, already-accepted behavior rather than introducing a new one (see Open Questions / architect follow-up).

## Functional Requirements

### FR-1: Add `[Required]` to `Title` on `CreateJournalEntryRequest`
Add the `System.ComponentModel.DataAnnotations.RequiredAttribute` to the `Title` property of `CreateJournalEntryRequest`, ordered above `[MaxLength(200)]` to match the existing ordering convention used on `Content` (`[Required]` then `[MaxLength(...)]`).

**Acceptance criteria:**
- `Title` on `CreateJournalEntryRequest` carries both `[Required]` and `[MaxLength(200)]`.
- The regenerated OpenAPI spec marks `title` as a required property of `CreateJournalEntryRequest`.
- The regenerated TypeScript client emits `title!: string;` (or equivalent non-optional form) instead of `title?: string;` for `CreateJournalEntryRequest`.
- No change to `MaxLength`, property type, or any other property on the DTO.

### FR-2: Add `[Required]` to `Title` on `UpdateJournalEntryRequest`
Same change as FR-1, applied to `UpdateJournalEntryRequest.Title`.

**Acceptance criteria:**
- `Title` on `UpdateJournalEntryRequest` carries both `[Required]` and `[MaxLength(200)]`.
- The regenerated OpenAPI spec marks `title` as a required property of `UpdateJournalEntryRequest`.
- The regenerated TypeScript client emits `title!: string;` (or equivalent non-optional form) instead of `title?: string;` for `UpdateJournalEntryRequest`.
- No change to `Id`, `MaxLength`, property type, or any other property on the DTO.

### FR-3: Preserve existing runtime validation and tests
The existing handler-level `string.IsNullOrWhiteSpace(request.Title)` checks and their `ErrorCodes.InvalidJournalTitle` domain-error responses remain in place unchanged. No handler, domain, or controller code is modified.

**Acceptance criteria:**
- `CreateJournalEntryHandlerTests` and `UpdateJournalEntryHandlerTests` (including the existing `[Theory]` cases for `null`/whitespace `Title`, e.g. `Handle_WhenTitleIsNullOrWhitespace_ShouldReturnInvalidJournalTitleError`) continue to pass unmodified, since these tests call the handler directly (bypassing ASP.NET Core model binding/validation) and are unaffected by the DTO annotation change.
- No existing test is modified as part of this change.

## Non-Functional Requirements

### NFR-1: Contract accuracy
The generated OpenAPI spec and TypeScript client must accurately reflect server-side validation requirements for `Title`, consistent with `Content`, `EntryDate`, and the EF Core schema. This eliminates a class of bug where the type system gives no compile-time signal for a field the backend treats as mandatory.

### NFR-2: No behavioral regression
The change must not alter any HTTP status code, error code, or response body currently produced by valid requests, nor the handler's domain-level rejection of a request with a genuinely missing/blank `Title` that does reach the handler (e.g., if some future caller bypasses model binding). See Open Questions for the one caller-visible nuance this DTO-only change surfaces.

## Data Model
No changes. `JournalEntry.Title` (domain entity) and its `JournalEntryConfiguration` EF Core mapping (`HasMaxLength(200).IsRequired()`) are already correct and untouched by this fix — only the two Application-layer request DTOs gain the annotation that was already implied by the domain/db layers.

## API / Interface Design
- `POST` journal-entry-create endpoint (`CreateJournalEntryRequest`): `title` becomes a required string property in the OpenAPI schema (was optional).
- `PUT`/`PATCH` journal-entry-update endpoint (`UpdateJournalEntryRequest`): `title` becomes a required string property in the OpenAPI schema (was optional).
- No endpoint routes, verbs, response shapes, or other request properties change.
- The TypeScript client regenerates automatically on build per project convention (`docs/development/api-client-generation.md`); no manual edits to `frontend/src/api/generated/api-client.ts` are needed or expected as part of this change — regeneration is a build-time side effect.

## Dependencies
- `frontend/src/api/generated/api-client.ts` is auto-generated from the OpenAPI spec on build; this fix depends on that regeneration step running (already part of standard build, per `docs/development/api-client-generation.md`) to actually surface the corrected TypeScript types. No manual regeneration instructions are needed beyond the normal build.
- No other feature, module, or external service depends on `Title` being optional in these two DTOs.

## Out of Scope
- Any change to `CreateJournalEntryHandler` / `UpdateJournalEntryHandler` validation logic.
- Any change to `JournalEntry` domain entity or `JournalEntryConfiguration`.
- Any change to `JournalController` or `BaseApiController` response-mapping behavior.
- Any change to `JournalEntryForm.tsx` or other frontend consumers.
- Manually editing the generated `api-client.ts` file (it is regenerated on build).
- Adding new tests. Existing tests already cover the handler-level required-title behavior (FR-3) and are expected to keep passing as-is.

## Open Questions
None. One architectural nuance worth the architect's attention (not a blocker, and not something this spec asks to be resolved differently): once `Title` carries `[Required]`, ASP.NET Core's `[ApiController]` automatic model-state validation (active on `JournalController`, unmodified elsewhere in the codebase) will short-circuit a request with a missing/blank `title` in the JSON body before it reaches the MediatR handler, returning the framework's default `ValidationProblemDetails` 400 response instead of the handler's `ErrorCodes.InvalidJournalTitle` `BaseResponse`-shaped 400. This is not a new risk introduced by this fix — `Content` already carries `[Required]` today and is subject to the identical framework behavior — so `Title` is simply being brought in line with the codebase's existing, already-accepted pattern rather than introducing a new behavior. Flagged here for the architect to confirm/document rather than treated as something this spec needs to change course over.

## Status: COMPLETE
