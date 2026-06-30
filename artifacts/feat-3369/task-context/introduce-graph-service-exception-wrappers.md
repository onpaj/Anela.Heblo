### task: introduce-graph-service-exception-wrappers

#### Goal

Define the two application-level exception classes for the UserManagement service boundary and update `GraphService.GetGroupMembersAsync` to catch SDK exceptions and rethrow as those wrappers, following the same pattern as `ArticleUserResolverAuthException` / `ArticleUserResolverServiceException`.

#### Files

- `backend/src/Anela.Heblo.Application/Features/UserManagement/Contracts/GraphServiceAuthException.cs` — create — application-level wrapper for MSAL auth failures
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Contracts/GraphServiceException.cs` — create — application-level wrapper for Graph OData errors
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs` — modify — add XML `<exception>` doc tags to `GetGroupMembersAsync`
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs` — modify — replace `throw;` in the `MsalException` and `ODataError` catch blocks with typed rethrows

#### Steps

1. Create `GraphServiceAuthException.cs`. Mirror `ArticleUserResolverAuthException` exactly (namespace, shape, XML doc); only the class name and XML doc text differ.

   ```csharp
   namespace Anela.Heblo.Application.Features.UserManagement.Contracts;

   /// <summary>
   /// Thrown by <see cref="IGraphService"/> implementations when token acquisition
   /// or authentication for the underlying identity provider fails.
   /// Wraps infrastructure-specific auth exceptions (e.g. MsalException) so that
   /// Application-layer consumers remain decoupled from SDK packages.
   /// </summary>
   public sealed class GraphServiceAuthException : Exception
   {
       public GraphServiceAuthException(string message, Exception innerException)
           : base(message, innerException) { }
   }
   ```

2. Create `GraphServiceException.cs`. Same shape, different XML doc.

   ```csharp
   namespace Anela.Heblo.Application.Features.UserManagement.Contracts;

   /// <summary>
   /// Thrown by <see cref="IGraphService"/> implementations when the remote
   /// directory service returns an error response (e.g. an OData error from Microsoft Graph).
   /// Wraps infrastructure-specific service exceptions so that Application-layer consumers
   /// remain decoupled from SDK packages.
   /// </summary>
   public sealed class GraphServiceException : Exception
   {
       public GraphServiceException(string message, Exception innerException)
           : base(message, innerException) { }
   }
   ```

3. In `IGraphService.cs`, add `<exception>` XML doc tags to `GetGroupMembersAsync` immediately above the method signature:

   ```csharp
   /// <exception cref="GraphServiceAuthException">
   /// Thrown when token acquisition fails (MSAL auth error).
   /// </exception>
   /// <exception cref="GraphServiceException">
   /// Thrown when Microsoft Graph returns an OData error response.
   /// </exception>
   Task<List<UserDto>> GetGroupMembersAsync(string groupId, CancellationToken cancellationToken = default);
   ```

4. In `GraphService.cs`, update `GetGroupMembersAsync`. Add a using for the new contracts at the top of the file:

   ```csharp
   using Anela.Heblo.Application.Features.UserManagement.Contracts;
   ```

   Replace the two bare `throw;` statements in the catch blocks:

   - `catch (MsalException msalEx)` block: replace `throw;` with:
     ```csharp
     throw new GraphServiceAuthException(
         $"Failed to acquire Graph API token for group {groupId}: {msalEx.Message}", msalEx);
     ```

   - `catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx)` block: replace `throw;` with:
     ```csharp
     throw new GraphServiceException(
         $"Microsoft Graph OData error fetching group members for group {groupId}: {odataEx.Error?.Code}", odataEx);
     ```

   The `catch (UnauthorizedAccessException ...)` and `catch (Exception ...)` blocks remain untouched.

#### Acceptance Criteria

- `GraphServiceAuthException.cs` and `GraphServiceException.cs` exist under `Features/UserManagement/Contracts/` with the `sealed class` / `(string message, Exception innerException)` constructor shape.
- `IGraphService.cs` carries `<exception>` XML doc on `GetGroupMembersAsync` referencing both new types.
- `GraphService.GetGroupMembersAsync` no longer contains bare `throw;` in the `MsalException` and `ODataError` catch blocks; each rethrows as the matching wrapper type with the original exception as `innerException`.
- `dotnet build` passes with no errors or warnings introduced by this task.
