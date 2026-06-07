# Specification: Stable User Identifier for Article Ownership

## Summary
Replace the display-name-based ownership check in the Article feedback flow with the stable user identifier (`CurrentUser.GetIdentifier()`). This eliminates a security/correctness defect where users with identical display names share article ownership and where renamed users permanently lose access to their own articles.

## Background
Articles generated via `GenerateArticleHandler` persist the requesting user's Azure AD **display name** into `Article.RequestedBy`. `SubmitArticleFeedbackHandler` later compares that stored value against the current user's display name to authorize feedback submission. Display names are neither unique nor immutable in Entra ID, so this check is both insecure (collisions grant unauthorized access) and fragile (renames lock legitimate owners out).

The codebase already exposes the correct abstraction: `CurrentUserExtensions.GetIdentifier()` returns `user.Id ?? user.Email ?? "system"` — a stable identifier specifically intended for ownership/audit comparisons. It is simply not used in this flow.

This defect was identified by the daily architecture-review routine on 2026-05-25.

## Functional Requirements

### FR-1: Store stable identifier at article creation
`GenerateArticleHandler` must persist the authenticated user's stable identifier (not the display name) into `Article.RequestedBy` when the user is authenticated. Unauthenticated requests continue to persist `null`.

**Acceptance criteria:**
- `GenerateArticleHandler.cs:46` uses `currentUser.GetIdentifier()` instead of `currentUser.Name`.
- For an authenticated user with `Id = "abc-123"`, a newly generated article has `RequestedBy = "abc-123"`.
- For an unauthenticated request, `RequestedBy` is `null`.
- A unit test covers both authenticated and unauthenticated cases.

### FR-2: Authorize feedback against stable identifier
`SubmitArticleFeedbackHandler` must compare `article.RequestedBy` against the current user's `GetIdentifier()` rather than `user.Name`. Behaviour on mismatch (return `ErrorCodes.Forbidden`) is unchanged.

**Acceptance criteria:**
- `SubmitArticleFeedbackHandler.cs:36` uses `user.GetIdentifier()` instead of `user.Name`.
- Submitting feedback as the article's owner (matching identifier) succeeds.
- Submitting feedback as a different user (mismatched identifier) returns `Forbidden`.
- Submitting feedback when `article.RequestedBy` is `null` continues to behave per existing semantics (verify and document — likely `Forbidden`).
- A unit test covers owner-success, non-owner-rejection, and null-owner cases.

### FR-3: Migrate existing article rows
Existing rows in the `Article` table store display names. After this change, those rows would be unowned (their `RequestedBy` would never match any user's stable identifier), permanently locking out the original authors. A migration must convert existing values to the corresponding stable identifier where unambiguously possible.

**Acceptance criteria:**
- A migration script is provided that:
  - Reads each non-null `RequestedBy` value.
  - Resolves it to a stable identifier via the available user directory (Entra/Graph) or an internal user table, whichever the project currently uses.
  - Updates the row in-place when the resolution is unambiguous (exactly one matching user).
  - Logs (does not fail) rows where resolution is ambiguous (multiple display-name matches) or impossible (no match) so they can be triaged.
- The migration is idempotent — re-running it on already-migrated rows is a no-op.
- Per the project rule that migrations are manual, the migration is documented (path, command, expected duration, rollback) but not auto-applied by deployment.

### FR-4: Audit other ownership comparisons against `RequestedBy`
Any other code paths that read `Article.RequestedBy` and compare it to user-derived data must be updated to use `GetIdentifier()` as well, or explicitly justified if they need the display name (e.g. for display-only purposes).

**Acceptance criteria:**
- A repository-wide search for `RequestedBy` is performed and each usage is classified as either "ownership check" (must use stable identifier) or "display" (may continue to use a name, but must resolve it from the identifier).
- Any ownership check that currently uses the display name is updated.
- Findings and decisions are recorded in the PR description.

## Non-Functional Requirements

### NFR-1: Security
The ownership check is an authorization gate. After this change, two distinct users with the same Azure AD display name must not be able to submit feedback on each other's articles. This must be verified by an integration test that simulates two users with identical `Name` but different `Id`.

### NFR-2: Backwards compatibility for owners
A user whose Entra display name changes between generating an article and submitting feedback must continue to be recognised as the owner. This must be verified by a test that mutates the simulated `CurrentUser.Name` while keeping `Id` constant.

### NFR-3: Data integrity during migration
The migration must not silently drop or corrupt data. Rows that cannot be resolved unambiguously must be left untouched and reported, never overwritten with a guess.

### NFR-4: Performance
No performance regression is expected. The comparison cost is unchanged; the migration is a one-off batch and is expected to touch fewer than 10,000 rows (verify against current row count before execution).

## Data Model
- **Article** (existing entity)
  - `RequestedBy : string?` — semantics change from "Azure AD display name at request time" to "stable user identifier (`Id ?? Email ?? "system"`) at request time". Column type and nullability are unchanged.
- **No new entities or relationships.**

Consider renaming the column to `RequestedById` in a follow-up to make the semantics self-documenting, but the rename is **out of scope** for this change (see Out of Scope).

## API / Interface Design
No public API surface changes.

- `POST` endpoints that generate articles: request and response shapes unchanged.
- `POST` endpoint that submits feedback: request, response, and error codes unchanged. Behaviour change is purely in the authorization predicate.

Internal changes:
- `GenerateArticleHandler.cs:46` — one-line swap.
- `SubmitArticleFeedbackHandler.cs:36` — one-line swap.
- One EF Core migration (data migration only, no schema change).

## Dependencies
- `CurrentUserExtensions.GetIdentifier()` (existing, no changes).
- `ICurrentUser.Id` populated correctly from the Entra OID claim (existing; verify the claim mapping is in place).
- Whatever user-directory mechanism the codebase uses for resolving display name → stable identifier during the migration (Microsoft Graph, an internal user cache, etc.) — needs confirmation during implementation.

## Out of Scope
- Renaming the `RequestedBy` column to `RequestedById` (cosmetic; do as a follow-up).
- Introducing a foreign-key relationship between `Article` and a user table.
- Reworking any other ownership/audit fields in unrelated modules — only `Article.RequestedBy` is in scope here, plus any direct comparison sites for the same field (FR-4).
- Changing the display of the requesting user in the UI (UI may continue to render a name; if it needs to look up a name from the new identifier, that's a separate concern).
- Backfilling rows whose display-name → identifier resolution is ambiguous; those are logged for manual triage.

## Open Questions
None.

## Status: COMPLETE