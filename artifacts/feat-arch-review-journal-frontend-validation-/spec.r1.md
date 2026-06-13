# Specification: Journal Entry Title Validation Alignment

## Summary
The Journal module has inconsistent validation rules between the frontend and backend: the React form requires a non-empty title while the backend contract and domain entity treat title as optional. This specification resolves the disagreement by making title **required** at all layers (Option A from the brief), aligning the contract with existing frontend behavior and preventing silent data traps when title-less entries are loaded into the edit modal.

## Background
`JournalEntryForm.tsx` enforces a non-empty `title` via a client-side check (`frontend/src/components/JournalEntryForm.tsx:111-113`). The backend declares `Title` as nullable in both the request DTO (`CreateJournalEntryRequest.cs:12`) and the domain entity (`JournalEntry.cs:14`), and the handler stores it with `request.Title?.Trim()`. As a result, an entry created without a title (via API, script, or import) becomes uneditable in the UI: opening it in the modal immediately triggers `"Název je povinný"` and the save button stays inert until the user invents a title — even when they only intended to modify content or tags.

Surfaced by the daily architecture review routine on 2026-06-10, this is a contract-layer disagreement, not a bug in either layer alone. The chosen resolution is **Option A — title is required by design** — because the UI has always treated title as mandatory, the field is the primary human-readable identifier of an entry in lists and detail views, and tightening the contract is safer than loosening the UI and discovering downstream display assumptions later.

## Functional Requirements

### FR-1: Title required in backend contract
The `CreateJournalEntryRequest` DTO must declare `Title` as a required, non-nullable string with explicit validation.

**Acceptance criteria:**
- `CreateJournalEntryRequest.Title` is typed as `string` (not `string?`) and decorated with `[Required]` and `[StringLength]` matching the existing maximum (verify against current schema; assume 200 chars unless an existing constant says otherwise).
- A POST to the create endpoint with `Title = null`, `Title = ""`, or `Title = "   "` (whitespace-only) returns HTTP 400 with a model-state error keyed on `Title`.
- The error message returned is human-readable and matches existing validation style in the Journal feature.
- The OpenAPI schema regenerates with `title` marked as `required` in the request body schema.

### FR-2: Title required in update contract
Any update/edit DTO for journal entries (e.g. `UpdateJournalEntryRequest` or equivalent) must mirror the same required-title constraint as the create DTO.

**Acceptance criteria:**
- The update request DTO has `Title` typed as `string` with `[Required]` and the same `[StringLength]` constraint.
- A PUT/PATCH with empty or whitespace-only title returns HTTP 400 with a `Title` model-state error.
- If no separate update DTO exists today, this requirement still applies to whatever payload the edit endpoint accepts.

### FR-3: Title required in domain entity
The `JournalEntry` domain entity must declare `Title` as a non-nullable string. Domain-level invariants must prevent persisting an entry without a title.

**Acceptance criteria:**
- `JournalEntry.Title` is `string` (not `string?`) with a non-null default (typically set via constructor or required-property syntax).
- Constructing a `JournalEntry` without a title, or assigning null/empty/whitespace to `Title`, fails at compile time (via `required`/non-nullable) or at runtime with a clear domain exception.
- Application handlers no longer use the null-conditional operator on `Title` (i.e. `request.Title?.Trim()` becomes `request.Title.Trim()`).

### FR-4: Database persistence reflects non-null title
The database column backing `JournalEntry.Title` must be `NOT NULL`.

**Acceptance criteria:**
- A new EF Core migration is generated that:
  - Backfills any existing rows where `Title IS NULL` or `Title = ''` with a deterministic placeholder (e.g. `"(bez názvu)"` or the first ~50 characters of the entry content with an ellipsis — see Open Questions).
  - Alters the `Title` column to `NOT NULL`.
- The migration is reversible: the `Down` method restores the column to nullable without data loss (placeholder values remain).
- Migration is manual per project convention (not auto-applied in deployment) and is documented in the PR description.

### FR-5: Frontend validation behavior is unchanged but aligned
The existing client-side validation in `JournalEntryForm.tsx` remains as the first line of defense; no UI behavior changes for the user.

