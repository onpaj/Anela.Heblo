# Design: Stop `GetAppRoleMembersAsync` from swallowing Graph failures

Backend-only correctness fix — no UI is added or changed by this task (frontend's non-handling of
`isError`/`success:false` on `EntraMemberSearch.tsx` is explicitly out of scope per the plan's Open Question 3 /
Dependencies section). UX/UI section is omitted.

## Component design

No new components. Four existing components change behavior/contract; one (`EntraAccessUserSourceAdapter`) is
unchanged but documented here because its behavior *implicitly* changes as a side effect.

### 1. `GraphService.GetAppRoleMembersAsync` (`Adapters/.../UserManagement/GraphService.cs:268-453`)

Responsibility stays the same: resolve the set of Graph users assigned (directly or via group) to a given app role.
What changes is failure signaling at exactly two of the method's seven early-return sites — the two that represent
"we could not reliably talk to Graph at all," as opposed to "Graph answered but the answer was empty/non-2xx."

| Site | Today | After | Rationale |
|---|---|---|---|
| Token acquisition (289-298), `catch (Exception ex)` | logs, returns `[]` | narrow to `catch (MsalException ex)`, `throw new GraphServiceAuthException(...)` | Mirrors `GetGroupMembersAsync:167-173` verbatim — same exception type, same message template shape (`"Failed to acquire Graph token for app role member lookup: {msalEx.Message}"`) |
| Outer wrapper (448-452), `catch (Exception ex)` | logs, returns `[]` | **removed** | Lets anything not already handled (malformed JSON via `JsonDocument.Parse`, `HttpRequestException` from `SendAsync`, an `OperationCanceledException`, or a non-`MsalException` auth failure) propagate to the caller, mirroring `GetGroupMembersAsync`'s outer `catch (Exception ex) { log; throw; }` (185-189) |

Unchanged (per plan FR-3, preserved intentionally — matches `GetGroupMembersAsync`'s own precedent of returning `[]`
on a non-2xx Graph *response*, lines 140-150):
- Missing `AzureAd:ClientId` config (283-287) → still returns `[]`
- Non-success service-principal lookup (308-312) → still returns `[]`
- Missing `spId` (315-319) or unresolved `appRoleId` (335-339) → still returns `[]`
- Non-success assignment-page fetch (352-356) → still returns `[]`
- Non-success `$batch` response (411-415) → still returns `[]`

