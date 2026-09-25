# Architecture Review: Remove out-of-scope Timestamp field from GetConfigurationResponse

## Skip Design: true

This is a backend DTO/handler field removal plus a trivial frontend call-site simplification and generated-client regeneration. There are no new or changed UI components, screens, layouts, or visual design decisions — the designer phase should produce a minimal pass-through design note (or the orchestrator may treat it as a no-op) rather than a UX design.

## Architectural Fit Assessment

The spec's diagnosis matches the codebase exactly. `docs/architecture/development_guidelines.md` (line 15) states:

> The `Configuration` module exposes only application-wide values (version, environment, mock-auth flag). Module-specific values ... live on a module-owned anonymous endpoint.

`GetConfigurationResponse` (`backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationResponse.cs`) currently declares exactly the three documented fields (`Version`, `Environment`, `UseMockAuth`) plus the undocumented `Timestamp`. `GetConfigurationHandler.Handle` sets `Timestamp = DateTime.UtcNow` inline — it is server-clock-at-request-time, not a configuration value, and every other field in the handler comes from `IConfiguration` or assembly metadata. Removing `Timestamp` restores the DTO to exactly the documented contract with zero ambiguity about what belongs.

I verified the full blast radius by grep across both backend and frontend (excluding EF `*Configuration.cs` persistence classes, which are an unrelated naming collision with "Configuration" and do not reference this DTO). The following files reference `Timestamp`/`timestamp` on this specific response and must all be touched:

**Backend:**
- `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationResponse.cs` — declares the field (per spec FR-1).
- `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs` — sets the field (per spec FR-2).
- `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationEndpointTests.cs` — **not covered by the spec, but required.** `GetConfiguration_ShouldReturnValidConfigurationResponse` (lines 45–46) asserts `configResponse.Timestamp > DateTime.MinValue` and `configResponse.Timestamp <= DateTime.UtcNow.AddMinutes(1)`. This will fail to compile once `Timestamp` is removed from the DTO. These two assertion lines must be deleted; the rest of the test (version/environment null checks) is unaffected and stays.

**Frontend:**
- `frontend/src/services/versionService.ts` (lines 98–99) — the call site named in the issue (per spec FR-3).
- `frontend/src/api/generated/api-client.ts` — generated file; `GetConfigurationResponse.timestamp?: Date` and its `IGetConfigurationResponse` interface counterpart and `init()` deserialization line will disappear automatically once the client is regenerated post-backend-change (per spec FR-4). Do not hand-edit this file — see Prerequisites.
- `frontend/src/services/__tests__/versionService.test.ts` — **not covered by the spec, but required.** `makeMockApiClient()` (line ~23) mocks `configuration_GetConfiguration` to resolve `{ version, environment, useMockAuth, timestamp: new Date('2024-01-01T00:00:00Z') }`. Leaving `timestamp` in the mock is harmless (extra mock property, TypeScript structural typing won't complain since the mock isn't statically typed against the generated interface) but is dead/misleading test fixture data now that nothing reads `response.timestamp`; remove it for clarity. Confirm no test in this file asserts on the returned `timestamp` value being anything other than a valid ISO string (a quick scan shows the tests assert on `hasUpdate`/version fields, not literal timestamp equality, so no test logic changes are expected beyond the fixture cleanup) — an assertion pinned to `'2024-01-01T00:00:00Z'` would need updating to a loose "is an ISO string" check instead, since the value now always comes from the live clock.

**Confirmed NOT affected** (checked and safe to leave untouched):
- `backend/src/Anela.Heblo.API/Controllers/ConfigurationController.cs` — thin pass-through, references only the request/response types, not the `Timestamp` field.
- `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationRequest.cs` — request has no timestamp concept.
- `frontend/src/api/hooks/useConfiguration.ts` — returns the whole `GetConfigurationResponse` via TanStack Query without touching individual fields.
- Other frontend hits on `.timestamp` (`ManufacturedInventoryPage.tsx`, `useLastAddedItem.ts`, `useLastManufacturedItems.ts`, `ClassificationHistoryPage.tsx`) are unrelated domain objects (manufacturing/classification timestamps), not this configuration response — confirmed by reading their import sources.

## Proposed Architecture

### Component Overview

```
[ConfigurationController] --(MediatR)--> [GetConfigurationHandler] --> [GetConfigurationResponse]
                                                                              |
                                                                     (Version, Environment,
                                                                      UseMockAuth only)
                                                                              |
                                                        NSwag codegen (build-time, automatic)
                                                                              |
                                                                              v
                                                        [api-client.ts: GetConfigurationResponse]
                                                                              |
                                                                              v
                                              [versionService.checkVersion()] --> VersionInfo
                                              { version, environment, useMockAuth,
                                                timestamp: new Date().toISOString() }  <-- always local clock now
```

No new components; this is a subtractive change to an existing, well-bounded vertical slice.

### Key Design Decisions

#### Decision 1: Delete rather than deprecate
**Options considered:** (a) Mark `Timestamp` `[Obsolete]` for a transition period; (b) remove it outright now.
**Chosen approach:** Remove outright now.
**Rationale:** The project has one solo developer + AI review, no external/third-party consumers of `/api/configuration` (single internal SPA consumer, confirmed by search), and the frontend already tolerates the field's absence via its existing fallback. A deprecation period adds process overhead with no compatibility benefit here. This matches the issue's own suggested fix and the "no behavior change" framing in the spec.

