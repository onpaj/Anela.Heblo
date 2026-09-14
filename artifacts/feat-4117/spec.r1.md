# Specification: Remove framework coupling from `RecurringJobConfiguration` domain entity

## Summary
`RecurringJobConfiguration` (in `Anela.Heblo.Domain`) references `System.ComponentModel.DataAnnotations` for two unrelated purposes: decorative `[Required]`/`[MaxLength]` attributes that duplicate the EF Core Fluent API configuration, and `ValidationException` thrown for constructor/method-argument guard checks. Both couple the innermost layer of Clean Architecture to a framework namespace. This is a mechanical refactor: strip the redundant attributes, swap the exception type, and remove the now-unused `using`.

## Background
The project follows Clean Architecture (per `docs/architecture/📘 Architecture Documentation – MVP Work.md` and `docs/architecture/development_guidelines.md`): the Domain layer must not depend on infrastructure or framework packages. `RecurringJobConfigurationConfiguration` (`backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationConfiguration.cs`) already declares `HasMaxLength(...)` / `IsRequired()` via EF Core's Fluent API for every string property on the entity — that is the single source of truth EF actually reads; the `[Required]`/`[MaxLength]` attributes on the entity are inert markup that could silently drift from the real DB constraint. Separately, the entity's guard clauses throw `System.ComponentModel.DataAnnotations.ValidationException`, an ASP.NET/data-binding type, for plain "argument was null/whitespace" checks — these are BCL-argument-style checks, not framework model-validation.

This finding was filed by the daily arch-review routine (`docs/architecture/...` review process) on 2026-09-09 and is being turned into an actionable spec.

## Functional Requirements

### FR-1: Remove redundant `DataAnnotations` attributes from the entity
Remove `[Required]` and `[MaxLength(n)]` attributes from all seven decorated properties on `RecurringJobConfiguration` (`backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs`): `JobName`, `DisplayName`, `Description`, `CronExpression`, `TimeZoneId`, `LastModifiedBy`. (`IsEnabled` and `LastModifiedAt` are not decorated today and are unaffected.)

The persistence-layer Fluent API configuration in `RecurringJobConfigurationConfiguration` (`backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationConfiguration.cs`) already declares matching `HasMaxLength(...)` + `IsRequired()` calls for every one of these properties (confirmed present and matching lengths as of this spec: `JobName` 100, `DisplayName` 200, `Description` 500, `CronExpression` 50, `TimeZoneId` 100, `LastModifiedBy` 100) — no changes are needed there; it remains the single source of truth for storage-level constraints.

**Acceptance criteria:**
- No property on `RecurringJobConfiguration` carries a `[Required]` or `[MaxLength]` attribute.
- The `using System.ComponentModel.DataAnnotations;` import is removed from the file (see FR-3).
- `RecurringJobConfigurationConfiguration` is unchanged — its Fluent API constraints remain the sole source of the DB-level length/required rules.
- EF migrations are unaffected: since EF Core ignores entity-level `DataAnnotations` here (Fluent API takes precedence), removing the attributes produces no pending model changes (verify with `dotnet ef migrations add --dry-run`-equivalent check, e.g. `dotnet ef migrations has-pending-model-changes` or by confirming no new migration is generated).

### FR-2: Replace `ValidationException` with a framework-independent exception
Replace every `throw new ValidationException("...")` in `RecurringJobConfiguration` with `throw new ArgumentException("...")`. This affects the guard clauses in:
- the public constructor (6 checks: `JobName`, `DisplayName`, `Description`, `CronExpression`, `TimeZoneId`, `LastModifiedBy`),
- `UpdateConfiguration` (5 checks: `DisplayName`, `Description`, `CronExpression`, `TimeZoneId`, `ModifiedBy`),
- `Enable` (1 check: `ModifiedBy`),
- `Disable` (1 check: `ModifiedBy`),
- `UpdateCronExpression` (2 checks: `CronExpression`, `ModifiedBy`).

