Now I have enough context to write the architecture review. The codebase has a critical pattern I need to flag: it uses a **custom `BaseResponse` error envelope with `ErrorCodes`**, not ASP.NET `ProblemDetails` or raw ModelState — and the existing `ErrorCodes.InvalidJournalTitle = 1602` is already defined but unused.

# Architecture Review: Journal Entry Title Validation Alignment

## Skip Design: true

No UI components, screens, or visual elements are added or changed. The frontend already enforces the required-title rule with existing styling; this work is a backend/contract/persistence alignment.

## Architectural Fit Assessment

The change is **structurally trivial** but sits on top of a non-obvious project convention that the spec gets slightly wrong. The spec proposes returning errors via **`[Required]` + ASP.NET ModelState/`ProblemDetails`** envelope (FR-1, FR-2, FR-6). This codebase does **not** use that pattern for business validation — it uses a custom `BaseResponse { Success, ErrorCode, Params }` envelope routed through `BaseApiController.HandleResponse(...)`, with an `ErrorCodes` enum and an `HttpStatusCodeAttribute` per code (see `backend/src/Anela.Heblo.API/Controllers/BaseApiController.cs:28-58` and `backend/src/Anela.Heblo.Application/Shared/BaseResponse.cs`).

Critically, **`ErrorCodes.InvalidJournalTitle = 1602` already exists** (`backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs:159`) and is unused — this is the codebase's intended slot for this exact rule. Falling back to raw ModelState would mean the Journal create/update endpoint returns a *different* error shape than every other Journal endpoint (auth → `UnauthorizedJournalAccess` via `BaseResponse`, not found → `JournalEntryNotFound` via `BaseResponse`). That inconsistency is what the spec is meant to prevent, not introduce.

Integration points are otherwise narrow: domain entity (`JournalEntry`), persistence configuration, two MediatR handlers, two DTOs, two read DTOs (`JournalEntryDto`, `SearchJournalEntryDto`), one EF migration, regenerated TypeScript client, and the existing form. The journal list already renders `title || "Bez názvu"` (`frontend/src/components/pages/Journal/JournalList.tsx:57`), which dictates the backfill placeholder choice.

## Proposed Architecture

### Component Overview

