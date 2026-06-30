## Module
Article

## Finding
`BackfillArticleRequestedByHandler` directly catches `MsalException` (from `Microsoft.Identity.Client`) and `Microsoft.Graph.Models.ODataErrors.ODataError` (from the Graph SDK) inside the Application layer:

```
backend/src/Anela.Heblo.Application/Features/Article/Admin/BackfillArticleRequestedByHandler.cs
  line 6:   using Microsoft.Identity.Client;
  line 45:  catch (MsalException ex) { … ErrorCodes.ConfigurationError … }
  line 49:  catch (Microsoft.Graph.Models.ODataErrors.ODataError ex) { … ErrorCodes.ExternalServiceError … }
```

The Application project's `Anela.Heblo.Application.csproj` carries a direct `<PackageReference>` on `Microsoft.Graph` (5.92.0) and `Microsoft.Identity.Web` (3.14.1) to support this.

`IArticleUserResolver.ResolveByGroupAsync` (the contract in `Contracts/IArticleUserResolver.cs`) has no documented exception contract, so MSAL and OData exceptions escape from the adapter implementation and force the handler to know about them.

## Why it matters
The Application layer must not depend on infrastructure packages (Clean Architecture dependency rule). By catching `MsalException` and `ODataError`, the handler takes on knowledge of which SDK powers `IArticleUserResolver`. If the adapter is ever swapped (different identity provider, different Graph SDK version) the handler must change too — defeating the abstraction. It also makes `BackfillArticleRequestedByHandler` untestable without MSAL/Graph stubs.

## Suggested fix
Translate infrastructure exceptions inside the `IArticleUserResolver` adapter implementation (which lives in the UserManagement or Adapters layer). Define a small set of domain exceptions in the Application/Domain layer — e.g. `ExternalAuthServiceException`, `ExternalServiceUnavailableException` — or reuse the existing `ErrorCodes` result pattern by returning a typed `Result<IReadOnlyList<ArticleUserMatch>>` from `ResolveByGroupAsync`. The handler then handles only domain outcomes; `MsalException` and `ODataError` never cross the layer boundary, and `Microsoft.Identity.Client`/`Microsoft.Graph` references can be removed from the Application project.

---
_Filed by daily arch-review routine on 2026-06-22._
