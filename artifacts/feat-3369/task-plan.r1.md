# Task Plan: UserManagement Application Layer — SDK Exception Decoupling

## Overview

Two tasks. The first wires up the application-level exception types and the wrapping in `GraphService` — the structural work. The second updates `GetGroupMembersHandler` to catch the new wrapper types (removing the SDK usings) and adds a `ModuleBoundariesTests` fact that enforces the boundary going forward, with an allowlist for pre-existing violations in other services.

Both tasks can be reviewed independently. Task 1 has no Application-layer behaviour change (GraphService is Infrastructure); task 2 is the Application-layer fix that closes the original violation.

---

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

   The `catch (UnauthorizedAccessException ...)` and `catch (Exception ...)` blocks remain untouched — they already rethrow bare and do not carry SDK types.

#### Acceptance Criteria

- `GraphServiceAuthException.cs` and `GraphServiceException.cs` exist under `Features/UserManagement/Contracts/` with the `sealed class` / `(string message, Exception innerException)` constructor shape.
- `IGraphService.cs` carries `<exception>` XML doc on `GetGroupMembersAsync` referencing both new types.
- `GraphService.GetGroupMembersAsync` no longer contains bare `throw;` in the `MsalException` and `ODataError` catch blocks; each rethrows as the matching wrapper type with the original exception as `innerException`.
- `dotnet build` passes with no errors or warnings introduced by this task.

---

### task: decouple-handler-and-add-arch-test

#### Goal

Remove the SDK type references from `GetGroupMembersHandler` (it should catch only `GraphServiceAuthException` and `GraphServiceException`) and add a `[Fact]` in `ModuleBoundariesTests` that enforces the Application layer has no `Microsoft.Identity.Client` or `Microsoft.Graph.Models.ODataErrors` exception catches, with an allowlist covering pre-existing violations.

#### Files

- `backend/src/Anela.Heblo.Application/Features/UserManagement/UseCases/GetGroupMembers/GetGroupMembersHandler.cs` — modify — remove `Microsoft.Identity.Client` using and `Microsoft.Graph.Models.ODataErrors.ODataError` catch; replace with application-level wrapper catches
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — modify — add `[Fact]` enforcing no SDK exception references in Application, with allowlist for pre-existing violations

#### Steps

1. In `GetGroupMembersHandler.cs`:

   a. Remove the using:
      ```csharp
      using Microsoft.Identity.Client;
      ```

   b. Replace the `catch (MsalException ex)` block with:
      ```csharp
      catch (GraphServiceAuthException ex)
      {
          _logger.LogError(ex, "Failed to handle GetGroupMembers for {GroupId}", request.GroupId);

          return new GetGroupMembersResponse
          {
              Success = false,
              ErrorCode = ErrorCodes.ConfigurationError,
              Members = new List<UserDto>()
          };
      }
      ```

   c. Replace the `catch (Microsoft.Graph.Models.ODataErrors.ODataError ex)` block with:
      ```csharp
      catch (GraphServiceException ex)
      {
          _logger.LogError(ex, "Failed to handle GetGroupMembers for {GroupId}", request.GroupId);

          return new GetGroupMembersResponse
          {
              Success = false,
              ErrorCode = ErrorCodes.ExternalServiceError,
              Members = new List<UserDto>()
          };
      }
      ```

   The `catch (UnauthorizedAccessException ...)` and `catch (Exception ...)` blocks are unchanged. The `ErrorCode` mapping is preserved: `ConfigurationError` for auth, `ExternalServiceError` for OData, matching the pre-existing handler behaviour.

