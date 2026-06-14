# Specification: Refactor GraphService.GetGroupMembersAsync into Focused Helper Methods

## Summary
Extract two private helper methods from the 160-line `GraphService.GetGroupMembersAsync` to separate token acquisition and Microsoft Graph JSON response parsing from the orchestration flow. This is a strictly internal, behaviour-preserving refactor that improves readability, testability, and adherence to the Single Responsibility Principle without altering the public interface or runtime behaviour.

## Background
`GraphService.GetGroupMembersAsync` currently mixes four abstraction levels in one method: memory cache access, MSAL token acquisition with timing/exception handling, raw HTTP request/response handling, manual `JsonDocument` walking with `@odata.type` discrimination, and cache write-back. At 160 lines, the method exceeds the team's 50-line guideline, and the JSON parsing logic (45 lines) cannot be unit-tested without the `FakeHttpMessageHandler` plumbing used in `GraphServiceTests`. A bug in member-type filtering (e.g. the `@odata.type` check at line 134) requires reading through token and HTTP code to reach the relevant branch, which slows reviews and makes safe modification harder. The daily arch-review routine flagged this on 2026-06-05.

## Functional Requirements

### FR-1: Extract `AcquireGraphTokenAsync` helper
Introduce a private instance method that encapsulates Microsoft Graph token acquisition for the application identity.

**Signature (assumed):**
```csharp
private async Task<string?> AcquireGraphTokenAsync(string groupId, CancellationToken cancellationToken)
```

**Behaviour:**
- Calls `ITokenAcquisition.GetAccessTokenForAppAsync` with the same scope(s) currently used at lines 64–85.
- Measures token acquisition duration with the same `Stopwatch`/logging pattern currently in place.
- Catches `MsalException` and returns `null` (or whatever sentinel the current code uses) with identical logging output and log levels.
- Propagates `OperationCanceledException` unchanged when cancellation is requested.
- Logs the same messages at the same severity levels with the same structured-logging property names as today (including `groupId` where present).

**Acceptance criteria:**
- All existing `GraphServiceTests` scenarios that cover token success, MSAL failure, and cancellation pass without modification.
- Method body of `GetGroupMembersAsync` no longer contains a direct call to `ITokenAcquisition.GetAccessTokenForAppAsync`.
- Log output (message templates, levels, structured properties) for a successful token acquisition and for an `MsalException` is byte-for-byte equivalent to current output for identical inputs.

### FR-2: Extract `ParseMembersFromJson` helper
Introduce a private static method that converts the raw Microsoft Graph JSON response body into the `List<UserDto>` currently produced inline at lines 119–164.

**Signature (assumed):**
```csharp
private static List<UserDto> ParseMembersFromJson(string json)
```

**Behaviour:**
- Parses the input string with `JsonDocument.Parse` and walks the `value` array exactly as the current implementation does.
- Discriminates users from nested groups using the `@odata.type` property (current check at line 134) — only entries whose type indicates a user are included.
- Maps each user entry to a `UserDto` using the same property names and null-handling as the current code.
- Returns an empty `List<UserDto>` (never `null`) when the `value` array is missing or empty.
- Disposes the `JsonDocument` deterministically (e.g. `using`).
- Does not throw on entries whose optional fields are missing — falls back to the same defaults the current code uses.

**Acceptance criteria:**
- New unit tests can call `ParseMembersFromJson` directly with representative JSON fixtures without involving HTTP or cache plumbing.
- For any JSON body that the current `GraphServiceTests` `FakeHttpMessageHandler` returns, the new helper produces a list whose contents are equal (by sequence of `UserDto` field values) to the list produced by the current inline parser.
- The helper handles a JSON body containing a mix of `#microsoft.graph.user` and `#microsoft.graph.group` entries, returning only users — verified by a new dedicated test.
- The helper handles JSON with no `value` field by returning an empty list (no exception).

### FR-3: Rewrite `GetGroupMembersAsync` as an orchestrator
After extraction, `GetGroupMembersAsync` reads as a sequential pipeline: cache read → token acquisition → HTTP call → response parsing → cache write.