(14 throw sites total, matching the brief's line count though not necessarily its exact line numbers — verified by direct reading of the current file.)

No domain-specific base exception type (e.g. a `DomainException`) exists in `Anela.Heblo.Domain.Shared` or elsewhere in the Domain project to reuse here. Existing Domain-layer exceptions in this codebase are inconsistent: some extend `System.Exception` directly (e.g. `InvalidPhotoSearchPatternException`, `GridLayoutPersistenceException`), while `TransportBoxExceptions.cs` extends `System.ComponentModel.DataAnnotations.ValidationException` (the same anti-pattern this spec is fixing, but out of scope — see Out of Scope). Given no shared convention exists, use the plain BCL `System.ArgumentException` as the brief suggests, since these are simple "argument is null/whitespace" guards on constructor/method parameters — this is both framework-independent and semantically accurate (`ArgumentException` is the idiomatic BCL type for invalid-argument guards).

**Acceptance criteria:**
- No reference to `System.ComponentModel.DataAnnotations.ValidationException` remains in `RecurringJobConfiguration`.
- All 14 guard-clause throw sites use `ArgumentException` with the same message text as today (message content is not part of this change's scope; only the exception type changes).
- Existing unit/integration tests covering `RecurringJobConfiguration` construction and mutation (search `backend/test` for tests referencing this entity) are updated to assert on `ArgumentException` instead of `ValidationException`, and continue to pass.
- Downstream behavior is unaffected: all current call sites (`UpdateRecurringJobStatusHandler`, `UpdateRecurringJobCronHandler`, `RecurringJobSeeder`) catch these guard-clause failures via a generic `catch (Exception ex)` and surface `ex.Message` in an error response — confirmed no code catches `System.ComponentModel.DataAnnotations.ValidationException` specifically for this entity. (Note: `Anela.Heblo.API/Infrastructure/ExceptionHandling/ValidationExceptionHandler.cs` maps `FluentValidation.ValidationException` — a different type entirely — to a 400 response; it does not and will not interact with this entity's exceptions either before or after this change.)

### FR-3: Remove the `System.ComponentModel.DataAnnotations` using directive
Once FR-1 and FR-2 are complete, delete `using System.ComponentModel.DataAnnotations;` from the top of `RecurringJobConfiguration.cs` — it will be unused.

**Acceptance criteria:**
- The file contains no `using System.ComponentModel.DataAnnotations;` line.
- `dotnet build` succeeds with no unused-using warnings for this file (or `dotnet format` doesn't flag it).

## Non-Functional Requirements

### NFR-1: Performance
N/A — no behavioral or runtime-performance impact; this is a compile-time/type-level change with no new allocations, I/O, or algorithmic changes.

### NFR-2: Security
N/A — no change to authentication, authorization, or data exposure. Exception messages already reach clients only via generic handler code (`ex.Message` in application-layer catch blocks), and message text is unchanged by this refactor.

## Data Model
No changes. `RecurringJobConfiguration`'s properties, types, and the underlying `RecurringJobConfigurations` table/columns (owned by `RecurringJobConfigurationConfiguration`'s Fluent API) are unchanged. This is a pure code-cleanliness change to the entity class; no new migration is expected (see FR-1 acceptance criteria).

## API / Interface Design
N/A — no public API surface, controller, or DTO changes. The entity's public constructor and method signatures (`RecurringJobConfiguration(...)`, `UpdateConfiguration`, `Enable`, `Disable`, `UpdateCronExpression`) keep identical parameter lists and return types; only the internal exception type thrown on invalid input changes.

## Dependencies
- `backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationConfiguration.cs` — must remain the single source of truth for length/required constraints (no change required, just must not be touched/weakened).
- Any test project referencing `RecurringJobConfiguration`'s validation exceptions (locate via `backend/test`, search for `ValidationException` in the context of `RecurringJobConfiguration`) — must be updated in lockstep to assert `ArgumentException`.
- No third-party library or infrastructure dependency changes.

## Out of Scope
- Fixing the same `ValidationException`-from-`DataAnnotations` anti-pattern in `Anela.Heblo.Domain/Features/Logistics/Transport/TransportBoxExceptions.cs`, where custom exceptions extend `System.ComponentModel.DataAnnotations.ValidationException`. This is a separate, pre-existing instance of the same class of issue in a different module and was not part of this finding.
- Introducing a shared `DomainException` base type for the whole Domain layer. None exists today; creating one is a broader architectural decision out of scope for this mechanical fix.
- Changing exception message text/wording.
- Any change to `RecurringJobConfigurationConfiguration.cs` (the Fluent API config) — it is already correct and is the thing being deferred to.
- Any change to how the API layer maps exceptions to HTTP responses (`ValidationExceptionHandler.cs` and friends) — out of scope, and confirmed not to interact with this entity's exceptions today or after this change.

## Open Questions
None.

## Status: COMPLETE