```
┌─────────────────────────────────────────────────────────────┐
│ Frontend                                                     │
│  JournalEntryForm.tsx (unchanged validation,                 │
│                        new server-error surfacing per FR-6)  │
│         │ POST/PUT /api/journal[/{id}]                       │
└─────────┼───────────────────────────────────────────────────┘
          │
┌─────────▼───────────────────────────────────────────────────┐
│ API Layer                                                    │
│  JournalController → MediatR.Send(request)                   │
│         │                                                    │
│         ▼                                                    │
│  BaseApiController.HandleResponse<T>()                       │
│   ↳ Success → 200/201                                        │
│   ↳ ErrorCode=InvalidJournalTitle → 400 + BaseResponse JSON  │
└─────────┼───────────────────────────────────────────────────┘
          │
┌─────────▼───────────────────────────────────────────────────┐
│ Application Layer (handlers)                                 │
│  CreateJournalEntryHandler                                   │
│  UpdateJournalEntryHandler                                   │
│   ↳ Guard: string.IsNullOrWhiteSpace(request.Title)          │
│       → return Response(ErrorCodes.InvalidJournalTitle)      │
│   ↳ entry.Update(title.Trim(), ...)  [no ?., title is NN]    │
└─────────┼───────────────────────────────────────────────────┘
          │
┌─────────▼───────────────────────────────────────────────────┐
│ Domain Layer                                                 │
│  JournalEntry { public string Title { get; set; } = null!; } │
│   ↳ Update(string title, ...)  [signature tightened]         │
└─────────┼───────────────────────────────────────────────────┘
          │
┌─────────▼───────────────────────────────────────────────────┐
│ Persistence                                                  │
│  JournalEntryConfiguration: .IsRequired() (was .IsRequired(false))
│  Migration: backfill NULL/'' → "Bez názvu", alter NOT NULL   │
└──────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Validate in handler with `ErrorCodes.InvalidJournalTitle`, not via `[Required]` + ModelState

**Options considered:**
- **A. `[Required]` on DTO + rely on auto ModelState 400** — what the spec asks for. Produces a `ValidationProblemDetails` shape inconsistent with the rest of the Journal endpoints' `BaseResponse` shape. The Journal controller does not call `ModelState.IsValid`, and the project has no global `ApiBehaviorOptions` override switching to `BaseResponse`, so the framework's default `ValidationProblemDetails` would be returned for this one validation path — breaking the consumer's single error-handling code path.
- **B. Validate inside the handler, return `BaseResponse(ErrorCodes.InvalidJournalTitle)`** — matches the existing pattern (`UnauthorizedJournalAccess`, `JournalEntryNotFound`). The `InvalidJournalTitle` code already exists and is unused (1602). `BaseApiController.HandleResponse` already maps `ErrorCodes` → HTTP status via `HttpStatusCodeAttribute`.
- **C. Both** — `[Required]` AND a handler guard. Redundant; first failure wins; the ModelState path still produces an inconsistent envelope.

**Chosen approach:** **B**. Keep `[StringLength(200)]` for max-length, drop the `[Required]` attribute, and validate `string.IsNullOrWhiteSpace(request.Title)` at the top of each handler before the auth check is reached on the happy path. Ensure `ErrorCodes.InvalidJournalTitle` carries `[HttpStatusCode(HttpStatusCode.BadRequest)]` (verify in the enum file; add if missing).

**Rationale:** Consistency with the rest of the Journal slice and with the project's documented `BaseResponse` pattern. The frontend already understands `BaseResponse.errorCode` shape; introducing a second error envelope for one field is exactly the kind of contract disagreement the brief is trying to eliminate.

#### Decision 2: Trim before persist, validate before trim

**Options considered:** Validate first vs. trim first.
**Chosen approach:** Validate `IsNullOrWhiteSpace(request.Title)` first; then `request.Title.Trim()`. The spec's FR-1 explicitly requires rejecting `"   "` — `[Required]` alone does not catch whitespace-only strings.
**Rationale:** A `[Required]` attribute treats `"   "` as valid. The handler-side guard with `IsNullOrWhiteSpace` is the only place where the whitespace rule is enforceable in a single, testable spot.

#### Decision 3: Domain entity non-nullability via `= null!` (not `required`)

**Options considered:** `public required string Title { get; set; }` vs. `public string Title { get; set; } = null!;`
**Chosen approach:** `public string Title { get; set; } = null!;` — matches the existing convention in this entity (`Content`, `CreatedByUserId`).
**Rationale:** Consistency. Mixing `required` and `= null!` in one entity is noisy and reads as if `Title` is somehow more important than `Content`. EF Core materialization compatibility is also smoother with the existing convention.

#### Decision 4: Backfill placeholder = `"Bez názvu"`

**Options considered:**
- `"(bez názvu)"` (spec's first suggestion)
- First ~50 chars of `Content` + ellipsis (spec's second suggestion)
- `"Bez názvu"` (matches existing frontend fallback)

**Chosen approach:** **`"Bez názvu"`** — exactly matches the string already rendered by the frontend for legacy null-title entries (`JournalList.tsx:57: title || "Bez názvu"`).
**Rationale:** Users already see this exact string for any null-title entry on the list page. Backfilling to the same string means the database state matches what users have visually associated with these entries. The content-prefix idea is rejected: it implies a meaningful title where there isn't one, and a partial sentence as a title is misleading.

#### Decision 5: Tighten read-DTO nullability (`JournalEntryDto.Title`, `SearchJournalEntryDto.Title`)

**Chosen approach:** Change both from `string?` to `string` with `= null!;`. Required so the generated TS client surfaces `title: string` everywhere consistently, including the form's "open existing entry" path. Mapper code (`JournalEntryMapper.cs:13` and `:43`) does not need to change — `entry.Title` is now non-null at the domain layer.

## Implementation Guidance

### Directory / Module Structure

No new files except the migration. All changes are surgical edits to existing files:

| File | Change |
|------|--------|
| `backend/src/Anela.Heblo.Domain/Features/Journal/JournalEntry.cs` | `Title` → `string` with `= null!;`; `Update(string title, …)` signature tightened; drop `?.Trim()` in `Update` body |
| `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/CreateJournalEntryRequest.cs` | `Title` → `string` with `= null!;`; **keep `[StringLength(200)]`**, **do not add `[Required]`** |
| `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/UpdateJournalEntryRequest.cs` | Same |
| `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/JournalEntryDto.cs` | `Title` → `string` with `= null!;` |
| `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/SearchJournalEntryDto.cs` | `Title` → `string` with `= null!;` |
| `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/CreateJournalEntry/CreateJournalEntryHandler.cs` | Add `IsNullOrWhiteSpace` guard returning `InvalidJournalTitle`; replace `request.Title?.Trim()` with `request.Title.Trim()` |
| `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/UpdateJournalEntry/UpdateJournalEntryHandler.cs` | Add same guard; pass `request.Title` to `entry.Update` (now non-null) |
| `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` | Verify `InvalidJournalTitle` has `[HttpStatusCode(HttpStatusCode.BadRequest)]`; add if missing |
| `backend/src/Anela.Heblo.Persistence/Journal/JournalEntryConfiguration.cs` | Line 18: `.IsRequired(false)` → `.IsRequired()` |
| `backend/src/Anela.Heblo.Persistence/Migrations/{timestamp}_RequireJournalEntryTitle.cs` | New EF migration; **manually edit** to insert backfill `UPDATE` between `Up()` ops |
| `frontend/src/components/JournalEntryForm.tsx` | Add post-mutate error surfacing for `errorCode === InvalidJournalTitle` under `setErrors(...)` (FR-6) |
| `backend/test/Anela.Heblo.Tests/Features/Journal/…` | Unit tests for both handlers' title-validation branches |
| `backend/test/Anela.Heblo.Tests/Features/Journal/…` (integration) | `WebApplicationFactory` test: 400 + `BaseResponse{ ErrorCode: InvalidJournalTitle }` |

### Interfaces and Contracts

**Domain (`JournalEntry`):**
```csharp
[Required]
[MaxLength(200)]
public string Title { get; set; } = null!;

