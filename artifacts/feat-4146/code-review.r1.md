# Code Review: Journal `Title` field missing `[Required]` annotation in request DTOs

## Review Result: CLEAN

## Scope Reviewed
Diff against `main` (merge-base `5b6465b951e98976bfb8e77dbf4707adf7513a1a`), non-artifact files only:
- `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/CreateJournalEntryRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/UpdateJournalEntryRequest.cs`
- `frontend/src/api/generated/api-client.ts` (generated, build-time output)

## Plan Alignment Analysis
The implementation matches `spec.r1.md`, `arch-review.r1.md`, and `design.r1.md` exactly, with zero deviation:
- `[Required]` added above the existing `[MaxLength(200)]` on `Title` in both `CreateJournalEntryRequest` and `UpdateJournalEntryRequest`, matching the ordering and style already used for `Content` in the same classes (FR-1, FR-2 satisfied verbatim).
- No handler, domain, controller, or EF Core configuration code touched (FR-3 / Out of Scope honored).
- `frontend/src/api/generated/api-client.ts` changed only in the four expected spots (`title?: string` → `title!: string` on the two classes, `title?: string` → `title: string` on the two interfaces), consistent with the `content` pattern already present in the same file. No hand-editing of the generated file — it is the expected build-time regeneration artifact (NFR-1).
- `Id` on `UpdateJournalEntryRequest`, `MaxLength`, property types, and all other properties are untouched.

## Code Quality Assessment
- `System.ComponentModel.DataAnnotations` was already imported in both files (used by the pre-existing `[MaxLength]`/`[Required]` attributes), so no new `using` was needed and none was added.
- Attribute ordering (`[Required]` before `[MaxLength(200)]`) matches the codebase's own established convention on `Content` in the same two files — no new convention introduced.
- The three developer-task artifacts (`impl/add-required-title-create.r1.md`, `impl/add-required-title-update.r1.md`, `impl/verify-build-and-contract-regen.r1.md`) document that the pre-existing `CreateJournalEntryHandlerTests` / `UpdateJournalEntryHandlerTests` (which call handlers directly, bypassing model binding) continued to pass unmodified, and that the full Journal-scoped suite (108 tests), `dotnet build`, `dotnet format --verify-no-changes`, `npm run build`, and `npm run lint` all passed with no new warnings/errors attributable to this change.

## Architecture and Design Review
- This is a two-line annotation change applied uniformly to two sibling DTOs already following an established `[Required]`+`[MaxLength]` pattern on their `Content` property — no architectural pattern is introduced, bent, or violated.
- The one behavioral nuance identified during specification/architecture (that `[ApiController]`'s automatic model-state validation will now short-circuit a missing/blank `title` before it reaches the handler's `ErrorCodes.InvalidJournalTitle` path, returning the framework's default `ValidationProblemDetails` instead) was explicitly raised, analyzed, and accepted at the spec and arch-review stages as matching `Content`'s already-existing, already-shipped behavior — not a new risk this change introduces. `JournalEntryForm.tsx` already enforces non-empty `Title` client-side, so the normal UI path is unaffected. Nothing further needed here.

## Documentation and Standards
N/A for a two-attribute annotation change — no new public surface requiring doc comments beyond what already existed on `Content`.

## Issues
- **Critical:** None.
- **Important:** None.
- **Suggestions:** None.

## Blocking
- None

## Advisory
- None beyond what `arch-review.r1.md` already flagged and accepted (the `[ApiController]` model-validation short-circuit for `title`, symmetric with `Content`'s existing behavior) — no further action needed.

## Summary
Minimal, surgical, fully spec-aligned fix. Build, tests, format, lint, and contract regeneration were all verified by the developer tasks and independently re-confirmed by re-reading the diff and the two edited files directly. No findings.