**Acceptance criteria:**
- Method length is reduced from ~160 lines to ≤50 lines (target ~30 lines).
- The method retains its existing signature, return type, parameters, and `CancellationToken` propagation.
- All existing `GraphServiceTests` pass without modification.
- The public surface of `GraphService` is unchanged (no new public/internal members added or removed).
- The HTTP request construction (URI, headers, method) and response status-code handling remain inline in the orchestrator — they are not extracted in this scope (see Out of Scope).

### FR-4: Preserve caching semantics
The `IMemoryCache` read (lines 39–45) and write (lines 171–173) remain in `GetGroupMembersAsync`.

**Acceptance criteria:**
- Cache key, value type, and expiration policy are identical to the current implementation.
- Cache read on hit short-circuits before token acquisition (no token request, no HTTP call) — verified by an existing or new test.
- Cache write occurs only after a successful parse, as today.

## Non-Functional Requirements

### NFR-1: Performance
No measurable regression. Token acquisition, HTTP I/O, and JSON parsing are unchanged in mechanism; only method boundaries shift. The `ParseMembersFromJson` helper must not introduce additional allocations beyond what the current inline code produces (no intermediate `List` copies, no LINQ where the current code uses index loops).

**Acceptance criteria:**
- Benchmark or manual profiling of a representative response (≥100 members) shows ≤5% deviation in execution time and allocated bytes versus the pre-refactor implementation.

### NFR-2: Security
No change to security posture. Token scopes, header construction, and HTTPS endpoint remain identical. The extracted helpers must not log token values or full response bodies at any log level.

**Acceptance criteria:**
- A grep of the diff confirms no new logging of `accessToken`, `Authorization` header values, or raw response bodies.

### NFR-3: Testability
JSON parsing becomes testable without HTTP plumbing.

**Acceptance criteria:**
- New `ParseMembersFromJsonTests` class (or equivalent test fixture) exercises the parser directly with at least: (a) all-users response, (b) mixed users/groups, (c) empty `value`, (d) missing optional fields.
- Test coverage of the parsing logic measured by line coverage is ≥90%.

### NFR-4: Maintainability
Each method has a single responsibility expressible in one short sentence. No method in the refactored file exceeds 50 lines.

## Data Model
No data-model changes. The existing `UserDto` (consumer-facing DTO returned from `GetGroupMembersAsync`) remains the only entity touched, and its shape is unchanged.

## API / Interface Design
No external API changes:
- `IGraphService.GetGroupMembersAsync` signature, semantics, and return value are unchanged.
- No new public, internal, or protected members are added to `GraphService`.
- The two new helpers are `private` (instance for `AcquireGraphTokenAsync`, static for `ParseMembersFromJson`).

## Dependencies
No new dependencies. The refactor uses only types already referenced by `GraphService`:
- `Microsoft.Identity.Web.ITokenAcquisition`
- `Microsoft.Identity.Client.MsalException`
- `System.Text.Json.JsonDocument`
- `Microsoft.Extensions.Caching.Memory.IMemoryCache`
- `Microsoft.Extensions.Logging.ILogger<GraphService>`
- The existing `UserDto`

## Out of Scope
- **HTTP request/response extraction.** The 25-line HTTP block (lines 89–113) is not extracted in this iteration — the brief specifies exactly two extractions. A follow-up may extract an `SendGraphRequestAsync` helper if subsequent review identifies further benefit.
- **Replacing manual `JsonDocument` walking with `JsonSerializer` / source-generated deserializers.** The parsing mechanism stays as-is to keep the diff strictly behaviour-preserving.
- **Changes to `IGraphService` or any caller of `GetGroupMembersAsync`.**
- **Cache key strategy or expiration tuning.**
- **Adding pagination support for groups exceeding the Graph default page size** (current behaviour, whatever it is, is preserved).
- **Changing log levels, message templates, or structured property names** beyond what is required to move code into the new helpers.
- **Adding new metrics, OpenTelemetry spans, or activity sources.**

## Open Questions
None.

## Status: COMPLETE