Both `GetGroupMembersAsync` (called internally for group-expansion at line 383) and `GetAppRoleMembersAsync` share
one HTTP call layer; `GetAppRoleMembersAsync`'s own `catch` no longer intercepts a `GraphServiceAuthException`
bubbling up from that nested call — it was never designed to (the outer catch swallowed `Exception`, which
includes `GraphServiceAuthException`, but that's exactly the swallowing this task removes). After the fix, an
auth failure during group-expansion propagates the same as one during the method's own token acquisition.

No signature change: return type stays `Task<List<UserDto>>`; the contract is now expressed by what it can throw,
consistent with `GetGroupMembersAsync`.

### 2. `IGraphService` (`Application/.../UserManagement/Services/IGraphService.cs`)

Add the same `<exception cref="GraphServiceAuthException">` xmldoc block already present on `GetGroupMembersAsync`
(lines 7-9) to `GetAppRoleMembersAsync`'s declaration. This is documentation only — no members added, no signature
change. It makes the interface state the failure contract itself instead of requiring the reader to diff two
implementations, per the finding's stated goal ("callers must read two implementations instead of one interface
to understand the contract").

`SearchUsersAsync` keeps no exception doc — its swallow-to-`[]` behavior is untouched (out of scope, flagged as a
follow-up in the plan).

### 3. `GetEntraAccessUsersHandler` (`Application/.../Authorization/UseCases/GetEntraAccessUsers/GetEntraAccessUsersHandler.cs`)

Currently a 12-line pass-through with no try/catch. Gains the same two-clause catch pattern already established in
`GetGroupMembersHandler` (`UserManagement/UseCases/GetGroupMembers/GetGroupMembersHandler.cs:34-77`), scoped down to
the two exception types this call chain can actually produce (`GraphServiceAuthException` from FR-1;
`GraphServiceException` is Graph-SDK-level and not thrown by `GetAppRoleMembersAsync` today, but is included for
interface-level consistency with `GetGroupMembersHandler` and because `IGraphService` implementations are free to
throw it):

```
try
{
    var users = await _source.GetBaseMembersAsync(ct);
    return new GetEntraAccessUsersResponse { Users = ...ordered by DisplayName... };
}
catch (GraphServiceAuthException ex)
{
    // log
    return new GetEntraAccessUsersResponse { Success = false, ErrorCode = ErrorCodes.ConfigurationError, Users = [] };
}
catch (GraphServiceException ex)
{
    // log
    return new GetEntraAccessUsersResponse { Success = false, ErrorCode = ErrorCodes.ExternalServiceError, Users = [] };
}
```

Not adding a bare `catch (Exception ex)` fallback (unlike `GetGroupMembersHandler`'s fifth clause, `ErrorCode =
InternalServerError`): the plan's scope is limited to the two typed exceptions this fix introduces. Handler doesn't
currently take an `ILogger`, so one is added via constructor injection (mirrors `GetGroupMembersHandler`'s
constructor shape) — this is the one net-new dependency in the whole change.

### 4. `EntraAccessUserSourceAdapter` (`Application/.../UserManagement/Infrastructure/EntraAccessUserSourceAdapter.cs`)

No code change. Its `GetBaseMembersAsync` already has no try/catch and simply awaits `_graph.GetAppRoleMembersAsync`
— today that call can never throw (everything is swallowed), after the fix it can throw `GraphServiceAuthException`
(or propagate other unexpected exceptions per FR-2), and this method now naturally propagates that to its caller,
`GetEntraAccessUsersHandler`, which is where the new handling lives (component 3). Documented here so the design is
explicit that this pass-through class needs no edit — the exception simply flows through undecorated, which is the
correct behavior for an adapter with no domain-specific translation to add.

## Data schemas

### Exceptions (no new types — both already exist and are reused as-is)

- `GraphServiceAuthException(string message, Exception innerException) : Exception` — `Contracts/GraphServiceAuthException.cs`. Thrown from the token-acquisition catch (component 1) with message
  `"Failed to acquire Graph token for app role member lookup: {msalEx.Message}"`, inner = the caught `MsalException`.
- `GraphServiceException(string message, Exception innerException) : Exception` — `Contracts/GraphServiceException.cs`. Not thrown by `GetAppRoleMembersAsync` itself under this change, but caught in the handler (component 3) for interface-level parity with `GetGroupMembersHandler` and forward-compatibility with `IGraphService` implementations that do throw it.

### `IGraphService.GetAppRoleMembersAsync` contract (xmldoc-only change)

```csharp
/// <exception cref="GraphServiceAuthException">
/// Thrown when token acquisition fails (MSAL auth error).
/// </exception>
Task<List<UserDto>> GetAppRoleMembersAsync(string appRoleValue, CancellationToken cancellationToken = default);
```

### `GetEntraAccessUsersResponse` (shape unchanged — new value combinations reachable)

```csharp
public class GetEntraAccessUsersResponse : BaseResponse   // Success: bool, ErrorCode: ErrorCodes?, Params: Dictionary<string,string>?
{
    public List<EntraUserDto> Users { get; set; } = new();
}
```

No new fields. Behavior change is which combinations of `(Success, ErrorCode, Users)` the endpoint can now produce:

| Scenario | Before | After |
|---|---|---|
| Role has zero members (legitimate) | `Success: true, Users: []` | unchanged |
| Graph token acquisition fails (MSAL error) | `Success: true, Users: []` — indistinguishable from above | `Success: false, ErrorCode: ConfigurationError, Users: []` — HTTP status per `[HttpStatusCode]` on `ErrorCodes.ConfigurationError` (500) |
| Unexpected error (malformed JSON, transport exception, etc.) | `Success: true, Users: []` | exception propagates past the handler's two typed catches to the global exception-handler middleware (unhandled → framework default 500 `ProblemDetails`) |

`GET /api/admin/authorization/entra-users` is the only HTTP surface affected; no request-shape change, no new
endpoint.

### Test doubles (no new fixtures — reuse existing handler classes in `GraphServiceTests.cs`)

- `SequentialFakeHttpMessageHandler` (queued status/body pairs) — already used for
  `GetAppRoleMembersAsync_BatchLevelFailure_ReturnsEmptyListAndLogsError`; reused unmodified to keep asserting the
  FR-3-preserved `$batch`-non-2xx-returns-`[]` path.
- `ThrowingHttpMessageHandler` (throws a given `Exception` from `SendAsync`) — already used for
  `GetGroupMembersAsync_TransportThrows_Throws`; the same pattern (queue an `HttpRequestException` after the SP
  lookup succeeds) is the shape of the new test proving FR-2's outer-catch removal actually propagates, rather than
  just renaming the existing non-2xx test as the plan's Open Question 1 warns against.

No new mock/fixture types are needed for the `MsalException`-on-token-acquisition test either — `GraphServiceTests.cs`
already has `Mock<ITokenAcquisition>().Setup(...).ThrowsAsync(new MsalUiRequiredException(...))` wired for the
`GetGroupMembersAsync_TokenAcquisitionMsalException_Throws` test; the new `GetAppRoleMembersAsync` variant reuses the
identical setup shape against the same mock type.
