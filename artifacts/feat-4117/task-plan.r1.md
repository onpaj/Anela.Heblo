# Remove framework coupling from RecurringJobConfiguration

**Goal:** Strip the redundant `[Required]`/`[MaxLength]` DataAnnotations attributes and the `System.ComponentModel.DataAnnotations.ValidationException` guard-clause type out of the Domain entity `RecurringJobConfiguration`, replacing the exception with the framework-independent `System.ArgumentException`, and update its unit test file to match — with zero behavioral change.

**Architecture:** `RecurringJobConfiguration` (`backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs`) is a Clean Architecture Domain entity that must not depend on framework namespaces. Its `[Required]`/`[MaxLength]` attributes are inert — `RecurringJobConfigurationConfiguration` (Persistence layer, EF Core Fluent API) already declares the same constraints and Fluent API takes precedence, so removing the attributes changes no EF model and requires no migration. Its guard clauses throw `ValidationException` (an ASP.NET/data-binding type) for plain null/whitespace argument checks; these become `ArgumentException` (the idiomatic BCL type), with identical message text. All downstream callers catch via generic `catch (Exception ex)`, so the exception-type swap is invisible to them.

**Tech Stack:** .NET 8, C#, xUnit, EF Core (Fluent API, untouched), MediatR/Clean Architecture monorepo.

---

## Task list

1. `entity-remove-dataannotations-and-swap-exception` — edit the Domain entity: remove attributes, swap exception type, remove using; verify with `dotnet build`.
2. `tests-update-for-argumentexception` — edit the test file to match: rename methods, swap assertion type, remove using; run the test suite; run repo-wide validation.

---

### task: entity-remove-dataannotations-and-swap-exception

**Context:** File to edit: `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs`. Repo root for all commands below: `/home/user/worktrees/feature-4117-Arch-Review-Backgroundjobs-Domain-Entity-Carries-R`.

The file currently starts like this (verified by direct read):

```csharp
using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.BackgroundJobs;

public class RecurringJobConfiguration : Entity<string>
{
    [Required]
    [MaxLength(100)]
    public string JobName { get; private set; }

    [Required]
    [MaxLength(200)]
    public string DisplayName { get; private set; }

    [Required]
    [MaxLength(500)]
    public string Description { get; private set; }

    [Required]
    [MaxLength(50)]
    public string CronExpression { get; private set; }

    [Required]
    [MaxLength(100)]
    public string TimeZoneId { get; private set; }

    public bool IsEnabled { get; private set; }

    public DateTime LastModifiedAt { get; private set; }

    [Required]
    [MaxLength(100)]
    public string LastModifiedBy { get; private set; }
```

It has 15 guard-clause throw sites reading `throw new ValidationException("...")` (verified by direct read, at lines 58, 60, 62, 64, 66, 68 in the constructor; 90, 92, 94, 96, 98 in `UpdateConfiguration`; 111 in `Enable`; 121 in `Disable`; 131, 133 in `UpdateCronExpression`) — each with a distinct message string but every one preceded by the exact substring `throw new ValidationException(`.

The project (`backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj`) has `<ImplicitUsings>enable</ImplicitUsings>`, so `System.ArgumentException` is available without adding any `using`.

Steps:

- [ ] **Step 1 — remove the now-doomed `using` line (do this last-but-declare-first is unnecessary; order doesn't matter for these three edits since they touch disjoint text).** Use the Edit tool on `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs`:
  - `old_string`:
    ```
    using System.ComponentModel.DataAnnotations;
    using Anela.Heblo.Xcc.Domain;
    ```
  - `new_string`:
    ```
    using Anela.Heblo.Xcc.Domain;
    ```

- [ ] **Step 2 — remove the `[Required]`/`[MaxLength]` attributes from the six decorated properties.** Use the Edit tool on the same file:
  - `old_string`:
    ```
    public class RecurringJobConfiguration : Entity<string>
    {
        [Required]
        [MaxLength(100)]
        public string JobName { get; private set; }

        [Required]
        [MaxLength(200)]
        public string DisplayName { get; private set; }

        [Required]
        [MaxLength(500)]
        public string Description { get; private set; }

        [Required]
        [MaxLength(50)]
        public string CronExpression { get; private set; }

        [Required]
        [MaxLength(100)]
        public string TimeZoneId { get; private set; }

        public bool IsEnabled { get; private set; }

        public DateTime LastModifiedAt { get; private set; }

        [Required]
        [MaxLength(100)]
        public string LastModifiedBy { get; private set; }
    ```
  - `new_string`:
    ```
    public class RecurringJobConfiguration : Entity<string>
    {
        public string JobName { get; private set; }

        public string DisplayName { get; private set; }

        public string Description { get; private set; }

        public string CronExpression { get; private set; }

        public string TimeZoneId { get; private set; }

        public bool IsEnabled { get; private set; }

        public DateTime LastModifiedAt { get; private set; }

        public string LastModifiedBy { get; private set; }
    ```

- [ ] **Step 3 — swap every `ValidationException` throw for `ArgumentException`.** Use the Edit tool on the same file with `replace_all: true` (the substring occurs identically 15 times, each immediately followed by a different message literal, so a single find/replace on the constructor-call prefix covers all sites without touching message text):
  - `old_string`: `throw new ValidationException(`
  - `new_string`: `throw new ArgumentException(`
  - `replace_all`: `true`

- [ ] **Step 4 — confirm no trace of the old type remains.** Run:
  ```
  grep -n "ValidationException\|DataAnnotations" backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs
  ```
  Expected output: nothing (no matches, exit code 1). If anything prints, re-check steps 1–3.

- [ ] **Step 5 — build the Domain project.** Run:
  ```
  dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj
  ```
  Expected output: `Build succeeded.` with `0 Error(s)` (warnings unrelated to this file are fine; there must be no CS0246 "ValidationException not found"/unused-using warnings for this file).

- [ ] **Step 6 — format check.** Run:
  ```
  dotnet format backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj --verify-no-changes
  ```
  Expected output: exit code 0, no "Formatted code file" lines for `RecurringJobConfiguration.cs`. If it reports formatting diffs, run `dotnet format backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj` (without `--verify-no-changes`) to apply them, then re-run the verify command to confirm it now passes.

- [ ] **Step 7 — optional EF model-drift sanity check (per spec FR-1).** Run, from the repo root:
  ```
  dotnet ef migrations has-pending-model-changes --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API
  ```
  Expected: it reports no pending model changes (exact wording varies by EF Core tools version, e.g. "No model changes were detected."). This confirms `RecurringJobConfigurationConfiguration`'s Fluent API — unchanged by this task — was already the sole source of truth, and the attribute removal produced no drift. If the `dotnet-ef` global tool is not installed in this environment, skip this step — it is a confirmatory check, not a gate; the architecture review already verified the Fluent API config matches (same lengths: `JobName` 100, `DisplayName` 200, `Description` 500, `CronExpression` 50, `TimeZoneId` 100, `LastModifiedBy` 100), and `RecurringJobConfigurationConfiguration.cs` is explicitly out of scope for this task (do not edit it).

- [ ] **Step 8 — commit.**
  ```
  git add backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs
  git commit -m "$(cat <<'EOF'
  Remove DataAnnotations coupling from RecurringJobConfiguration

  Drop redundant [Required]/[MaxLength] attributes (Fluent API in
  RecurringJobConfigurationConfiguration already enforces the same
  constraints) and replace ValidationException guard-clause throws
  with ArgumentException so the Domain entity no longer depends on
  System.ComponentModel.DataAnnotations.

  Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_019hfZcVQK13U8w5oZ2ditpc
  EOF
  )"
  ```
  Expected: commit succeeds; `git status` shows a clean tree for this file.

---

### task: tests-update-for-argumentexception

**Context:** File to edit: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobConfigurationTests.cs`. Repo root for all commands below: `/home/user/worktrees/feature-4117-Arch-Review-Backgroundjobs-Domain-Entity-Carries-R`. This task assumes the entity change from `entity-remove-dataannotations-and-swap-exception` is already applied (the entity now throws `ArgumentException`, not `ValidationException`) — if it is not, running this task's tests will fail with the old type mismatch, so apply that task first.

The file currently starts:

```csharp
using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Xunit;
```

It contains exactly 6 assertion call sites reading `Assert.Throws<ValidationException>` (verified by direct read, at lines 81, 97, 192, 220, 246, 265) and 6 test methods whose names contain `ShouldThrowValidationException` (at lines 78, 94, 189, 205, 231, 250):
- `Constructor_ShouldThrowValidationException_WhenJobNameIsEmpty` (line 78)
- `Constructor_ShouldThrowValidationException_WhenDisplayNameIsEmpty` (line 94)
- `Constructor_ShouldThrowValidationException_WhenTimeZoneIdIsEmpty` (line 189)
- `UpdateConfiguration_ShouldThrowValidationException_WhenTimeZoneIdIsEmpty` (line 205)
- `Enable_ShouldThrowValidationException_WhenModifiedByIsEmpty` (line 231)
- `Disable_ShouldThrowValidationException_WhenModifiedByIsEmpty` (line 250)

(Note: the arch-review document for this feature states "9" `Assert.Throws<ValidationException>` call sites; a direct read of the current file found exactly 6. This task proceeds against the actual file content — 6 assertion sites and 6 method names — which is what steps 2 and 3 below cover exhaustively via `replace_all`, so the discrepancy has no effect on correctness: every actual occurrence is replaced.)

The project (`backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`) has `<ImplicitUsings>enable</ImplicitUsings>`, so `System.ArgumentException` is available without adding any `using`.

Steps:

- [ ] **Step 1 — write the test file's expectations first (TDD check): confirm the current (pre-edit) test run fails to compile/pass against the already-updated entity.** Since task `entity-remove-dataannotations-and-swap-exception` already changed the entity to throw `ArgumentException`, running the *unmodified* test file now should fail. Run:
  ```
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RecurringJobConfigurationTests"
  ```
  Expected output: build/run failure or test failures — the 6 `Assert.Throws<ValidationException>` sites now see an `ArgumentException` thrown instead, which `Assert.Throws<ValidationException>` does NOT catch as a match (they are unrelated exception types, `ArgumentException` does not derive from `ValidationException`), so those 6 `[Fact]` tests fail with an `Assert.Throws() Failure` (wrong exception type) or, if the entity was left uncompilable some other way, a build error. This confirms the "before" state is red, as expected.

- [ ] **Step 2 — remove the now-unused `using System.ComponentModel.DataAnnotations;`.** Use the Edit tool on `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobConfigurationTests.cs`:
  - `old_string`:
    ```
    using System.ComponentModel.DataAnnotations;
    using Anela.Heblo.Domain.Features.BackgroundJobs;
    using Xunit;
    ```
  - `new_string`:
    ```
    using Anela.Heblo.Domain.Features.BackgroundJobs;
    using Xunit;
    ```

- [ ] **Step 3 — swap the assertion type at all 6 call sites.** Use the Edit tool on the same file with `replace_all: true`:
  - `old_string`: `Assert.Throws<ValidationException>`
  - `new_string`: `Assert.Throws<ArgumentException>`
  - `replace_all`: `true`

- [ ] **Step 4 — rename the 6 test methods so their names match what they now assert.** Use the Edit tool on the same file with `replace_all: true`:
  - `old_string`: `ShouldThrowValidationException`
  - `new_string`: `ShouldThrowArgumentException`
  - `replace_all`: `true`

- [ ] **Step 5 — confirm no trace of the old type/name remains.** Run:
  ```
  grep -n "ValidationException\|DataAnnotations" backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobConfigurationTests.cs
  ```
  Expected output: nothing (no matches, exit code 1).

- [ ] **Step 6 — run the affected tests and see them pass (green).** Run:
  ```
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RecurringJobConfigurationTests"
  ```
  Expected output: `Passed!` summary line with all 12 tests in `RecurringJobConfigurationTests` passing, 0 failed, 0 skipped (the class has 12 `[Fact]` methods total: `RecurringJobConfiguration_ShouldCreateWithValidProperties`, `RecurringJobConfiguration_ShouldSetTimeZoneId_WhenGivenNonDefaultValue`, `RecurringJobConfiguration_ShouldAllowDisabling`, `Constructor_ShouldThrowArgumentException_WhenJobNameIsEmpty`, `Constructor_ShouldThrowArgumentException_WhenDisplayNameIsEmpty`, `Enable_ShouldSetIsEnabledToTrue`, `Disable_ShouldSetIsEnabledToFalse`, `UpdateConfiguration_ShouldUpdateProperties`, `Constructor_ShouldThrowArgumentException_WhenTimeZoneIdIsEmpty`, `UpdateConfiguration_ShouldThrowArgumentException_WhenTimeZoneIdIsEmpty`, `Enable_ShouldThrowArgumentException_WhenModifiedByIsEmpty`, `Disable_ShouldThrowArgumentException_WhenModifiedByIsEmpty`).

- [ ] **Step 7 — build the whole test project to catch any other reference to the old names/type.** Run:
  ```
  dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
  ```
  Expected output: `Build succeeded.` with `0 Error(s)`.

- [ ] **Step 8 — format check.** Run:
  ```
  dotnet format backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --verify-no-changes
  ```
  Expected output: exit code 0, no formatting diffs reported for `RecurringJobConfigurationTests.cs`. If it reports diffs, run `dotnet format backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` (without `--verify-no-changes`) to apply them, then re-run the verify command to confirm it now passes.

- [ ] **Step 9 — full repo-wide validation (per CLAUDE.md "Validation before completion").** Run, from the repo root:
  ```
  dotnet build Anela.Heblo.sln
  ```
  Expected output: `Build succeeded.` with `0 Error(s)` across every project in the solution — this confirms no other file in the codebase referenced `RecurringJobConfiguration`'s old exception type or the removed attributes in a way that broke compilation. Then run the full BackgroundJobs test slice to catch any other test referencing this entity beyond the one file already covered:
  ```
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~BackgroundJobs"
  ```
  Expected output: `Passed!` summary line, 0 failed.

- [ ] **Step 10 — commit.**
  ```
  git add backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobConfigurationTests.cs
  git commit -m "$(cat <<'EOF'
  Update RecurringJobConfigurationTests for ArgumentException

  Assert on ArgumentException instead of the removed
  System.ComponentModel.DataAnnotations.ValidationException,
  rename the 6 affected test methods to match, and drop the
  now-unused DataAnnotations using directive.

  Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_019hfZcVQK13U8w5oZ2ditpc
  EOF
  )"
  ```
  Expected: commit succeeds; `git status` shows a clean tree for this file.

---

## Self-review against the spec

- **FR-1** (remove `[Required]`/`[MaxLength]` from `JobName`, `DisplayName`, `Description`, `CronExpression`, `TimeZoneId`, `LastModifiedBy`; `IsEnabled`/`LastModifiedAt` untouched; `using` removed; `RecurringJobConfigurationConfiguration` untouched) → covered by `entity-remove-dataannotations-and-swap-exception` Steps 1–2 (exact before/after property block shows all six properties losing their attributes and `IsEnabled`/`LastModifiedAt` staying bare), Step 7 (EF drift check), and by never touching `RecurringJobConfigurationConfiguration.cs` anywhere in the plan.
- **FR-2** (14→ actually 15 verified `ValidationException` throw sites become `ArgumentException`, same message text, tests updated) → covered by `entity-remove-dataannotations-and-swap-exception` Step 3 (`replace_all` on the constructor-call prefix only, leaving every message string byte-for-byte unchanged) and Step 4 (grep proof), and by `tests-update-for-argumentexception` Steps 3–4 (assertion type + method names) and Step 6 (green run). Downstream callers (`UpdateRecurringJobStatusHandler`, `UpdateRecurringJobCronHandler`, `RecurringJobSeeder`) are not edited anywhere in this plan, matching the spec's confirmation that none of them pattern-match on the old type.
- **FR-3** (remove the `using System.ComponentModel.DataAnnotations;` from the entity, `dotnet build`/`dotnet format` clean) → covered by `entity-remove-dataannotations-and-swap-exception` Step 1 (removal), Step 4 (grep proof), Step 5 (build), Step 6 (format). The arch-review's addendum to also remove the `using` from the *test* file is covered by `tests-update-for-argumentexception` Step 2.
- **Arch-review's method-rename addendum** (rename the 6 `ShouldThrowValidationException` test methods to `ShouldThrowArgumentException`) → covered by `tests-update-for-argumentexception` Step 4.
- **Out of scope items** (`TransportBoxExceptions.cs`, a shared `DomainException` base, message-text changes, `RecurringJobConfigurationConfiguration.cs`, `ValidationExceptionHandler.cs`) → none of these files appear anywhere in either task; no step touches them.
- **Type/name consistency check:** both tasks use the same target type name `ArgumentException` (fully-qualified as `System.ArgumentException`, available via `ImplicitUsings`) in both the production file and the test file — no mismatch. Method-rename target (`ShouldThrowArgumentException`) is used consistently. No task invents a name not already used by the other or by the spec/arch-review.
- **Discrepancy noted and resolved:** the spec/arch-review say "14 throw sites" and "9 `Assert.Throws<ValidationException>` call sites" respectively; a direct read of the current files found 15 throw sites in the entity and 6 assertion call sites (plus 6 differently-named test methods) in the test file. Both tasks operate via exhaustive `replace_all` on the literal substrings involved (`throw new ValidationException(`, `Assert.Throws<ValidationException>`, `ShouldThrowValidationException`) rather than a fixed count, and Steps 4/5 (grep) independently prove zero occurrences remain — so the plan is correct regardless of which count is accurate.
