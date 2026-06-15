# Specification: Correct Authorization Role in Article Generation Feature Doc

## Summary
Update `docs/features/article-generation.md` section 14 to reference the `marketing_reader` role (matching the actual implementation in `ArticlesController.cs`) instead of the incorrect `marketing_writer` role. Documentation-only change; no code modifications required.

## Background
A three-way inconsistency exists between the feature spec, the `AuthorizationConstants` XML doc comments, and the controller implementation for `POST /api/Articles/generate`:

- **Implementation** (`backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs:29`) uses `[Authorize(Policy = AuthorizationConstants.Policies.MarketingReader)]`, which resolves to the `marketing_reader` role (confirmed in `AuthenticationExtensions.cs:116–118`).
- **`AuthorizationConstants.cs`** (lines 41 and 47) — XML comments agree with the code: `MarketingReader` is for "generating leaflets and articles (GenAI features)", `MarketingWriter` is for "tagging photos in the photobank".
- **Feature doc** (`docs/features/article-generation.md` section 14) incorrectly states `marketing_writer` is the required role and that it replaced the earlier `article_generator` role.

The feature doc is the authoritative reference for the Article module. A developer using it to scaffold a new endpoint, assign Azure AD roles, or onboard a user would grant the wrong role, locking users out of article generation. The discrepancy is most likely to surface as a production access incident.

## Functional Requirements

### FR-1: Correct the role name in section 14 prose
The prose paragraph in `docs/features/article-generation.md` section 14 that currently describes `marketing_writer` as the required role must be replaced with text describing `marketing_reader`.

**Acceptance criteria:**
- The line referencing `marketing_writer` as the role for article generation is removed from section 14.
- The replacement text names `marketing_reader` as the role assigned to Heblo users with GenAI content-generation responsibilities (admin-assigned).
- No other occurrence of `marketing_writer` in connection with article generation remains in the document.

### FR-2: Correct the `[Authorize]` code snippet in section 14
The illustrative C# snippet in section 14 must reflect the actual attribute used on the controller.

**Acceptance criteria:**
- The snippet `[Authorize(Roles = "marketing_writer")] on POST /generate` is replaced with `[Authorize(Policy = AuthorizationConstants.Policies.MarketingReader)] on POST /generate`.
- The snippet matches the attribute on `ArticlesController.Generate` at `backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs:29` verbatim (policy-based, not role-based).

### FR-3: Correct the closing historical note in section 14
The closing note that references `marketing_writer` as the replacement for `article_generator` must be corrected to reference `marketing_reader`.

**Acceptance criteria:**
- The sentence "The earlier `article_generator` role was removed and replaced by the unified `marketing_writer` role." is replaced with "The earlier `article_generator` role was removed. Access is granted via the `marketing_reader` role (`AuthorizationConstants.Roles.MarketingReader`)."
- The note no longer implies any role unification with `marketing_writer`.

### FR-4: Document-wide consistency sweep
Verify the rest of `docs/features/article-generation.md` does not introduce the same inconsistency outside section 14.

**Acceptance criteria:**
- Every reference to the role that authorizes `POST /api/Articles/generate` anywhere in the document names `marketing_reader` (not `marketing_writer` or `article_generator`).
- Any table, sequence diagram caption, or sidebar that names the role is updated to match.
- A grep for `marketing_writer` in `docs/features/article-generation.md` returns no matches in the context of article generation. (If the term appears in some unrelated context — e.g., a comparison list — it is left intact only if clearly not about the generate endpoint.)

## Non-Functional Requirements

### NFR-1: Accuracy
The updated documentation must be byte-accurate to the implementation: the policy name, constant path, and role string must all match `ArticlesController.cs:29`, `AuthorizationConstants.cs` (lines 41, 47), and `AuthenticationExtensions.cs:116–118` as of the commit that introduces the doc change.

### NFR-2: Traceability
The PR description must link to:
- The controller line that defines the authorization policy
- The `AuthorizationConstants` line that defines `MarketingReader`
- The original brief (the arch-review finding from 2026-05-29)

### NFR-3: Reversibility
Because this is a documentation-only change, no migrations, feature flags, or rollout coordination are required. The change can be merged immediately after review.

## Data Model
N/A — no data model, schema, or persisted state is touched. The change is confined to a single markdown file.

## API / Interface Design
N/A — no API surface changes. The documentation is being aligned with the existing API:

- **Endpoint:** `POST /api/Articles/generate`
- **Authorization (existing, unchanged):** `[Authorize(Policy = AuthorizationConstants.Policies.MarketingReader)]`
- **Required role (existing, unchanged):** `marketing_reader`

## Dependencies
- Read access to `backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs` (to confirm current attribute)
- Read access to `backend/src/Anela.Heblo.Domain/Features/Authorization/AuthorizationConstants.cs` (to confirm constant values)
- Write access to `docs/features/article-generation.md`

No external services, libraries, or new packages are required.

## Out of Scope
- **Code changes.** The controller is already correct; do not modify `ArticlesController.cs`, `AuthorizationConstants.cs`, or `AuthenticationExtensions.cs`.
- **Renaming roles.** Do not propose renaming `marketing_reader` to a more descriptive name; that is a separate architectural discussion.
- **Reviewing other feature docs** for similar inconsistencies. Only `docs/features/article-generation.md` is in scope. If similar drift is suspected elsewhere, file a follow-up arch-review finding.
- **Azure AD role assignment changes.** Operations on the directory side are not affected.
- **Updating the `MarketingWriter` constant's XML comment.** It already correctly describes the photobank-tagging usage.

## Open Questions
None.

## Status: COMPLETE