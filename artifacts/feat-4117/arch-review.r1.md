# Architecture Review: Remove framework coupling from `RecurringJobConfiguration`

## Skip Design: true

## Architectural Fit Assessment
This is a textbook Clean Architecture cleanup, not a new capability. It removes an existing violation (Domain → `System.ComponentModel.DataAnnotations`, a framework namespace) without touching behavior, persistence, or any public contract. I verified both halves of the finding directly against the source:

- `RecurringJobConfiguration.cs` decorates six properties with `[Required]`/`[MaxLength(n)]`. `RecurringJobConfigurationConfiguration.cs` (Fluent API) declares `HasMaxLength(...)` + `IsRequired()` for the same six properties with matching lengths (100/200/500/50/100/100). EF Core's Fluent API wins over attribute-based configuration when both are present, so the attributes are inert. Removing them is safe and changes no EF model — confirmed there is no other consumer of these attributes (no `[Required]`/`[MaxLength]` reflection anywhere else in the codebase touching this type; ASP.NET model binding never sees this entity, only its DTOs).
- All 14 `throw new ValidationException(...)` sites in the entity are plain null/whitespace argument guards, not ASP.NET model-validation. `ArgumentException` is the correct BCL type for this.
- I checked for a reusable domain exception base and confirmed the spec's claim: none exists (`InvalidPhotoSearchPatternException`, `GridLayoutPersistenceException` extend `System.Exception` directly; `TransportBoxExceptions.cs` extends the same `ValidationException` anti-pattern this task fixes, out of scope per the spec). No architectural decision is needed here — `ArgumentException` is the right call and matches `development_guidelines.md`'s spirit of keeping Domain framework-free, even though that doc doesn't name this specific rule explicitly.
- All three downstream callers (`UpdateRecurringJobStatusHandler.cs:77`, `UpdateRecurringJobCronHandler.cs:88`, and the seeder) catch via generic `catch (Exception ex)` and surface `ex.Message` — none pattern-matches on `ValidationException` specifically, so the type swap has zero behavioral impact on any handler.

The only wrinkle beyond the spec's own text: **`backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobConfigurationTests.cs` has 9 call sites** using `Assert.Throws<ValidationException>` (lines 81, 97, 192, 220, 246, 265, plus test-method names at 78, 94, 189, 205, 231, 250 that say "ShouldThrowValidationException") and a top-of-file `using System.ComponentModel.DataAnnotations;` (line 1) that becomes unused once the assertions change type. FR-2's acceptance criteria already anticipates test updates in general terms; this review confirms the exact file and makes explicit that the `using` in the *test* file must also be dropped, and that the test-method names, while not required to change for correctness, should be renamed for consistency (`...ShouldThrowArgumentException...`) so they don't lie about what they assert.

## Proposed Architecture
None needed — no new component, no new interface, no new data flow. This is a same-file, same-signature edit confined to one Domain entity plus its one direct test file.

### Key Design Decisions

