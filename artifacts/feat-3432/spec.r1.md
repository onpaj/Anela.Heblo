# Specification: Remove Timestamp from ApplicationConfiguration Domain Entity

## Summary

`ApplicationConfiguration` currently captures `DateTime.UtcNow` in its constructor, conflating a transport-layer concern (response generation time) with domain state and introducing non-determinism into an otherwise pure value object. This refactor removes `Timestamp` from the domain entity and moves it to the handler at response-construction time, restoring SRP compliance and full testability.

## Background

`ApplicationConfiguration` is a domain entity that models the running application's version, environment, and authentication mode. None of these values are time-dependent at the domain level; the entity has no business need for a "created at" field.

`Timestamp` was added solely to populate `GetConfigurationResponse.Timestamp`, which tells the HTTP consumer when the response was generated. This is a transport/serialization concern and belongs in the application layer, not the domain. As a direct consequence, every constructor call produces a different object (different `Timestamp`), forcing unit tests to use a loose tolerance assertion (`Timestamp <= DateTime.UtcNow.AddMinutes(1)`) rather than an exact value. Clean Architecture requires domain entities to be pure, deterministic, and free of infrastructure side effects.

## Functional Requirements

### FR-1: Remove `Timestamp` property from `ApplicationConfiguration`

Remove the `Timestamp` property declaration and its assignment (`Timestamp = DateTime.UtcNow`) from `ApplicationConfiguration`.

**Files affected:**
- `backend/src/Anela.Heblo.Domain/Features/Configuration/ApplicationConfiguration.cs`

**Acceptance criteria:**
- `ApplicationConfiguration` has no `Timestamp` property.
- The `ApplicationConfiguration` constructor accepts the same three parameters (`version`, `environment`, `useMockAuth`) and sets only those three properties.
- `CreateWithDefaults` continues to work and delegates to the updated constructor.
- Two `ApplicationConfiguration` instances constructed with identical arguments compare as semantically equivalent (no non-deterministic state).

### FR-2: Set `Timestamp` in `GetConfigurationHandler` at response-construction time

After removing `Timestamp` from the domain entity, assign `DateTime.UtcNow` directly to `GetConfigurationResponse.Timestamp` inside the handler's `Handle` method, immediately before returning.

**Files affected:**
- `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs`

**Acceptance criteria:**
- `GetConfigurationResponse.Timestamp` is set to `DateTime.UtcNow` inside `Handle`, not sourced from `appConfig`.
- `GetConfigurationResponse` retains its `Timestamp` property (no change to the DTO).
- The handler compiles and all existing handler tests pass without modification to the tests themselves.

### FR-3: Update unit tests to assert `Timestamp` exactly (or remove the tolerance workaround)

With `Timestamp` no longer embedded in the domain entity, handler unit tests can capture the time window precisely. The existing loose tolerance assertion in `GetConfigurationHandlerTests` should be tightened or the test coverage extended to verify that `Timestamp` reflects the moment `Handle` was called.

**Files affected:**
- `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs`

**Acceptance criteria:**
- The test suite for `GetConfigurationHandler` asserts `Timestamp` with a tight bound (e.g., within 1 second of calling `Handle`, using `BeCloseTo`) rather than relying on a multi-minute tolerance.
- No test uses `Timestamp` sourced from `ApplicationConfiguration` to form its assertion.

**Note:** The integration test `GetConfigurationEndpointTests` uses the same loose bound (`DateTime.UtcNow.AddMinutes(1)`). That assertion remains acceptable as-is because network/test-host overhead is real; it does not need to change, but it may be tightened opportunistically if desired.

### FR-4: Verify `GetConfigurationResponse.Timestamp` is not changed

`GetConfigurationResponse` is a DTO / API contract class. The `Timestamp` property must remain in place and continue to be serialised in the API response. Only its assignment source changes (from `appConfig.Timestamp` to inline `DateTime.UtcNow`).

**Files affected:**
- `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationResponse.cs` — no changes required.

**Acceptance criteria:**
- `GET /api/configuration` continues to return a `timestamp` field in the JSON response.
- The field value is a valid UTC datetime close to the time of the HTTP request.

## Non-Functional Requirements

### NFR-1: Performance

No performance impact. The change is a property removal and an assignment relocation; no new allocations or I/O are introduced.

### NFR-2: Correctness / Determinism

After this change, `ApplicationConfiguration` must be a deterministic value object: equal inputs produce semantically equal outputs. This is a correctness requirement, not a style preference.

### NFR-3: Backward Compatibility

The public HTTP API response shape (`GET /api/configuration`) must remain unchanged. The `timestamp` JSON field must still be present and populated. No frontend or consumer changes are required.

### NFR-4: Test Coverage

All currently passing tests must continue to pass. The handler unit tests must be updated so that the Timestamp assertion is meaningful rather than a workaround (see FR-3).

## Data Model

No database tables or persistent entities are involved. `ApplicationConfiguration` is a transient in-memory value object constructed on each request.

**Before (domain entity carries transport metadata):**

```
ApplicationConfiguration
  Version     : string   (domain)
  Environment : string   (domain)
  UseMockAuth : bool     (domain)
  Timestamp   : DateTime (transport — REMOVE)
```

**After (pure domain value object):**

```
ApplicationConfiguration
  Version     : string   (domain)
  Environment : string   (domain)
  UseMockAuth : bool     (domain)

GetConfigurationResponse            (application / transport layer)
  Version     : string
  Environment : string
  UseMockAuth : bool
  Timestamp   : DateTime  ← set in handler, not sourced from domain entity
```

## API / Interface Design

`GET /api/configuration` response shape is unchanged:

```json
{
  "version": "2.5.1-ci.42",
  "environment": "Production",
  "useMockAuth": false,
  "timestamp": "2026-06-30T10:15:30.123Z"
}
```

The only behavioural difference is that `timestamp` now reflects the instant the handler constructs the response rather than the instant `ApplicationConfiguration` was instantiated (the two were previously effectively the same instant; the observable difference is zero for callers).

## Dependencies

- No external service dependencies.
- MediatR pipeline is unaffected (request/response types do not change).
- OpenAPI TypeScript client: the response DTO shape is unchanged, so no client regeneration is needed.

## Out of Scope

- Changes to any other domain entity.
- Changes to `ConfigurationController`, `ConfigurationModule`, or `GetConfigurationRequest`.
- Changes to the `GetConfigurationResponse` DTO (property set stays the same).
- Replacing `DateTime.UtcNow` with an injected `ISystemClock` / `TimeProvider` abstraction. That is a worthwhile future improvement but is explicitly not part of this refactor — the goal is the minimum surgical change that fixes the SRP and non-determinism violations.
- Modifying the integration test `GetConfigurationEndpointTests` — its existing assertions remain valid.
- Frontend changes.

## Open Questions

None.

## Status: COMPLETE