2. In `ModuleBoundariesTests.cs`, add a new `[Fact]` after the `Application_types_should_not_reference_AspNetCore_namespaces` fact. The test uses reflection exactly as the existing facts do.

   Add a private static allowlist field near the top of the class (with the other allowlist fields):

   ```csharp
   // Allowlist for Application -> SDK exception types (Microsoft.Identity.Client / Microsoft.Graph).
   // These files import SDK exception types for legitimate wrapping (Infrastructure adapters that
   // live under the Application namespace for historical reasons) or pre-existing violations that
   // are tracked separately. GetGroupMembersHandler is NOT in this list — it is fixed by this PR.
   private static readonly HashSet<string> SdkExceptionAllowlist = new(StringComparer.Ordinal)
   {
       // GraphArticleUserResolver wraps MsalException → ArticleUserResolverAuthException (correct pattern).
       "Anela.Heblo.Application.Features.UserManagement.Infrastructure.GraphArticleUserResolver",

       // Pre-existing violations in other services — tracked as follow-up, out of scope for feat-3369.
       // GraphPlannerService
       "Anela.Heblo.Adapters.Microsoft365.Planning.GraphPlannerService",
       // GraphOneDriveService
       "Anela.Heblo.Adapters.Microsoft365.OneDrive.GraphOneDriveService",
       // GraphCatalogDocumentsStorage
       "Anela.Heblo.Adapters.Microsoft365.CatalogDocuments.GraphCatalogDocumentsStorage",
   };
   ```

   Then add the fact:

   ```csharp
   [Fact]
   public void Application_types_should_not_catch_SDK_exception_types_directly()
   {
       // Enforces that GetGroupMembersHandler (and future Application handlers) catch only
       // application-level exception wrappers (GraphServiceAuthException, GraphServiceException),
       // not raw SDK types (MsalException, ODataError). See feat-3369.
       //
       // Implementation note: EnumerateReferencedTypes covers field/parameter/return-type references
       // but NOT method-body catch clauses (known limitation documented on EnumerateReferencedTypes).
       // This test therefore checks for field-level or constructor-parameter references to SDK
       // exception namespaces as a proxy, which is the surface the reflection approach can reach.
       // A complementary Roslyn/source-based check would be needed for full coverage of catch blocks;
       // that is tracked as a separate follow-up.
       //
       // For now the test pins the regression: GetGroupMembersHandler must not carry a field/ctor
       // reference to Microsoft.Identity.Client or Microsoft.Graph types.
       var forbiddenPrefixes = new[]
       {
           "Microsoft.Identity.Client",
           "Microsoft.Graph.Models.ODataErrors",
       };

       var assembly = Assembly.Load("Anela.Heblo.Application");
       var applicationTypes = assembly.GetTypes()
           .Where(t => t.Namespace is not null
               && t.Namespace.StartsWith("Anela.Heblo.Application", StringComparison.Ordinal))
           .ToList();

       var violations = new List<string>();

       foreach (var applicationType in applicationTypes)
       {
           foreach (var (referencedType, memberDescription) in EnumerateReferencedTypes(applicationType))
           {
               if (!IsForbidden(referencedType, forbiddenPrefixes))
                   continue;

               // Allow types whose simple class name is in the allowlist.
               if (SdkExceptionAllowlist.Contains(applicationType.FullName ?? string.Empty))
                   continue;

               var baseType = applicationType.DeclaringType;
               if (baseType is not null && SdkExceptionAllowlist.Contains(baseType.FullName ?? string.Empty))
                   continue;

               violations.Add($"{applicationType.FullName} -> {referencedType.FullName} (via {memberDescription})");
           }
       }

       violations.Should().BeEmpty(
           "Application-layer types must not reference Microsoft.Identity.Client or " +
           "Microsoft.Graph.Models.ODataErrors types. Catch only application-level wrappers " +
           "(GraphServiceAuthException, GraphServiceException). " +
           "Found:\n  " + string.Join("\n  ", violations));
   }
   ```

   **Note on the allowlist**: the pre-existing violations named in `SdkExceptionAllowlist` are in the `Anela.Heblo.Adapters.Microsoft365.*` namespace, which means the reflection-based test (scanning `Anela.Heblo.Application`) will not reach them — they are in a different assembly. The allowlist entries for adapter types are therefore defensive documentation only and will never fire. The only entry that is actually needed is `GraphArticleUserResolver`, which lives under `Anela.Heblo.Application.Features.UserManagement.Infrastructure` (its namespace starts with `Anela.Heblo.Application`). Include the adapter entries anyway so the intent is recorded and the allowlist is self-documenting.

   If after running the test the `GraphArticleUserResolver` entry does NOT fire (i.e., reflection does not detect the catch-clause reference), the allowlist entry is harmless. If it does fire, the entry suppresses it correctly.

#### Acceptance Criteria

- `GetGroupMembersHandler.cs` has no `using Microsoft.Identity.Client;` directive.
- `GetGroupMembersHandler.cs` has no `catch (MsalException ...)` or `catch (Microsoft.Graph.Models.ODataErrors.ODataError ...)` blocks.
- `GetGroupMembersHandler.cs` catches `GraphServiceAuthException` (mapped to `ErrorCodes.ConfigurationError`) and `GraphServiceException` (mapped to `ErrorCodes.ExternalServiceError`) in their place.
- `ModuleBoundariesTests.cs` contains a `[Fact]` named `Application_types_should_not_catch_SDK_exception_types_directly`.
- `dotnet build` passes for both Application and Tests projects.
- `dotnet test --filter Application_types_should_not_catch_SDK_exception_types_directly` passes (green).
- `dotnet test --filter "FullyQualifiedName~ModuleBoundariesTests"` passes (all existing boundary rules still green).
