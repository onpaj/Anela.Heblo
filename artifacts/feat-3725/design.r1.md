# Design: Remove dead `IGraphService.SearchUsersAsync` method

## Component Design
This change removes one unused method from an Application-layer port and both of its Adapter-layer implementations, plus the test file dedicated to that method. No new components are introduced and no existing component's responsibilities change beyond shrinking.

- **`IGraphService`** (`backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs`) — Application-layer port used by the `UserManagement` vertical slice to talk to Microsoft Graph. Responsibility narrows from three operations (`GetGroupMembersAsync`, `SearchUsersAsync`, `GetAppRoleMembersAsync`) to two (`GetGroupMembersAsync`, `GetAppRoleMembersAsync`). The `SearchUsersAsync` signature — `Task<List<UserDto>> SearchUsersAsync(string query, CancellationToken cancellationToken = default)` — is deleted outright; it has no caller anywhere in Application, API, Domain, or the frontend.
- **`GraphService`** (`backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs`) — production adapter implementing `IGraphService` against the live Microsoft Graph API. Its `SearchUsersAsync` method body and the `SearchResultLimit` constant (which existed only to parameterize that method's `$top` query parameter) are removed. `GraphBatchSize`, `AcquireGraphTokenAsync`, `ParseMembersFromJson`, `GetGroupMembersAsync`, and `GetAppRoleMembersAsync` are untouched — `GetAppRoleMembersAsync` depends on `GraphBatchSize` and `GetGroupMembersAsync`, none of which reference the deleted code.
- **`MockGraphService`** (`backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/MockGraphService.cs`) — test/dev-mode adapter implementing `IGraphService`. Its no-op `SearchUsersAsync` stub is removed; `GetGroupMembersAsync` and `GetAppRoleMembersAsync` stubs are unchanged.
- **`GraphServiceSearchTests`** (`backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceSearchTests.cs`) — unit test file whose sole purpose is exercising `SearchUsersAsync`. Deleted in its entirety (5 test methods), since nothing else in the file has independent value once the method it tests is gone.

DI registration selecting `GraphService` vs. `MockGraphService` at startup is unaffected — both adapters remain complete, valid implementations of the narrowed `IGraphService` interface after the change. The shared test helper `FakeHttpMessageHandler.cs` is preserved, as it is also used by `GraphServiceTests.cs` and `OutlookCalendarSyncServiceTests.cs`.

## Data Schemas
N/A — no data schema changes, this is a pure code deletion.
