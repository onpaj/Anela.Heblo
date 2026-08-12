# Architecture Review: Gate `E2ETestController.GetEnvironmentInfo` behind the same environment check as its siblings

## Skip Design: true
Backend-only fix: one controller action gains a guard clause identical in shape to three guards already present in the same file. No new or changed UI, no new API contract for the in-environment case, no new component. `docs/design/*` is out of scope.

## Architectural Fit Assessment
This aligns cleanly with existing conventions — it does not introduce a new pattern, it applies an existing one consistently. `E2ETestController` (`backend/src/Anela.Heblo.API/Controllers/E2ETestController.cs`) already has the exact guard clause duplicated verbatim in `CreateE2ESession` (line 68), `GetAuthStatus` (line 134), and `GetE2EApp` (line 172):

```csharp
if (!_environment.IsEnvironment("Staging") && !_environment.IsDevelopment())
{
    return NotFound(new { error = "E2E endpoints only available in Staging or Development environment", currentEnvironment = _environment.EnvironmentName });
}
```

`GetEnvironmentInfo` (line 41) is the sole outlier. I confirmed via `AuthenticationExtensions.ConfigureAuthorizationPolicies` (`backend/src/Anela.Heblo.API/Extensions/AuthenticationExtensions.cs:104-121`) that `options.DefaultPolicy` requires `AccessRoles.Base` — but ASP.NET Core only enforces `DefaultPolicy` on actions carrying a bare `[Authorize]` attribute with no named policy. No `FallbackPolicy` is registered in this extension or in `Program.cs`, so an action with **no** `[Authorize]` attribute at all — like `GetEnvironmentInfo` — receives no authorization check whatsoever. This matches the brief's diagnosis exactly; there is no other net that would have caught this. `grep` across `backend/` confirms the guard-clause text is not duplicated or centralized anywhere else in the codebase — it exists only as three copy-pasted blocks in this one file.

## Proposed Architecture

### Component Overview
No new components. Single-file, single-method change:

```
E2ETestController (unchanged shape)
 ├─ GetEnvironmentInfo()   ← add guard clause here (this fix)
 ├─ CreateE2ESession()     ← existing guard, unchanged
 ├─ GetAuthStatus()        ← existing guard, unchanged
 └─ GetE2EApp()            ← existing guard, unchanged
```

### Key Design Decisions

#### Decision 1: Environment guard vs. `[Authorize]` attribute
**Options considered:**
1. Add the same inline environment guard clause used by the other three actions.
2. Add `[Authorize(AuthenticationSchemes = "E2ETestCookies")]` (as `GetAuthStatus`/`GetE2EApp` also carry), gating on authentication instead of/in addition to environment.
3. Register a global `FallbackPolicy` requiring authentication on every action lacking an explicit `[Authorize]`/`[AllowAnonymous]`.

**Chosen approach:** Option 1 — inline environment guard, matching `CreateE2ESession`'s pattern (environment guard only, no `[Authorize]`).

**Rationale:** `GetEnvironmentInfo` is a diagnostic/debugging endpoint intended to be callable without an authenticated E2E session (that's its whole purpose — to check what environment is running before you've set anything up), exactly like `CreateE2ESession`. Adding `[Authorize(AuthenticationSchemes = "E2ETestCookies")]` would break that use case by requiring a session that doesn't exist yet. The environment guard alone closes the actual reported gap (anonymous reachability in Production) while preserving current Staging/Development behavior. Option 3 (global `FallbackPolicy`) is the more systemic fix but is explicitly out of scope per the spec — it risks affecting unauthenticated-by-design endpoints elsewhere (health checks, etc.) and deserves its own reviewed change, not to be bundled into a one-line fix for this specific finding.

#### Decision 2: Extract the guard into a shared helper, or leave it duplicated a fourth time?
**Options considered:**
1. Copy the same 4-line guard block into `GetEnvironmentInfo`, as the third existing occurrence was copied into the second and third.
2. Extract a `private bool IsE2EEnvironmentAllowed()` (or an `ActionResult?`-returning helper) and call it from all four actions, removing the duplication.

**Chosen approach:** Option 1 for this fix's diff, but flag Option 2 as a natural low-risk follow-up (not required to close this finding — see Specification Amendments).

**Rationale:** The spec (FR-2) explicitly scopes this change to `GetEnvironmentInfo` only, to keep the diff minimal and reviewable and avoid touching the three already-correct actions. Refactoring all four call sites at once is reasonable but is a separate, slightly larger-surface-area change; doing it here would mean the diff modifies code that isn't part of the reported bug. Developers are free to extract the helper in this same PR if they judge the four-line duplication not worth a fourth copy, but it is not required.

## Implementation Guidance

### Directory / Module Structure
No new files. Single edit in `backend/src/Anela.Heblo.API/Controllers/E2ETestController.cs`, inside the existing `GetEnvironmentInfo` method (lines 40-54).

### Interfaces and Contracts
No interface changes. `IWebHostEnvironment _environment` is already injected into the controller and used identically by the other three actions — no new dependency required.

New response contract for the out-of-environment case only:
```csharp
NotFound(new { error = "E2E endpoints only available in Staging or Development environment", currentEnvironment = _environment.EnvironmentName })
```
This is the exact literal shape already used by `CreateE2ESession`, `GetAuthStatus`, and `GetE2EApp` — reuse it verbatim rather than inventing a new error shape, so E2E tooling and log-scanning that may already expect this shape stays consistent.

### Data Flow
Unchanged for Staging/Development. For any other environment: request → `GetEnvironmentInfo` → guard clause evaluates `_environment.IsEnvironment("Staging") || _environment.IsDevelopment()` → `false` → `404 NotFound` returned immediately, no environment data ever constructed or serialized.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Something in Staging or Development currently depends on `env-info` being reachable in an environment name that doesn't literally match `"Staging"`/`IsDevelopment()` (e.g. a `Test` environment used by CI) | Low | The guard clause is copied verbatim from three siblings already gating the whole controller's real traffic; if those three haven't broken E2E/CI, this one won't either. Grep found no other environment name used for E2E tooling. |
| Diff creep — touching the other three actions "while we're in the file" | Low | FR-2 explicitly scopes the diff to `GetEnvironmentInfo`; reviewers should reject unrelated changes to the sibling actions in this PR. |

## Specification Amendments
None required — the spec's FR-1/FR-2 scoping is architecturally sound and matches the minimal, consistent fix. One optional follow-up worth filing separately (not part of this change): extract the four-times-duplicated environment guard into a single private helper or an action filter, to prevent a fifth new E2E action from being added later without the guard (the same class of bug this issue reports).

## Prerequisites
None. No migrations, no config, no infrastructure changes — the fix is a self-contained code change deployable through the normal pipeline.
