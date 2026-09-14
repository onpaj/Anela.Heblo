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