#### Decision 2: Regenerate the client instead of hand-editing generated code
**Options considered:** (a) Hand-patch `api-client.ts` to drop the `timestamp` field immediately; (b) change only the backend DTO and let the standard `npm run generate-client` prebuild step regenerate the client as part of normal build.
**Chosen approach:** (b) — change the backend DTO/handler, then run the documented generation command (`dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual` or `npm run generate-client` from `frontend/`) once, and commit the regenerated `api-client.ts` alongside the backend change in the same unit of work.
**Rationale:** `docs/development/api-client-generation.md` documents this file as auto-generated from the OpenAPI spec; hand-editing it would immediately drift from source of truth on the next build and risks editing mistakes (e.g. missing the `IGetConfigurationResponse` interface's matching field). The task-plan should include an explicit "regenerate client" step, not rely on CI/dev doing it silently, since `versionService.ts`'s edit depends on the field truly being gone to catch any missed reference at compile time.

## Implementation Guidance

### Directory / Module Structure
No new files or directories. All edits are in-place to existing files:
- `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationResponse.cs`
- `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs`
- `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationEndpointTests.cs`
- `frontend/src/api/generated/api-client.ts` (regenerated, not hand-written)
- `frontend/src/services/versionService.ts`
- `frontend/src/services/__tests__/versionService.test.ts`

### Interfaces and Contracts
`GetConfigurationResponse` (C# class, per project convention — DTOs are never records) becomes:
```csharp
public class GetConfigurationResponse : BaseResponse
{
    public string Version { get; set; } = default!;
    public string Environment { get; set; } = default!;
    public bool UseMockAuth { get; set; }
}
```
No `Timestamp` property, no `DateTime` using-directive dependency introduced by it (the handler's `using System;`-level `DateTime.UtcNow` reference for this purpose is removed; verify no other `DateTime` usage in the handler needs the import kept — a quick check shows none, so no stray `using` cleanup is needed since `DateTime` isn't explicitly imported as a separate using in the handler file today).

`VersionInfo` (frontend local type in `versionService.ts`, line ~7) is **unchanged** — it still has a `timestamp: string` field; only its *source expression* changes from `response.timestamp?.toISOString() || new Date().toISOString()` to `new Date().toISOString()`.

### Data Flow
1. Client calls `GET /api/configuration`.
2. `GetConfigurationHandler` builds `ApplicationConfiguration` (version/environment/mock-auth) exactly as today — this logic is entirely untouched.
3. Handler returns `GetConfigurationResponse { Version, Environment, UseMockAuth }` — no server clock read.
4. `versionService.checkVersion()` maps the response into `VersionInfo`, filling `timestamp` from `new Date().toISOString()` (the browser's own clock) unconditionally, exactly as it already did in the fallback branch.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Backend test `GetConfigurationEndpointTests.cs` fails to compile after DTO change (not mentioned in original spec) | Medium | Task plan must include removing the two `Timestamp`-asserting lines (45–46) in the same commit as the DTO change, or the build breaks. |
| Regenerated `api-client.ts` diff includes unrelated churn (e.g. timestamp regeneration, formatting) if generated with a stale toolchain version | Low | Regenerate via the documented command only; review the diff is scoped to the `GetConfigurationResponse`/`IGetConfigurationResponse` block before committing. |
| Frontend unit test mock (`versionService.test.ts`) still hard-codes a `timestamp` field in its mock response, silently masking that the field is no longer read | Low | Remove the now-irrelevant `timestamp` key from `makeMockApiClient()`'s mock payload for clarity; no test assertion currently depends on the literal mocked value, so behavior of the test suite is unaffected either way — this is a cleanliness fix, not a required correctness fix. |
| Someone else added a consumer of `response.timestamp` since the issue was filed | Low | `npm run build` (TypeScript strict compilation) will fail loudly on any remaining reference once the generated type drops the field — this is a hard compile-time safety net per NFR-1/FR-4. |

## Specification Amendments

The spec (FR-1–FR-4) is accurate but incomplete on test fallout. Add:

- **FR-5 (new): Update backend integration test.** Remove lines 45–46 of `GetConfigurationEndpointTests.cs` (`configResponse.Timestamp > DateTime.MinValue` and the `<=` assertion) from `GetConfiguration_ShouldReturnValidConfigurationResponse`. Do not remove or rename the test method itself — its remaining assertions (`Version`, `Environment` not null) stay valid and valuable.
- **FR-6 (new): Clean up frontend test fixture.** Remove the now-unused `timestamp: new Date('2024-01-01T00:00:00Z')` line from `makeMockApiClient()` in `frontend/src/services/__tests__/versionService.test.ts`. No test assertions need to change since none currently pin the returned `timestamp` value.

Both are mechanical, low-risk, and required for `dotnet build`/`npm run build`/test-suite green per this project's own validation-before-completion rule — they are not optional cleanup.

## Prerequisites

- No migrations, config, or infrastructure changes.
- Implementation order matters for a clean compile: (1) edit backend DTO + handler, (2) run the client regeneration command per `docs/development/api-client-generation.md` (`npm run generate-client` from `frontend/`, or the equivalent `dotnet msbuild ... -t:GenerateFrontendClientManual`), (3) edit `versionService.ts`, (4) edit the two test files, (5) run `dotnet build` + `dotnet format` and `npm run build` + `npm run lint` per project validation rules, (6) run the affected test suites (`GetConfigurationEndpointTests`, `versionService.test.ts`).