#### Decision 1: Exception type for domain guard clauses
**Options considered:**
- Keep `ValidationException` (rejected — the finding itself, framework coupling).
- Introduce a new `DomainException` base type in `Anela.Heblo.Domain.Shared` for this and future use (rejected for *this* task — no existing convention to anchor it to, `TransportBoxExceptions.cs` shows the codebase already has an inconsistent mix of exception bases, and inventing a new shared abstraction is a broader decision that shouldn't ride on a mechanical cleanup PR).
- Use plain BCL `ArgumentException` (chosen).

**Chosen approach:** `throw new ArgumentException("<same message text>")` at all 14 sites, unchanged message strings.

**Rationale:** These are constructor/method argument guards (`string.IsNullOrWhiteSpace` checks), which is exactly `ArgumentException`'s intended use, it has zero framework dependency, and it requires no new abstraction or DI wiring. Do **not** use `ArgumentNullException`/`ArgumentException(message, paramName)` overloads or otherwise "improve" the guards — the spec and brief are explicit that message text and exception construction stay as close to identical as the type change allows; scope creep here (e.g., adding `nameof(jobName)` as a `paramName` argument) is out of scope and should be avoided per the "surgical changes" rule.

## Implementation Guidance

### Directory / Module Structure
No new files, no new directories. Edits confined to:
- `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs` — remove `[Required]`/`[MaxLength]` attributes (6 properties, 12 attribute lines), replace all 14 `ValidationException` throws with `ArgumentException`, remove the `using System.ComponentModel.DataAnnotations;` line.
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobConfigurationTests.cs` — update the 9 `Assert.Throws<ValidationException>` call sites to `Assert.Throws<ArgumentException>`, remove the now-unused `using System.ComponentModel.DataAnnotations;` (line 1), and rename the 6 affected test methods (`Constructor_ShouldThrowValidationException_*`, `UpdateConfiguration_ShouldThrowValidationException_*`, `Enable_ShouldThrowValidationException_*`, `Disable_ShouldThrowValidationException_*`) to say `ShouldThrowArgumentException`.

Do **not** touch `RecurringJobConfigurationConfiguration.cs` (Persistence) — it is the confirmed source of truth and is explicitly out of scope.

### Interfaces and Contracts
No public interface, DTO, or API contract changes. The entity's constructor and public method signatures (`RecurringJobConfiguration(...)`, `UpdateConfiguration`, `Enable`, `Disable`, `UpdateCronExpression`) are unchanged — only the internal exception type on the guard clauses changes.

### Data Flow
Unaffected. No new or changed data path; this is a compile-time type substitution plus removal of dead attribute markup.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Test file left asserting the old exception type, breaking the build | Low | Update all 9 `Assert.Throws<ValidationException>` sites in `RecurringJobConfigurationTests.cs` in the same commit; `dotnet build`/`dotnet test` will fail loudly if any is missed since `ValidationException` won't be thrown anymore. |
| Some other caller pattern-matches on `ValidationException` for this entity, silently changing error handling | Very low | Verified: all three call sites (`UpdateRecurringJobStatusHandler`, `UpdateRecurringJobCronHandler`, seeder) use generic `catch (Exception ex)`. Confirmed no `catch (ValidationException ...)` anywhere in `Anela.Heblo.Application/Features/BackgroundJobs/`. |
| Removing `[MaxLength]`/`[Required]` attributes silently changes the EF model, triggering an unwanted migration | Low | Fluent API config already declares identical constraints and takes precedence over attributes; verify via `dotnet ef migrations has-pending-model-changes` (or equivalent dry-run) before declaring done, per spec FR-1. |
| Unused-using warning/CI lint failure if the `using` isn't removed from *both* files | Low | Remove `using System.ComponentModel.DataAnnotations;` from both `RecurringJobConfiguration.cs` and `RecurringJobConfigurationTests.cs`; run `dotnet format` to confirm no residual warning. |

## Specification Amendments
- **FR-2's acceptance criteria** ("Existing unit/integration tests ... are updated") should be made concrete: the file is `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobConfigurationTests.cs`, with exactly 9 `Assert.Throws<ValidationException>` sites (lines 81, 97, 192, 220, 246, 265) to change to `Assert.Throws<ArgumentException>`, and a `using System.ComponentModel.DataAnnotations;` (line 1) to remove.
- **Add to FR-2 scope**: rename the 6 test methods whose names say `ShouldThrowValidationException` to `ShouldThrowArgumentException` (`Constructor_ShouldThrowValidationException_WhenJobNameIsEmpty`, `_WhenDisplayNameIsEmpty`, `_WhenTimeZoneIdIsEmpty`; `UpdateConfiguration_ShouldThrowValidationException_WhenTimeZoneIdIsEmpty`; `Enable_ShouldThrowValidationException_WhenModifiedByIsEmpty`; `Disable_ShouldThrowValidationException_WhenModifiedByIsEmpty`). This is a naming-consistency nit, not a functional requirement — low priority but should not be skipped, since a test named "ShouldThrowValidationException" that asserts `ArgumentException` is confusing to future readers.
- No other amendments. The spec's FR-1/FR-3 scope and the Out-of-Scope section are accurate as written and require no correction.

## Prerequisites
None. No migration, no config, no infrastructure change is required before implementation starts. The only pre-implementation check worth running (already called out in FR-1) is confirming `dotnet ef migrations has-pending-model-changes` (or the project's equivalent) reports no pending changes after the attribute removal, to prove the Fluent API config was indeed already authoritative.
