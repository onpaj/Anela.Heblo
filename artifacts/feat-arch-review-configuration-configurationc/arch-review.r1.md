```markdown
# Architecture Review: Remove Unused ASPNETCORE_ENVIRONMENT Constant

## Skip Design: true

This is a pure code-deletion change in the backend Domain project. No UI, no new components, no visual surface — strictly a dead-code removal.

## Architectural Fit Assessment

The change aligns cleanly with existing conventions and **strengthens** the codebase by removing a misleading symbol:

- **`ConfigurationConstants` is the correct home** for cross-cutting configuration keys consumed via `IConfiguration.GetValue<T>(...)`. Verified usages of `USE_MOCK_AUTH` and `BYPASS_JWT_VALIDATION` confirm the pattern (4+ call sites each across `Authentication`, `Hangfire`, `UserManagement`, `Configuration` modules).
- **`APP_VERSION` is the correct precedent** for an environment-variable key resolved via `Environment.GetEnvironmentVariable(ConfigurationConstants.APP_VERSION)` — used in `GetConfigurationHandler.cs:76`.
- **`ASPNETCORE_ENVIRONMENT` breaks this pattern**: the constant exists but no caller dereferences it. A grep across the entire solution for `ConfigurationConstants.ASPNETCORE_ENVIRONMENT` returns zero matches. The five raw-string call sites listed in the brief bypass the constant entirely.
- The constant therefore offers no centralization value, no type/refactor safety (since callers use the literal), and pollutes the file's "constants we actually rely on" signal.

Integration points: **one file edited, one line removed.** No module boundaries crossed, no DI registration changes, no contract changes.

## Proposed Architecture

### Component Overview

```
backend/src/Anela.Heblo.Domain/Features/Configuration/
└── ConfigurationConstants.cs   ← single edit: delete line 10
                                  (current line numbering in file:
                                   8  // Environment variable keys
                                   9  public const string APP_VERSION = ...;
                                  10  public const string ASPNETCORE_ENVIRONMENT = ...;  ← REMOVE
                                  11
                                  12  // Configuration keys
                                  ...)
```

No other files are touched. The five raw-string call sites enumerated in spec FR-4 are explicitly preserved.

### Key Design Decisions

#### Decision 1: Delete, do not deprecate
**Options considered:**
- (A) Delete the constant outright.
- (B) Mark `[Obsolete("Use IHostEnvironment.EnvironmentName")]` and remove in a later release.
- (C) Keep the constant and migrate raw-string callers to use it.

**Chosen approach:** (A) — straight delete.

**Rationale:** `ConfigurationConstants` lives in the internal `Anela.Heblo.Domain` assembly with no external consumers (solo-developer monorepo, single Docker image). `[Obsolete]` adds noise without benefit when the symbol has zero callers — there is nothing to warn. Option (C) is a separate concern: the preferred fix at the raw-string sites is `IHostEnvironment.EnvironmentName`, not `Environment.GetEnvironmentVariable(constant)`, so promoting the constant would entrench the wrong pattern.

#### Decision 2: Do not touch raw-string callers in this PR
**Options considered:**
- (A) Bundle the raw-string-to-`IHostEnvironment` migration into the same PR.
- (B) Scope this PR to deletion only; track the migration separately.

**Chosen approach:** (B) — aligns with spec FR-4.

**Rationale:** Migrating the five call sites (`DiagnosticsController`, `E2ETestController`, `CostOptimizedTelemetryProcessor`, `DesignTimeDbContextFactory`, `GetConfigurationHandler`) is non-trivial. `DesignTimeDbContextFactory` is a static EF design-time entry point with no DI scope and must continue to read the environment variable directly. `CostOptimizedTelemetryProcessor` is constructed by Application Insights infrastructure where `IHostEnvironment` availability needs verification. These per-site assessments belong in a focused follow-up, not this dead-code removal. Keeping the PR surgical preserves reviewability and makes the changeset trivially safe to revert.

#### Decision 3: Do not audit sibling constants
**Options considered:**
- (A) Sweep `ConfigurationConstants.cs` for other dead symbols while here.
- (B) Address only the named finding.

**Chosen approach:** (B) — aligns with spec Out of Scope.

**Rationale:** A focused PR matches the daily arch-review routine's single-finding cadence. Bundling a broader audit dilutes the changeset and risks reviewer fatigue. (For reference, `DEFAULT_VERSION` and `DEFAULT_ENVIRONMENT` did not appear in qualified-access grep results and are candidates for a separate audit — but explicitly out of scope here.)

## Implementation Guidance

### Directory / Module Structure

No new files. No moves. Single edit:

- **Edit:** `backend/src/Anela.Heblo.Domain/Features/Configuration/ConfigurationConstants.cs` — remove the `ASPNETCORE_ENVIRONMENT` constant declaration.

After removal, the `// Environment variable keys` comment block should still make sense with `APP_VERSION` remaining underneath it. Leave the comment intact.

### Interfaces and Contracts

No interface, no contract, no DTO, no API surface change. `ConfigurationConstants` is a `public static class` of `const string` values in an internal-only assembly. Deletion of a `const` field with zero callers cannot break source or binary compatibility within the solution.

### Data Flow

Unchanged. The two existing patterns for reading the environment name continue to work as-is:

1. **Preferred (DI-aware code):** `IHostEnvironment.EnvironmentName` — used in `GetConfigurationHandler.cs:63`.
2. **Direct env-var read (static/infra contexts):** `Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")` — used in the five sites listed in spec FR-4.

Neither path touches the deleted symbol.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Hidden reflection-based or string-name reference to `ASPNETCORE_ENVIRONMENT` (e.g., `nameof(ConfigurationConstants.ASPNETCORE_ENVIRONMENT)`) | Very Low | Run both literal and regex greps as required by spec FR-2; also grep for `nameof(ConfigurationConstants` to be thorough. Compile + tests catch any direct reference. |
| Constant referenced from generated code (OpenAPI client, EF migrations) | Negligible | The OpenAPI generator does not emit references to internal Domain constants. EF migrations are SQL/C# files that don't import `ConfigurationConstants`. Build will catch any miss. |
| Developer assumes the constant should exist and reintroduces it later | Low | Spec NFR-3 documents the rationale. Optional follow-up: PR description should link to the brief so future contributors can locate context via git blame. |
| Reviewer conflates this PR with the raw-string callsite cleanup | Low | PR description must explicitly call out FR-4 (raw-string sites preserved by design) and link a follow-up issue. |

## Specification Amendments

None required. The spec is correctly scoped, has accurate acceptance criteria, and properly defers the raw-string-callsite refactor to a follow-up. One minor enrichment for the implementer (not a spec defect):

- **Verification command (suggested for PR description, satisfies FR-2):**
  ```
  rg -n 'ConfigurationConstants\.ASPNETCORE_ENVIRONMENT' --type cs
  rg -n 'nameof\(ConfigurationConstants\.ASPNETCORE_ENVIRONMENT\)' --type cs
  ```
  Both should return zero matches before deletion. Paste the empty output into the PR.

## Prerequisites

None. No migrations, no configuration changes, no infrastructure work, no feature flag, no Key Vault entries. The change is a pure source-code deletion that can land independently and reverts cleanly with `git revert`.
```