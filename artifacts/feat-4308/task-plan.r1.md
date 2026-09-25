# Remove Out-of-Scope Timestamp Field From GetConfigurationResponse Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the undocumented `Timestamp` field from `GetConfigurationResponse` (and its handler assignment), regenerate the OpenAPI client, and simplify the frontend's `versionService.ts` to always source its local `timestamp` from the browser clock — restoring the Configuration module's `/api/configuration` endpoint to its documented contract (version, environment, mock-auth flag only) with no behavior change.

**Architecture:** Three sequential tasks: (1) remove the field from the backend DTO/handler and update the backend integration test that asserted on it, (2) regenerate the NSwag-generated TypeScript client so the frontend type drops `timestamp`, (3) simplify the frontend call site and its unit test fixture now that the backend never sends a timestamp. Order matters — the client must be regenerated before the frontend edit, or `versionService.ts` would reference a field TypeScript no longer knows about mid-way through.

**Tech Stack:** .NET 8 (MediatR, xUnit, FluentAssertions), React/TypeScript (NSwag-generated Fetch client, Jest), no database involved.

---

### task: remove-backend-timestamp-field

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationResponse.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationEndpointTests.cs`

- [ ] **Step 1: Confirm the current failing-safe baseline by running the existing test**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~GetConfigurationEndpointTests`
Expected: PASS (all 5 tests green) — this confirms the starting state before any edits.

- [ ] **Step 2: Remove the `Timestamp`-asserting lines from the integration test**

In `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationEndpointTests.cs`, in `GetConfiguration_ShouldReturnValidConfigurationResponse`, change:

```csharp
    [Fact]
    public async Task GetConfiguration_ShouldReturnValidConfigurationResponse()
    {
        // Act
        var response = await _client.GetAsync("/api/configuration");
        var configResponse = await response.Content.ReadFromJsonAsync<GetConfigurationResponse>();

        // Assert
        configResponse.Should().NotBeNull();
        configResponse.Version.Should().NotBeNull();
        configResponse.Environment.Should().NotBeNull();
        (configResponse.Timestamp > DateTime.MinValue).Should().BeTrue();
        Assert.True(configResponse.Timestamp <= DateTime.UtcNow.AddMinutes(1)); // Allow 1 minute tolerance
    }
```

to:

```csharp
    [Fact]
    public async Task GetConfiguration_ShouldReturnValidConfigurationResponse()
    {
        // Act
        var response = await _client.GetAsync("/api/configuration");
        var configResponse = await response.Content.ReadFromJsonAsync<GetConfigurationResponse>();

        // Assert
        configResponse.Should().NotBeNull();
        configResponse.Version.Should().NotBeNull();
        configResponse.Environment.Should().NotBeNull();
    }
```

Leave the other three test methods (`GetConfiguration_ShouldReturnSuccessAndCorrectContentType`, `GetConfiguration_ShouldIncludeMockAuthFlag`, `GetConfiguration_ShouldReturnTestEnvironment`, `GetConfiguration_ShouldReturnValidVersion`) untouched.

- [ ] **Step 3: Run the test to verify it still passes (field still exists on the DTO at this point)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~GetConfigurationEndpointTests`
Expected: PASS — the assertions removed were the only ones referencing `Timestamp`; nothing else changed yet.

- [ ] **Step 4: Remove the `Timestamp` property from `GetConfigurationResponse`**

In `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationResponse.cs`, change:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Configuration;

/// <summary>
/// Response containing application configuration information
/// </summary>
public class GetConfigurationResponse : BaseResponse
{
    /// <summary>
    /// Application version from CI/CD pipeline or assembly
    /// </summary>
    public string Version { get; set; } = default!;

    /// <summary>
    /// Current environment (Development, Test, Production)
    /// </summary>
    public string Environment { get; set; } = default!;

    /// <summary>
    /// Whether mock authentication is enabled
    /// </summary>
    public bool UseMockAuth { get; set; }

    /// <summary>
    /// Response timestamp in UTC
    /// </summary>
    public DateTime Timestamp { get; set; }
}
```

to:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Configuration;

/// <summary>
/// Response containing application configuration information
/// </summary>
public class GetConfigurationResponse : BaseResponse
{
    /// <summary>
    /// Application version from CI/CD pipeline or assembly
    /// </summary>
    public string Version { get; set; } = default!;