**Acceptance criteria:**
- `frontend/src/components/JournalEntryForm.tsx:111-113` continues to block save when title is empty/whitespace and displays `"Název je povinný"`.
- The error rendering at line ~254 (`errors.title`) is unchanged.
- When a legacy entry (pre-migration backfill) is opened in the edit modal, the backfilled title displays correctly and the form does not produce a misleading validation error on initial open.
- The OpenAPI-generated TypeScript client reflects the new required-title contract; no consumer of the client breaks (verify by `npm run build`).

### FR-6: Server-side validation surfaces in UI
If a future code path bypasses client validation, the server's 400 response must produce a user-visible error in the form rather than a silent failure.

**Acceptance criteria:**
- When a POST/PUT to the journal endpoint returns a 400 with a `Title` model-state error, the form displays the error under the title field using the existing error rendering pattern.
- The existing API error handling in the form is reviewed for compatibility; no new global error surfaces are required.

## Non-Functional Requirements

### NFR-1: Performance
No measurable performance impact expected. The migration's backfill must execute in under 5 seconds for the current journal entry volume (verify approximate row count before running in staging).

### NFR-2: Security
No new security surface. Existing authorization on journal endpoints (per the Journal feature slice) remains unchanged. Validation errors must not leak schema internals beyond standard ASP.NET model-state messages.

### NFR-3: Backwards compatibility
- Existing journal entries with non-null, non-empty titles are unaffected.
- Existing entries with null or empty titles are backfilled with a placeholder during migration so they remain accessible.
- No external API consumer is documented (the journal endpoints are UI-facing only), so the contract tightening is acceptable. If any external integration is later identified, it would need to start sending a title.

### NFR-4: Testability
- Backend unit tests cover the validation behavior of the create and update DTOs (empty, whitespace, null, valid).
- Backend integration tests cover the 400 response from the endpoints when title is missing.
- Frontend unit tests for `JournalEntryForm` cover the title-required validation (likely already exist — extend if needed).
- The migration is verified manually in staging before production application.

## Data Model

**Entity: `JournalEntry`** (`backend/src/Anela.Heblo.Domain/Features/Journal/JournalEntry.cs`)
- **Before:** `public string? Title { get; set; }`
- **After:** `public string Title { get; set; } = default!;` (or use `required` modifier per project convention)
- Other fields unchanged (content, tags, timestamps, etc.).

**Database column:**
- **Before:** `Title` nullable text/varchar
- **After:** `Title` NOT NULL, same length as current declared `[StringLength]`

No new entities, relationships, or indexes are introduced.

## API / Interface Design

**Endpoints affected** (Journal vertical slice):
- `POST /api/journal` (create) — request body schema changes: `title` becomes required.
- `PUT /api/journal/{id}` or equivalent update endpoint — request body schema changes: `title` becomes required.
- `GET` endpoints — response schema changes: `title` is now non-nullable in returned DTOs.

**Validation errors:**
- 400 response with standard ASP.NET `ProblemDetails` or model-state envelope, keyed on `Title`.

**OpenAPI / TypeScript client:**
- Regenerated client reflects required `title` in request and response types.
- Consumers (`JournalEntryForm`, list views, detail views) compile cleanly against the new types; any `title?: string` usages tighten to `title: string`.

**UI flows:**
- Create flow: unchanged — user enters title before save, save succeeds.
- Edit flow: unchanged for valid entries; legacy null-title entries now show backfilled placeholder instead of triggering a save block.
- No new screens, modals, or routes.

## Dependencies

- **EF Core migration tooling** — to generate and apply the schema change. Migrations are manual in this project.
- **NSwag / OpenAPI generator** — to regenerate the TypeScript client (auto-runs on build per project setup).
- **Existing Journal feature slice** — handler, repository, and contracts under `backend/src/Anela.Heblo.Application/Features/Journal/`.
- No external services, no new NuGet or npm packages.

## Out of Scope

- Changing the **content** field's nullability or validation rules.
- Changing the **tags** field's validation or schema.
- Adding new fields to `JournalEntry`.
- Redesigning the journal entry edit modal UX (only the validation alignment is in scope).
- Adding undo/restore for the migration backfill — the placeholder is permanent unless the user edits it.
- Audit logging of the migration backfill rows (the migration's existence in source control is sufficient traceability).
- Bulk-edit tooling for users to update placeholder titles after migration — users can edit them one at a time using the existing UI.
- Localization of the backfill placeholder for other languages — the app is Czech-only today.

## Open Questions

None.

## Status: COMPLETE