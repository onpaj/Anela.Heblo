### task: decouple-handler-and-add-arch-test

#### Goal

Remove the SDK type references from `GetGroupMembersHandler` (it should catch only `GraphServiceAuthException` and `GraphServiceException`) and add a `[Fact]` in `ModuleBoundariesTests` that enforces the Application layer has no `Microsoft.Identity.Client` or `Microsoft.Graph.Models.ODataErrors` exception catches, with an allowlist covering pre-existing violations.

#### Context: task 1 already done

By the time this task runs, `GraphServiceAuthException` and `GraphServiceException` are already defined in `backend/src/Anela.Heblo.Application/Features/UserManagement/Contracts/` and `GraphService.GetGroupMembersAsync` already rethrows using those types. This task only modifies the handler and the architecture test.

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

   The `catch (UnauthorizedAccessException ...)` and `catch (Exception ...)` blocks are unchanged. The `ErrorCode` mapping is preserved: `ConfigurationError` for auth, `ExternalServiceError` for OData.

2. In `ModuleBoundariesTests.cs`, add a new `[Fact]` after `Application_types_should_not_reference_AspNetCore_namespaces`. First read the existing file to understand the `EnumerateReferencedTypes` and `IsForbidden` helpers and the allowlist pattern used by other tests.

   Add a private static allowlist field near the top of the class (with the other allowlist fields):

   ```csharp
   // Allowlist for Application -> SDK exception types (Microsoft.Identity.Client / Microsoft.Graph).
   // GraphArticleUserResolver wraps MsalException legitimately. Pre-existing violations in adapter
   // services are in different assemblies and won't be detected by reflection anyway.
   private static readonly HashSet<string> SdkExceptionAllowlist = new(StringComparer.Ordinal)
   {
       "Anela.Heblo.Application.Features.UserManagement.Infrastructure.GraphArticleUserResolver",
   };
   ```

   Then add the fact:

   ```csharp
   [Fact]
   public void Application_types_should_not_catch_SDK_exception_types_directly()
   {
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

   **IMPORTANT**: Before finalising the allowlist, run `dotnet test --filter Application_types_should_not_catch_SDK_exception_types_directly` from inside the test project directory to see if any violations surface that need to be added to the allowlist. If the test fails with violations for `GraphArticleUserResolver` or others, add them. The test must be green before committing.

#### Acceptance Criteria

- `GetGroupMembersHandler.cs` has no `using Microsoft.Identity.Client;` directive.
- `GetGroupMembersHandler.cs` has no `catch (MsalException ...)` or `catch (Microsoft.Graph.Models.ODataErrors.ODataError ...)` blocks.
- `GetGroupMembersHandler.cs` catches `GraphServiceAuthException` (mapped to `ErrorCodes.ConfigurationError`) and `GraphServiceException` (mapped to `ErrorCodes.ExternalServiceError`).
- `ModuleBoundariesTests.cs` contains a `[Fact]` named `Application_types_should_not_catch_SDK_exception_types_directly`.
- `dotnet build` passes for both Application and Tests projects.
- `dotnet test --filter Application_types_should_not_catch_SDK_exception_types_directly` passes (green).
- `dotnet test --filter "FullyQualifiedName~ModuleBoundariesTests"` passes (all existing boundary rules still green).