    /// <summary>
    /// Current environment (Development, Test, Production)
    /// </summary>
    public string Environment { get; set; } = default!;

    /// <summary>
    /// Whether mock authentication is enabled
    /// </summary>
    public bool UseMockAuth { get; set; }
}
```

- [ ] **Step 5: Remove the `Timestamp` assignment from `GetConfigurationHandler`**

In `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs`, change:

```csharp
        var response = new GetConfigurationResponse
        {
            Version = appConfig.Version,
            Environment = appConfig.Environment,
            UseMockAuth = appConfig.UseMockAuth,
            Timestamp = DateTime.UtcNow,
        };
```

to:

```csharp
        var response = new GetConfigurationResponse
        {
            Version = appConfig.Version,
            Environment = appConfig.Environment,
            UseMockAuth = appConfig.UseMockAuth,
        };
```

Do not touch any other line in this file — `BuildApplicationConfiguration()` and `GetVersionFromSources()` are unrelated and must stay exactly as they are.

- [ ] **Step 6: Build the backend and run the full Configuration test file to verify everything compiles and passes**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: Build succeeded, 0 errors.

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~GetConfigurationEndpointTests`
Expected: PASS (all 4 remaining tests green).

- [ ] **Step 7: Run `dotnet format` and commit**

Run: `dotnet format backend/Anela.Heblo.sln`

```bash
git add backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationResponse.cs backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationEndpointTests.cs
git commit -m "fix: remove out-of-scope Timestamp field from GetConfigurationResponse"
```

---

### task: regenerate-frontend-client

**Files:**
- Modify (generated, do not hand-edit content beyond what regeneration produces): `frontend/src/api/generated/api-client.ts`

**Prerequisite:** `task: remove-backend-timestamp-field` must be complete and the backend must build successfully — the client is generated from the live backend OpenAPI spec, so the `Timestamp` field must already be gone from `GetConfigurationResponse` before regenerating, or this step is a no-op.

- [ ] **Step 1: Regenerate the TypeScript client from the updated backend**

Run: `cd frontend && npm run generate-client`

This runs `dotnet msbuild ../backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual` per `docs/development/api-client-generation.md`, which rebuilds the backend's OpenAPI spec and re-emits `frontend/src/api/generated/api-client.ts` via NSwag.

- [ ] **Step 2: Verify the generated diff is scoped to the `GetConfigurationResponse`/`IGetConfigurationResponse` block**

Run: `git diff frontend/src/api/generated/api-client.ts`
Expected: The diff removes exactly:
- `timestamp?: Date;` from `export interface IGetConfigurationResponse { ... }`
- `timestamp?: Date;` from `export class GetConfigurationResponse extends BaseResponse implements IGetConfigurationResponse { ... }`
- the line `this.timestamp = _data["timestamp"] ? new Date(_data["timestamp"].toString()) : <any>undefined;` from `GetConfigurationResponse.init()`
- any corresponding `timestamp` serialization line in `GetConfigurationResponse.toJSON()`, if present

If the diff contains unrelated changes (e.g. from an out-of-date NSwag toolchain producing incidental formatting churn across the whole file), do not hand-trim it — that would drift from the generator's actual output. Instead, verify the NSwag/toolchain version matches what CI uses (see `docs/development/api-client-generation.md`) before proceeding, since this file must always match what `npm run generate-client` produces byte-for-byte.

- [ ] **Step 3: Confirm the frontend still type-checks with the field gone (expected to fail here — this is deliberate)**

Run: `cd frontend && npx tsc --noEmit`
Expected: FAIL, with a TypeScript error in `frontend/src/services/versionService.ts` at the line referencing `response.timestamp` (property `timestamp` does not exist on type `GetConfigurationResponse`). This confirms the generated client actually dropped the field and that the next task's edit is necessary — do not treat this as a broken build to fix in this task; the fix is `task: simplify-frontend-timestamp-usage`.

- [ ] **Step 4: Commit the regenerated client**

```bash
git add frontend/src/api/generated/api-client.ts
git commit -m "chore: regenerate OpenAPI client without Timestamp field"
```

---

### task: simplify-frontend-timestamp-usage

