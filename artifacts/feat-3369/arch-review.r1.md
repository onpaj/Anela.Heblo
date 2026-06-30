# Architecture Review: UserManagement Application Layer — SDK Exception Decoupling

## Skip Design: true

## Architectural Fit Assessment

The change is a pure internal refactor. The correct pattern already exists in this codebase and the spec correctly identifies it: `ArticleUserResolverAuthException` / `ArticleUserResolverServiceException` in `Application/Features/Article/Contracts/` are the reference implementation. The proposed design mirrors that pattern exactly for the UserManagement service boundary.

The integration points are narrow and well-understood:

- `IGraphService` in `Application/Features/UserManagement/Services/` — the boundary whose declared exception contract needs updating.
- `GraphService` in `Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/` — the only implementation; it catches raw SDK exceptions and must rethrow as the new wrapper types.
- `GetGroupMembersHandler` in `Application/Features/UserManagement/UseCases/GetGroupMembers/` — the only handler that currently catches raw SDK types and must be fixed.
- `ModuleBoundariesTests.cs` in `backend/test/Anela.Heblo.Tests/Architecture/` — the architecture gate that needs a new `[Fact]` to enforce the fix permanently.

There is one critical finding that the spec does not address: **FR-5 (remove `Microsoft.Graph` and `Microsoft.Identity.Web` from `Anela.Heblo.Application.csproj`) cannot be executed as written**. Three other classes in the Application project also import SDK types and will fail to build if the `PackageReference` entries are removed:

- `Application/Features/MeetingTasks/Services/GraphPlannerService.cs` — imports `Microsoft.Identity.Client` and `Microsoft.Identity.Web`, holds `ITokenAcquisition`.
- `Application/Features/KnowledgeBase/Services/GraphOneDriveService.cs` — imports `Microsoft.Identity.Web`, holds `ITokenAcquisition`.
- `Application/Features/CatalogDocuments/Services/GraphCatalogDocumentsStorage.cs` — imports `Microsoft.Identity.Client` and `Microsoft.Identity.Web`.

These are separate pre-existing violations. They are out of scope for this task, but they mean the `PackageReference` removal must be deferred to a follow-up that also addresses those files.

Additionally, `GraphArticleUserResolver` (in `Application/Features/UserManagement/Infrastructure/`) catches raw `MsalException` and `ODataError` directly. This is currently correct behaviour — it wraps them into Article-level types — but it means the Application project needs `Microsoft.Graph` and `Microsoft.Identity.Client` regardless of this task's changes. This is another reason FR-5 cannot be completed here.

The architecture test proposed in FR-6 is the right long-term enforcement mechanism and should be written to match the `EnumerateReferencedTypes` pattern already used by every other test in `ModuleBoundariesTests.cs`.

## Proposed Architecture

### Component Overview

```
Application layer (Anela.Heblo.Application)
├── Features/UserManagement/
│   ├── Contracts/
│   │   ├── UserDto.cs                          [existing]
│   │   ├── GraphServiceAuthException.cs         [NEW] wraps MsalException
│   │   └── GraphServiceException.cs             [NEW] wraps ODataError
│   ├── Services/
│   │   └── IGraphService.cs                    [update: add <exception> XML docs]
│   ├── UseCases/GetGroupMembers/
│   │   └── GetGroupMembersHandler.cs           [update: catch app-level types only]
│   └── Infrastructure/
│       └── GraphArticleUserResolver.cs          [no change — wrapping is correct]

Adapters layer (Anela.Heblo.Adapters.Microsoft365)
└── UserManagement/
    └── GraphService.cs                          [update: rethrow as app-level types]

Test layer (Anela.Heblo.Tests)
└── Architecture/
    └── ModuleBoundariesTests.cs                [update: add Application SDK namespace test]
```

The data flow for `GetGroupMembersAsync` after the change:

```
GetGroupMembersHandler
  │  calls IGraphService.GetGroupMembersAsync
  │
GraphService (Adapters layer)
  │  catches MsalException
  │    → throws GraphServiceAuthException (Application Contracts type)
  │  catches ODataError
  │    → throws GraphServiceException (Application Contracts type)
  │
GetGroupMembersHandler
  │  catches GraphServiceAuthException → maps to ConfigurationError
  └  catches GraphServiceException     → maps to ExternalServiceError
```

### Key Design Decisions

#### Decision 1: Exception type placement — `UserManagement/Contracts/` not `UserManagement/Services/`
**Options considered:** Place new exception types alongside `IGraphService` in `Services/`, or in `Contracts/` alongside `UserDto`.
**Chosen approach:** `Contracts/`. The `ArticleUserResolverAuthException` / `ArticleUserResolverServiceException` precedent places exception wrappers in `Contracts/`. `Services/` is for service interfaces; `Contracts/` is for types that form the service boundary contract (DTOs, exception types, value types consumed/produced by the service). Consistency matters more than proximity to the interface.
**Rationale:** Every existing architecture test in this codebase verifies behaviour by namespace prefix. Placing the new exceptions in `Contracts/` means any future rule that inspects `UserManagement/Contracts/` will naturally include them.

