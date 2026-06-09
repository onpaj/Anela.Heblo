## Module
UserManagement

## Finding
`GraphService.GetGroupMembersAsync` (`backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs:35-193`) is a 160-line method that performs four distinct operations in sequence, each with its own abstraction level:

1. **Cache read** (lines 39–45) — reads from `IMemoryCache`
2. **Token acquisition** (lines 64–85) — calls `ITokenAcquisition.GetAccessTokenForAppAsync`, measures timing, handles `MsalException` separately
3. **HTTP request/response** (lines 89–113) — constructs `HttpRequestMessage`, sends, checks status, logs timing
4. **JSON parsing + member filtering** (lines 119–164) — manually walks `JsonDocument`, discriminates users from nested groups via `@odata.type`, populates `List<UserDto>`
5. **Cache write** (lines 171–173)

The JSON parsing section alone is 45 lines. The token acquisition block is 25 lines. Neither can be understood, tested, or changed without reading the full surrounding context.

## Why it matters
- **Single Responsibility / readability**: the method operates at three different abstraction levels simultaneously (cache management, network I/O, domain filtering). A bug in member-type filtering (the `@odata.type` check, line 134) requires tracing through token, HTTP, and response code to reach it.
- **Testability**: `GraphServiceTests` uses `FakeHttpMessageHandler` to reach the parsing logic, but can't test the JSON parser in isolation.
- **Method length**: 160 lines exceeds the 50-line guideline mentioned in the review criteria and makes diffs harder to review.

## Suggested fix
Extract two private helpers with no change to behaviour:

```csharp
// Extract token acquisition
private async Task<string?> AcquireGraphTokenAsync(string groupId, CancellationToken ct) { ... }

// Extract JSON → List<UserDto> parsing
private static List<UserDto> ParseMembersFromJson(string json) { ... }
```

`GetGroupMembersAsync` then becomes a ~30-line orchestrator: cache-check → token → HTTP → parse → cache-write. Each extracted method is independently testable.

No interface changes, no behavioural changes — strictly internal refactoring.

---
_Filed by daily arch-review routine on 2026-06-05._