**Files:**
- Modify: `frontend/src/services/versionService.ts`
- Modify: `frontend/src/services/__tests__/versionService.test.ts`

**Prerequisite:** `task: regenerate-frontend-client` must be complete — this task fixes the compile error that task deliberately left in place.

- [ ] **Step 1: Update the failing test fixture first**

In `frontend/src/services/__tests__/versionService.test.ts`, in `makeMockApiClient()`, change:

```typescript
function makeMockApiClient(version: string) {
  return {
    configuration_GetConfiguration: jest.fn().mockResolvedValue({
      version,
      environment: 'test',
      useMockAuth: false,
      timestamp: new Date('2024-01-01T00:00:00Z'),
    }),
  };
}
```

to:

```typescript
function makeMockApiClient(version: string) {
  return {
    configuration_GetConfiguration: jest.fn().mockResolvedValue({
      version,
      environment: 'test',
      useMockAuth: false,
    }),
  };
}
```

- [ ] **Step 2: Run the frontend test suite to confirm no test asserts on the literal mocked timestamp value**

Run: `cd frontend && npx jest src/services/__tests__/versionService.test.ts`
Expected: PASS — if any test fails here asserting `timestamp` equals `'2024-01-01T00:00:00Z'` or similar, that assertion must be loosened to check the value is a valid ISO-8601 string (e.g. `expect(() => new Date(result.timestamp).toISOString()).not.toThrow()` or `expect(result.timestamp).toEqual(expect.any(String))`), since `checkVersion()` will populate it from the live clock after Step 3 below. Re-run after loosening until green.

- [ ] **Step 3: Simplify the `checkVersion()` call site**

In `frontend/src/services/versionService.ts`, change:

```typescript
      return {
        version: response.version || "0.0.0",
        environment: response.environment || "unknown",
        useMockAuth: response.useMockAuth || false,
        timestamp:
          response.timestamp?.toISOString() || new Date().toISOString(),
      };
```

to:

```typescript
      return {
        version: response.version || "0.0.0",
        environment: response.environment || "unknown",
        useMockAuth: response.useMockAuth || false,
        timestamp: new Date().toISOString(),
      };
```

Do not change the `VersionInfo` type declaration (line ~7) — `timestamp: string` stays as-is; only its source expression here changes.

- [ ] **Step 4: Type-check and run the full frontend build**

Run: `cd frontend && npx tsc --noEmit`
Expected: PASS, 0 errors (this resolves the deliberate failure left by the previous task's Step 3).

Run: `cd frontend && npm run build`
Expected: Build succeeds.

- [ ] **Step 5: Run the frontend test suite and lint**

Run: `cd frontend && npx jest src/services/__tests__/versionService.test.ts`
Expected: PASS (all tests green).

Run: `cd frontend && npm run lint`
Expected: No new lint errors introduced by this change.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/services/versionService.ts frontend/src/services/__tests__/versionService.test.ts
git commit -m "fix: source versionService timestamp from local clock only"
```

---

## Self-Review

**Spec coverage:**
- FR-1 (remove `Timestamp` from DTO) → `task: remove-backend-timestamp-field`, Step 4.
- FR-2 (remove assignment from handler) → `task: remove-backend-timestamp-field`, Step 5.
- FR-3 (simplify frontend consumer) → `task: simplify-frontend-timestamp-usage`, Step 3.
- FR-4 (regenerate OpenAPI client) → `task: regenerate-frontend-client`, Steps 1–2.
- Arch-review FR-5 (update backend integration test) → `task: remove-backend-timestamp-field`, Step 2.
- Arch-review FR-6 (clean up frontend test fixture) → `task: simplify-frontend-timestamp-usage`, Step 1.
- NFR-1 (no behavior change) → verified structurally: the frontend's effective value is `new Date().toISOString()` in both the pre- and post-change code paths (it was already the fallback), and Step 2 of the last task requires loosening any test that pinned a literal mock timestamp, so no test encodes a false expectation of backend-sourced time.

**Placeholder scan:** No "TBD"/"TODO"/"handle appropriately" language; every step shows exact before/after code and exact commands with expected output.

**Type consistency:** `GetConfigurationResponse` (backend class and generated frontend class), `VersionInfo`, `checkVersion()`, and `makeMockApiClient()` are referenced with matching names and shapes across all three tasks.