public void Update(string title, string content, DateTime entryDate, string userId, string username)
{
    Title = title.Trim();
    Content = content.Trim();
    // ... rest unchanged
}
```

**Request DTOs (Create + Update):**
```csharp
[StringLength(200)]
public string Title { get; set; } = null!;
// Do NOT add [Required]; whitespace check belongs in the handler.
```

**Handler guard (both Create and Update), placed immediately after the auth check:**
```csharp
if (string.IsNullOrWhiteSpace(request.Title))
{
    return new {Create|Update}JournalEntryResponse(
        ErrorCodes.InvalidJournalTitle,
        new Dictionary<string, string> { { "field", "title" } });
}
```

**Error code (verify/add in `ErrorCodes.cs`):**
```csharp
[HttpStatusCode(HttpStatusCode.BadRequest)]
InvalidJournalTitle = 1602,
```

**Frontend server-error surface (`JournalEntryForm.tsx`, around the `catch` at line ~168):**
```typescript
catch (error) {
  if (isApiError(error, ErrorCodes.InvalidJournalTitle)) {
    setErrors((prev) => ({ ...prev, title: "Název je povinný" }));
    return;
  }
  console.error("Error saving journal entry:", error);
}
```
Use the existing error helper if one exists; otherwise inspect `error.response.data.errorCode` consistent with how the rest of the app surfaces `BaseResponse` errors.

### Data Flow

**Create / Update happy path:**
1. Form trims locally → POST/PUT JSON with non-empty `title`.
2. Controller binds DTO; `[StringLength(200)]` rejects over-length via auto ModelState (returns whatever the framework default is — acceptable because the frontend already enforces 200 chars via form state if needed; add a `maxLength={200}` on the input if not present).
3. MediatR handler runs auth check → title-guard → trims → constructs/updates entity → repo save.
4. Returns `Success=true` + entity id.

**Create / Update title-missing path (defense in depth):**
1. Bypass scenario: Postman, script, or future code path posts `{ title: "  " }`.
2. Handler guard short-circuits → `BaseResponse(InvalidJournalTitle)`.
3. `BaseApiController.HandleResponse` → `BadRequest(response)`.
4. Frontend catch branch surfaces the localized message on the title field (FR-6).

**Read path (no behavior change):**
- Repository returns entity with non-null `Title` (post-migration invariant).
- Mapper passes through.
- Form opens with a populated title, no false validation error on load.

**Migration path:**
1. `UPDATE public."JournalEntries" SET "Title" = 'Bez názvu' WHERE "Title" IS NULL OR trim("Title") = '';`
2. `ALTER COLUMN "Title" SET NOT NULL`.
3. `Down`: `ALTER COLUMN "Title" DROP NOT NULL`. Placeholder values remain — irreversible by design, documented in PR description.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Spec's `[Required]` + ModelState approach breaks `BaseResponse` envelope consistency | **High** | Decision 1 — validate in handler, use existing `ErrorCodes.InvalidJournalTitle`. Amend spec FR-1/FR-2/FR-6. |
| `[Required]` lets whitespace-only titles pass; spec requires rejecting them | High | Handler uses `IsNullOrWhiteSpace`, not the attribute. |
| Migration backfills nulls but misses empty-string rows | Medium | `UPDATE … WHERE "Title" IS NULL OR trim("Title") = ''` (covers null, empty, whitespace). |
| EF auto-generated migration includes only the `AlterColumn` and would fail on rows with NULL | Medium | Hand-edit the generated migration to put the `UPDATE` before the `AlterColumn`. Test on a staging snapshot first. |
| Existing integration / unit tests construct `JournalEntry` or DTOs with null title and break compilation | Low | Compilation surfaces all sites; fix per-test. Search: `new JournalEntry {`, `new CreateJournalEntryRequest {`, `new UpdateJournalEntryRequest {`. |
| `InvalidJournalTitle` enum value lacks `[HttpStatusCode]` attribute → maps to default `BadRequest` only by fallback | Low | Verify attribute is present; add if missing for explicitness. |
| Other consumers of `JournalEntry.Title` (e.g., mapper, search highlighter) assume nullable | Low | After tightening, `string? title` → `string title` propagates; compiler will surface. |
| Future external API consumer (none today) gets a contract break | Low | Documented in spec NFR-3; acceptable trade-off. |
| Form's post-error surfacing path doesn't already exist for this error shape | Low | Confirm with one test in `JournalEntryForm.test.tsx`; pattern can mirror any other error-handling location in the app. |

## Specification Amendments

The spec is sound in intent but needs three concrete corrections before implementation:

1. **FR-1 / FR-2 — replace `[Required]` + ModelState with handler-side validation returning `ErrorCodes.InvalidJournalTitle`.** Rationale: the project uses a `BaseResponse`/`ErrorCodes` envelope across all Journal endpoints; introducing raw ModelState 400s for one field breaks the consumer's single error-handling path. The code `InvalidJournalTitle = 1602` already exists and is unused — it was reserved exactly for this. Keep `[StringLength(200)]` on the DTO; drop the proposed `[Required]`.

2. **FR-6 — replace "model-state error keyed on `Title`" with "`BaseResponse.errorCode === InvalidJournalTitle`".** The frontend surfaces this code into the existing `errors.title` field using the same Czech message (`"Název je povinný"`) the client-side validator already shows.

3. **FR-4 — backfill placeholder is `"Bez názvu"`** (without parentheses). This is the exact string the journal list already renders for null-title entries (`JournalList.tsx:57`). The spec's two suggestions (`"(bez názvu)"` or content-prefix) both create new strings; using the existing one means migrated rows visually unchanged.

4. **Add FR-7 — `JournalEntry.Update(string title, …)` signature tightens to non-nullable.** Not in the original spec but a required consequence of FR-3 (the handler can no longer pass nullable to a method that's already trimming nullable).

5. **Add FR-8 — `JournalEntryDto.Title` and `SearchJournalEntryDto.Title` tighten to non-nullable** so the generated TS client surfaces `title: string` on the read path. Without this, the form's edit-mode open path still receives `title: string | undefined` from the client, leaving `setTitle(entryData.title || "")` defensive when it no longer needs to be — and any list/detail view that does `title ?? "Bez názvu"` becomes dead code.

## Prerequisites

- **Verify row count.** Before implementation: `SELECT COUNT(*) FROM public."JournalEntries" WHERE "Title" IS NULL OR trim("Title") = '';` in staging. The migration backfill must complete in under 5 seconds per NFR-1; current expectation is a single-digit-to-low-hundreds count given the app's volume, but confirm.
- **Confirm `[HttpStatusCode]` mapping** on `ErrorCodes.InvalidJournalTitle = 1602` in `ErrorCodes.cs`. If absent, add `[HttpStatusCode(HttpStatusCode.BadRequest)]` in the same edit.
- **No infrastructure / config changes.** No Key Vault secrets, no feature flag, no DI rewiring.
- **Manual migration application** per `CLAUDE.md` ("Database migrations are manual"). Run in staging, validate frontend edit-flow on a backfilled entry, then production.
- **OpenAPI client regeneration** happens automatically on `dotnet build` per project setup; the FE build (`npm run build`) will surface any compile breakages from tightened types — fix them in the same PR.