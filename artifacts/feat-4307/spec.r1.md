# Specification: Remove unnecessary async state machine from GetConfigurationHandler

## Summary
`GetConfigurationHandler.Handle` is marked `async` but performs no asynchronous work (`await`), causing the C# compiler to emit an unneeded `IAsyncStateMachine` allocation on every call (CS1998). This change removes the `async` modifier and returns the response via `Task.FromResult`, eliminating the unnecessary allocation while preserving the exact same public signature and behavior.

## Background
The `Configuration` module exposes a MediatR handler (`GetConfigurationHandler`) that returns static/derived application configuration (version, environment, mock-auth flag, timestamp) built synchronously from `IConfiguration` and in-memory values. Because the method body contains no `await`, the `async` keyword provides no benefit — it only causes the compiler to generate a state machine wrapper around otherwise synchronous work. This endpoint is called on every frontend app boot (`staleTime: Infinity` cached query), so the overhead is incurred once per session, not per request. This was flagged by the automated `arch-review` routine (see `brief.md`) as a low-risk code-quality cleanup, not a bug — nothing observable changes for callers.

## Functional Requirements

### FR-1: Remove unnecessary `async`/`await` state machine from `GetConfigurationHandler.Handle`
Change the method signature from `public async Task<GetConfigurationResponse> Handle(...)` to `public Task<GetConfigurationResponse> Handle(...)`, and change the final `return response;` to `return Task.FromResult(response);`. All other logic (logging calls, `BuildApplicationConfiguration()`, response construction) stays exactly as-is — no `async`/`await` remain in the method body.

**Acceptance criteria:**
- The `Handle` method in `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs` no longer has the `async` modifier.
- The method returns `Task.FromResult(response)` instead of a bare `response`.
- `MediatR`'s `IRequestHandler<GetConfigurationRequest, GetConfigurationResponse>` contract is still satisfied (method signature `Task<GetConfigurationResponse> Handle(GetConfigurationRequest request, CancellationToken cancellationToken)` unchanged from the caller's perspective).
- `dotnet build` produces no CS1998 warning for this file, and no new warnings are introduced.
- Existing unit/handler tests that `await handler.Handle(...)` continue to pass unmodified (per the brief, no test changes are required).
- No other file changes — this is a single-method, single-file edit.

## Non-Functional Requirements

### NFR-1: Performance
Eliminates one `IAsyncStateMachine` allocation per `GetConfiguration` request. No measurable/benchmarked target is required — this is a micro-optimization and code-clarity fix, not a performance-critical path.

### NFR-2: Security
None. No change to authentication, authorization, data exposure, or input handling — `GetConfigurationResponse` fields and their values are unchanged.

## Data Model
No data model changes. `GetConfigurationResponse` (an existing DTO class — not a record, per project convention) and `GetConfigurationRequest` are unchanged.

## API / Interface Design
No change to the public API surface: the MediatR request/response contract (`GetConfigurationRequest` → `GetConfigurationResponse`) and the controller/endpoint that dispatches it are unaffected. This is purely an internal implementation detail of the handler.

## Dependencies
None beyond what the handler already depends on (`IConfiguration`, `ILogger`, existing `BuildApplicationConfiguration()` helper). No new packages, no new project references.

## Out of Scope
- Any other handler in the codebase with the same `async`-without-`await` pattern (this issue is scoped to `GetConfigurationHandler` only, per the brief's file/line reference).
- Any change to `GetConfigurationResponse`, `GetConfigurationRequest`, `BuildApplicationConfiguration()`, or the controller that invokes this handler.
- Any test changes — the brief explicitly states none are required.
- Any frontend change (the `staleTime: Infinity` React Query hook is mentioned only as context for why the allocation is incurred once per session).

## Open Questions

None.

## Status: COMPLETE