#### Decision 2: Exception class shape — sealed class, not record
**Options considered:** `sealed class`, `record`.
**Chosen approach:** `sealed class`. The project rule is explicit: DTOs are classes, never records. Exception types are not DTOs but they hold construction state and participate in catch semantics. Using a `sealed class` matching the `ArticleUserResolver*Exception` shape avoids any ambiguity.
**Rationale:** Records generate equality members and deconstruct operators that are meaningless for exception types. The reference implementations use `sealed class`; deviate only with a reason.

#### Decision 3: FR-5 scope reduction
**Options considered:** (a) Remove both `PackageReference` entries as specified; (b) leave the removal out of scope and note it as a follow-up; (c) partial removal (e.g. remove `Microsoft.Graph` only if `Microsoft.Identity.Web` is needed elsewhere).
**Chosen approach:** Scope FR-5 out entirely. Do not touch `Anela.Heblo.Application.csproj` in this task.
**Rationale:** `GraphPlannerService`, `GraphOneDriveService`, and `GraphCatalogDocumentsStorage` all import `Microsoft.Identity.Client` / `Microsoft.Identity.Web` directly, and `GraphArticleUserResolver` catches raw SDK exceptions. Removing the `PackageReference` entries would break the build. The primary goal of this task — decoupling `GetGroupMembersHandler` from SDK exception types — is fully achieved by FR-1 through FR-4 and FR-6. The `PackageReference` removal belongs in a separate task that also addresses the three other violating files.

#### Decision 4: Architecture test approach — new `[Fact]` using `EnumerateReferencedTypes`
**Options considered:** Extend the existing `Application_types_should_not_reference_AspNetCore_namespaces` test to also check SDK namespaces; add a new `[Fact]` following the same pattern.
**Chosen approach:** New standalone `[Fact]`. The AspNetCore test uses a slightly different structure (no allowlist, single forbidden prefix). The new test should be its own method for clarity, and it should use an allowlist from the start to accommodate the pre-existing SDK references in `GraphPlannerService`, `GraphOneDriveService`, `GraphCatalogDocumentsStorage`, and `GraphArticleUserResolver` — otherwise the test will fail on the first run. The test enforces the specific prohibition for `GetGroupMembersHandler` while honestly acknowledging pre-existing violations.
**Rationale:** A failing test that cannot be fixed without scope creep is worthless. The allowlist approach is how every other boundary test in this file handles pre-existing debt. The test must be green after FR-1–FR-4 are applied.

## Implementation Guidance

### Directory / Module Structure

Create two new files:

```
backend/src/Anela.Heblo.Application/Features/UserManagement/Contracts/GraphServiceAuthException.cs
backend/src/Anela.Heblo.Application/Features/UserManagement/Contracts/GraphServiceException.cs
```

Update three existing files:

```
backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs
backend/src/Anela.Heblo.Application/Features/UserManagement/UseCases/GetGroupMembers/GetGroupMembersHandler.cs
backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs
backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
```

Do not touch `Anela.Heblo.Application.csproj`.

### Interfaces and Contracts

**GraphServiceAuthException** — namespace `Anela.Heblo.Application.Features.UserManagement.Contracts`:

```csharp
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

**GraphServiceException** — namespace `Anela.Heblo.Application.Features.UserManagement.Contracts`:

```csharp
/// <summary>
/// Thrown by <see cref="IGraphService"/> implementations when the remote
/// Microsoft Graph service returns an error response (e.g. an OData error).
/// Wraps infrastructure-specific service exceptions so that Application-layer
/// consumers remain decoupled from SDK packages.
/// </summary>
public sealed class GraphServiceException : Exception
{
    public GraphServiceException(string message, Exception innerException)
        : base(message, innerException) { }
}
```

**IGraphService XML doc additions** — add to `GetGroupMembersAsync` only (scope limited per spec):

```csharp
/// <exception cref="GraphServiceAuthException">
/// Thrown when token acquisition or authentication for the identity provider fails.
/// </exception>
/// <exception cref="GraphServiceException">
/// Thrown when the Microsoft Graph service returns an error response.
/// </exception>
```

**Architecture test allowlist** — the new `[Fact]` in `ModuleBoundariesTests.cs` must include an allowlist with entries for the three pre-existing violating files. Without this the test will be red immediately and useless:

```csharp
private static readonly HashSet<string> ApplicationSdkNamespaceAllowlist = new(StringComparer.Ordinal)
{
    // Pre-existing: GraphPlannerService (MeetingTasks) holds ITokenAcquisition and MsalException.
    // Decoupling is out of scope — tracked as follow-up.
    // Add specific type-level entries here after running the test to find exact violations.

    // Pre-existing: GraphOneDriveService (KnowledgeBase) holds ITokenAcquisition.
    // Pre-existing: GraphCatalogDocumentsStorage (CatalogDocuments) holds ITokenAcquisition.
    // Pre-existing: GraphArticleUserResolver (UserManagement/Infrastructure) catches raw exceptions.
};
```

Run the test once after FR-1–FR-4 to discover the exact `"FullTypeName -> SDK.Type"` strings that must go into the allowlist. The test infrastructure's `EnumerateReferencedTypes` inspects fields, constructor parameters, method signatures — not catch blocks — so `GraphArticleUserResolver`'s catch clauses may or may not surface depending on how the compiler emits them. Populate the allowlist from the actual test output.

### Data Flow

**Happy path** — unchanged: `GetGroupMembersHandler` calls `IGraphService.GetGroupMembersAsync`, receives `List<UserDto>`, returns `GetGroupMembersResponse { Success = true }`.

**Token acquisition failure:**
1. `GraphService.AcquireGraphTokenAsync` throws `MsalException`.
2. `GraphService.GetGroupMembersAsync` catch block logs, throws `GraphServiceAuthException(message, msalEx)`.
3. `GetGroupMembersHandler` catch block catches `GraphServiceAuthException`, logs, returns `GetGroupMembersResponse { Success = false, ErrorCode = ConfigurationError }`.

**Graph OData error:**
1. `GraphService.GetGroupMembersAsync` receives a non-success HTTP response — note: the current implementation returns an empty list for non-success HTTP responses (line 149), not an exception. The `ODataError` catch block exists for exceptions thrown by the Graph SDK client, not HTTP status codes. This path is preserved unchanged.
2. If an `ODataError` is thrown (e.g. by SDK internals), catch block logs, throws `GraphServiceException(message, odataEx)`.
3. `GetGroupMembersHandler` catch block catches `GraphServiceException`, logs, returns `GetGroupMembersResponse { Success = false, ErrorCode = ExternalServiceError }`.

**Important implementation note:** `GraphService.GetGroupMembersAsync` does not use the Graph SDK's fluent client for its HTTP calls — it uses a raw `HttpClient` with a Bearer token header. The existing `catch (ODataError)` block in `GraphService` will therefore only fire if the Graph SDK is invoked elsewhere in a code path triggered by `GetGroupMembersAsync` (it currently is not). The catch block should still be updated per spec for correctness and future-proofing, but implementers should be aware that in practice the `MsalException` path is the only one likely to fire.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| FR-5 csproj removal breaks build due to `GraphPlannerService`, `GraphOneDriveService`, `GraphCatalogDocumentsStorage` | High | Remove FR-5 from scope entirely. Document the three violating files as a separate follow-up. |
| Architecture test (FR-6) fails on first run due to pre-existing SDK references in Application project | High | Populate `ApplicationSdkNamespaceAllowlist` from actual test output before committing. The allowlist pattern is established by every other test in this file. |
| `GraphArticleUserResolver` in `UserManagement/Infrastructure/` also catches raw SDK exceptions — test may flag it | Medium | `GraphArticleUserResolver` is already correct (it wraps before surfacing). It will appear in the allowlist. This is expected and documented. |
| `ODataError` catch in updated `GraphService` is unreachable under current HTTP-based implementation | Low | Keep the catch block as a defensive guard; document the observation in a code comment. No functional risk. |
| `EnumerateReferencedTypes` does not inspect method bodies — catch clause types may not surface in the test | Low | Verify after FR-1–FR-4 by running the test. If the handler's old SDK catch types are not detected via reflection, confirm via code review that the using directives and type references are gone. The build failure from the removed `using` directive is the primary safety net. |

## Specification Amendments

**FR-5 must be removed from this task's scope.** The spec states that no other file under the Application project imports types from the SDK packages before removing them — this assumption is false. The following files all import SDK types and are not addressed by this spec:

- `Features/MeetingTasks/Services/GraphPlannerService.cs` — `Microsoft.Identity.Client`, `Microsoft.Identity.Web`
- `Features/KnowledgeBase/Services/GraphOneDriveService.cs` — `Microsoft.Identity.Web`
- `Features/CatalogDocuments/Services/GraphCatalogDocumentsStorage.cs` — `Microsoft.Identity.Client`, `Microsoft.Identity.Web`
- `Features/UserManagement/Infrastructure/GraphArticleUserResolver.cs` — `Microsoft.Identity.Client`, `Microsoft.Graph.Models.ODataErrors` (legitimately, as it performs wrapping)

Removing the `PackageReference` entries now would break the build. FR-5 should be filed as a separate arch-debt task covering all four files together.

**FR-6 architecture test must include an allowlist.** The spec proposes a test with `forbidden prefixes ["Microsoft.Identity", "Microsoft.Graph"]` but does not account for the pre-existing violations listed above. Without an allowlist the test will fail from day one and be immediately skipped or deleted. The test must be written with `ApplicationSdkNamespaceAllowlist` populated from actual test output, following the established pattern in `ModuleBoundariesTests.cs`.

**No other amendments.** FR-1 through FR-4 are correct and complete as written.

## Prerequisites

Nothing external needs to exist before implementation starts. All required components (`IGraphService`, `GraphService`, `GetGroupMembersHandler`, `ModuleBoundariesTests.cs`) are already in place. The `ArticleUserResolverAuthException` / `ArticleUserResolverServiceException` files serve as the copy-template for FR-1.
