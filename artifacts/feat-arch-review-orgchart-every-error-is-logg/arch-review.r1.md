I have enough context. The proposal in the spec is straightforward and aligns with the existing `OrgChartController` already owning a single error log site. Let me write the review.

```markdown
# Architecture Review: OrgChart Service — Consolidate Error Logging to Single Site

## Skip Design: true

This change is a backend-only refactor of error-logging behavior. No UI, route, response shape, or visual component changes are introduced.

## Architectural Fit Assessment

The proposal aligns cleanly with the codebase's existing conventions:

- **Vertical Slice + MediatR layering is preserved.** The change touches only the `OrgChart` slice (`Anela.Heblo.Application/Features/OrgChart/Infrastructure/OrgChartService.cs`, `Anela.Heblo.API/Controllers/OrgChartController.cs`). No cross-slice coupling is introduced and no public contract changes.
- **Single error-logging owner is the dominant existing pattern.** Surveying `Anela.Heblo.API/Controllers/*.cs` (`BankStatementsController`, `BackgroundRefreshController`, `LeafletController`, `SmartsuppWebhookController`, `OrgChartController`), each controller already owns its `catch (Exception) → _logger.LogError(...)` block while underlying application services generally do not log-and-rethrow. OrgChart is the outlier — fixing it brings the slice into compliance with the de-facto convention.
- **Existing test enforces the chosen owner.** `GetOrganizationStructureHandlerTests.cs:63-73` already asserts the handler does not emit `LogError` and explicitly states "the controller owns failure logging." Picking the controller as the single owner closes the contradiction without rewriting any test assertion intent.
- **Observability strategy is respected.** `docs/architecture/observability.md` (§"Logging Strategy") classes `Error` as "Always logged" and does not de-duplicate downstream. The current duplicate amplifies error-rate signals against the design intent. Removing the service-side log corrects the signal-to-noise ratio without losing any data: Application Insights also automatically captures unhandled/captured `Exception` telemetry once via standard ASP.NET Core instrumentation, so the controller's `LogError(ex, ...)` remains the single explicit log line and the exception chain (including the `HttpRequestException`/`JsonException` inner) is preserved.

The proposal is the minimal change that delivers the desired outcome. No architectural deviation is required.

## Proposed Architecture

### Component Overview

```
┌────────────────────────────────────┐
│ OrgChartController (API layer)     │
│  - try/catch (Exception)           │
│  - LogError(ex, ...)   ← ONLY site │  ← single failure-log owner
│  - returns 500 + error envelope    │
└──────────────┬─────────────────────┘
               │ IMediator.Send
               ▼
┌────────────────────────────────────┐
│ GetOrganizationStructureHandler    │
│  - LogInformation (handle entry)   │
│  - no error log; exception bubbles │
└──────────────┬─────────────────────┘
               │ IOrgChartService
               ▼
┌────────────────────────────────────┐
│ OrgChartService (Infrastructure)   │
│  - LogInformation (start/success)  │
│  - catch + WRAP + THROW            │  ← no LogError calls
│  - catch (Exception) re-throw      │
└────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Controller is the single error-logging owner
**Options considered:**
- (A) Controller owns logging; service catches and re-throws silently.
- (B) Service owns logging; controller has no try/catch (relies on global ASP.NET Core exception middleware).
- (C) Both keep logging (status quo) — rejected, that is the bug.

**Chosen approach:** (A).

**Rationale:** Matches the existing convention across all surveyed controllers in `Anela.Heblo.API/Controllers/`. Aligns with the pre-existing test assertion at `GetOrganizationStructureHandlerTests.cs:63-73`. Option (B) is a larger architectural change (introduce global exception middleware, migrate all controllers, adjust response envelopes) and is explicitly listed as Out of Scope in the spec. Choosing (A) means zero changes to other modules.

#### Decision 2: Preserve typed-exception wrapping in the service
**Options considered:**
- (A) Service still wraps `HttpRequestException` → `InvalidOperationException` and `JsonException` → `InvalidOperationException`, re-throws other `Exception` unchanged. (Current behavior, minus the logs.)
- (B) Replace `InvalidOperationException` with new typed exceptions (`OrgChartFetchException`, `OrgChartParseException`).

**Chosen approach:** (A).

**Rationale:** Spec marks (B) as out of scope. The handler/controller already treat all errors via a generic `catch (Exception)` and do not branch on type, so (B) provides no observable benefit today. Preserving the wrap also preserves the existing public contract of the service — no callers (current or test) need to change.

#### Decision 3: Keep the controller's per-action try/catch — do not introduce global middleware
**Options considered:**
- (A) Keep per-action `try/catch` (status quo).
- (B) Introduce ASP.NET Core exception-handling middleware (`IExceptionHandler` or `UseExceptionHandler`).

**Chosen approach:** (A).

**Rationale:** (B) is explicitly out of scope in the spec and would require touching `Anela.Heblo.API` startup, every existing controller, and the response envelope contract. Worth a separate initiative; out of scope here.

#### Decision 4: Leave the in-method `LogError` at `OrgChartService.cs:49` in place
The `if (orgChart == null) { _logger.LogError(...); throw new InvalidOperationException(...); }` block at lines 47–51 also produces a service-side error log and is **not** in any catch block. To deliver the spec's intent ("zero `LogError` calls in `GetOrganizationStructureAsync`"), this call must also be removed; the `throw new InvalidOperationException("Failed to deserialize organizational structure")` will bubble to the controller which will log it once. See **Specification Amendments** below.

## Implementation Guidance

### Directory / Module Structure
No new files. Only the following are edited:

- `backend/src/Anela.Heblo.Application/Features/OrgChart/Infrastructure/OrgChartService.cs`
  - Remove `_logger.LogError(...)` at line 49 (the null-deserialization branch).
  - Remove `_logger.LogError(...)` at line 62 (HttpRequestException catch).
  - Remove `_logger.LogError(...)` at line 67 (JsonException catch).
  - Remove `_logger.LogError(...)` at line 72 (generic Exception catch).
  - Keep all `throw`/`throw new InvalidOperationException(...)` statements unchanged, including inner-exception parameter.
  - Keep `LogInformation` calls (lines 38, 53–56) unchanged.

- `backend/test/Anela.Heblo.Tests/Features/OrgChart/GetOrganizationStructureHandlerTests.cs`
  - Existing assertions remain valid (the handler still does not log on failure).
  - Update the inline comment at lines 66–74 from
    `// Assert: the handler does NOT emit its own LogError — the controller owns failure logging.`
    to reflect the cleaned-up rule (e.g. `// Assert: the handler does NOT emit its own LogError — the controller is the single error-logging site for the OrgChart slice.`). The contradiction the original comment defended against is now gone, but the assertion itself still encodes the rule.

- `backend/test/Anela.Heblo.Tests/Features/OrgChart/OrgChartServiceTests.cs` **(new file)**
  - Houses the three regression tests required by FR-4.

- `backend/src/Anela.Heblo.API/Controllers/OrgChartController.cs` — **no changes**. The current `catch (Exception ex) { _logger.LogError(ex, "Error fetching organizational structure"); ... }` block at lines 47–52 already satisfies FR-2: the exception itself (with inner `HttpRequestException`/`JsonException` and the URL in `ex.Message`) is passed to `LogError`, so `ex.ToString()` exposes the full chain.

### Interfaces and Contracts

No changes to `IOrgChartService`, `OrgChartResponse`, route, status codes, or response envelope. The only externally observable behavior change is the absence of one log entry per failure under the `Anela.Heblo.Application.Features.OrgChart.Infrastructure.OrgChartService` logger category.

### Data Flow

**Failure path (after change):**
1. `OrgChartController.GetOrganizationStructure` logs `Information("Fetching organizational structure")`.
2. Mediator dispatches → `GetOrganizationStructureHandler.Handle` logs `Information("Handling request to fetch organizational structure")`.
3. Handler invokes `IOrgChartService.GetOrganizationStructureAsync`.
4. Service logs `Information("Fetching organizational structure from {Url}")` and issues `_httpClient.GetAsync(...)`.
5. On `HttpRequestException` / `JsonException` / null-deserialization / unexpected `Exception` — service throws (wrapped or as-is). **No `LogError` is emitted by the service.**
6. Handler is non-catching; exception propagates to controller.
7. Controller's `catch (Exception ex)` emits **exactly one** `LogError(ex, "Error fetching organizational structure")` and returns `500 InternalServerError` with `{ error, message }` body.

**Happy path:** unchanged.

### Test Plan

#### Modify `GetOrganizationStructureHandlerTests.cs`
- Comment text only (no assertion changes). Existing assertions remain green.

#### Add `OrgChartServiceTests.cs` (covering FR-4)
Three tests, each:
1. Arrange a `Mock<HttpMessageHandler>` (or `HttpMessageHandler` test double) that throws the target exception when `_httpClient.GetAsync(...)` is invoked, plus a `Mock<ILogger<OrgChartService>>` and `IOptions<OrgChartOptions>` populated with a stub URL.
2. Act: invoke `GetOrganizationStructureAsync`.
3. Assert the expected typed exception:
   - `HttpRequestException` → expect `InvalidOperationException` whose `InnerException` is the original `HttpRequestException` and whose `Message` starts with `"Failed to fetch organizational structure: "`.
   - `JsonException` path (need a 200 response with malformed JSON body so deserialization fails): expect `InvalidOperationException` whose `InnerException` is `JsonException` and `Message` starts with `"Failed to parse organizational structure: "`.
   - Generic `Exception` (e.g. `OperationCanceledException` mid-flight or a custom test exception): expect that exact exception instance to propagate unwrapped.
4. Assert the logger received `Times.Never` for `LogLevel.Error`:
   ```csharp
   _loggerMock.Verify(
       x => x.Log(
           LogLevel.Error,
           It.IsAny<EventId>(),
           It.IsAny<It.IsAnyType>(),
           It.IsAny<Exception>(),
           It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
       Times.Never);
   ```

Add a fourth test for the null-deserialization branch (FR-amendment, see below): supply a valid JSON `"null"` body, expect `InvalidOperationException("Failed to deserialize organizational structure")`, and assert `LogError` Times.Never.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Operators relying on the `OrgChartService` logger category for alerting see a "drop" in error count and assume the issue is fixed. | Low | Spec NFR-3 already calls this out. Add a one-line CHANGELOG / release-note entry: *"OrgChart error logs are now emitted once (controller layer) instead of twice."* Alert rules keyed on `Anela.Heblo.Application.Features.OrgChart.Infrastructure.OrgChartService` may need to be re-pointed at the controller category or made category-agnostic. |
| Future maintainer re-adds `LogError` in `OrgChartService` without noticing the controller still logs. | Low | The new FR-4 regression tests will fail if any `LogError` is reintroduced. Update the comment in `GetOrganizationStructureHandlerTests` to also explain the rule, so it is discoverable. |
| Loss of structured `{Url}` field in logs (the service log used a templated `{Url}` parameter; the controller log does not). | Low–Medium | The URL is still recoverable from the exception object the controller passes to `LogError(ex, ...)` — both `HttpRequestException` and the wrapped `InvalidOperationException` include the request URI in their message chain, and Application Insights captures `ex.ToString()` including the inner chain. If structured `{Url}` searchability is required by ops, the controller line can be extended to `LogError(ex, "Error fetching organizational structure")` → `LogError(ex, "Error fetching organizational structure from {Url}", ???)`, but the controller does not have direct access to `OrgChartOptions`. Out of scope of this change; flag for follow-up only if ops requests it. |
| The null-deserialization branch at `OrgChartService.cs:49` still emits a service-side `LogError` after the change, partially defeating the goal. | Medium | Mandatory amendment — remove that `LogError` too. See **Specification Amendments**. |
| Application Insights automatic exception telemetry could be perceived as "another duplicate." | Low | This is platform telemetry (Exception table), not a log entry from our code, and is already present today. No additional change. |

## Specification Amendments

The spec must be tightened in one place: **FR-1 currently scopes the deletion to "the three `catch` blocks (lines 60–74)." There is a fourth `LogError` call at `OrgChartService.cs:49`** inside the happy-path `try` block (the null-deserialization guard) that also logs at `Error` level before throwing. Without removing it, the spec's own acceptance criterion ("zero `LogError`/`LogWarning`/`LogCritical` calls") is not met, and a null-deserialization failure still produces a duplicate log.

**Amend FR-1 to:**
> Remove **every** `_logger.LogError(...)` call from `OrgChartService.GetOrganizationStructureAsync`, specifically:
> - line 49 (null-deserialization guard inside the `try`)
> - line 62 (`HttpRequestException` catch)
> - line 67 (`JsonException` catch)
> - line 72 (generic `Exception` catch)
>
> All four `throw`/`throw new InvalidOperationException(...)` statements remain unchanged.

**Amend FR-4 acceptance criteria** to add a fourth regression test for the null-deserialization path (response is a 200 with body `"null"` → service throws `InvalidOperationException("Failed to deserialize organizational structure")` with no `LogError` invocation).

No other amendments needed.

## Prerequisites

None. This change is self-contained within the `OrgChart` vertical slice:
- No DB migration.
- No configuration change (`OrgChartOptions` schema unchanged).
- No new NuGet packages.
- No infrastructure or Azure Key Vault changes.
- No alerting reconfiguration is strictly required; an operator-facing note in the release PR description is sufficient (see Risks table).

Implementation can begin immediately.
```