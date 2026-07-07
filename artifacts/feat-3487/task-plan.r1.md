# Remove dead `IncludeDetailedBreakdown` flag from GetMarginReport Implementation Plan

**Goal:** Delete the unused `IncludeDetailedBreakdown` property from `GetMarginReportRequest` (never read by `GetMarginReportHandler`), remove its one test reference, and regenerate the OpenAPI-derived clients so the public contract matches actual behavior.
**Architecture:** No architectural change — subtractive edit inside the existing `Analytics/UseCases/GetMarginReport` vertical slice. No module boundary, DI, persistence, or controller changes.
**Tech Stack:** .NET 8 (C#, MediatR, FluentValidation, xUnit) backend; React/TypeScript frontend with NSwag-generated API client.

---

### task: remove-includedetailedbreakdown-property

**Context:** `GetMarginReportRequest.IncludeDetailedBreakdown` (backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportRequest.cs:11) is a public query-string boolean that `GetMarginReportHandler` never reads. Repo-wide search (spec + arch-review) confirms zero callers anywhere in backend or frontend, generated or hand-written. This task removes the dead property from the DTO and its only test reference, then verifies the backend still builds and all tests pass.

**Step 1 — Remove the property from the request DTO**

File: `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportRequest.cs`

Current content:
```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Analytics.UseCases.GetMarginReport;

public class GetMarginReportRequest : IRequest<GetMarginReportResponse>
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string? ProductFilter { get; set; }
    public string? CategoryFilter { get; set; }
    public bool IncludeDetailedBreakdown { get; set; } = false;
    public int MaxProducts { get; set; } = 50;
}
```

Replace with:
```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Analytics.UseCases.GetMarginReport;

public class GetMarginReportRequest : IRequest<GetMarginReportResponse>
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string? ProductFilter { get; set; }
    public string? CategoryFilter { get; set; }
    public int MaxProducts { get; set; } = 50;
}
```

(i.e. delete the single line `public bool IncludeDetailedBreakdown { get; set; } = false;`)

Do not touch `GetMarginReportHandler.cs`, `GetMarginReportRequestValidator.cs`, or `AnalyticsController.cs` — none reference this property.

**Step 2 — Remove the test reference**

File: `backend/test/Anela.Heblo.Tests/Features/Analytics/Validators/GetMarginReportRequestValidatorTests.cs`

Current content around line 207-227 (test method `ValidRequest_ShouldNotHaveAnyValidationErrors`):
```csharp
    [Fact]
    public void ValidRequest_ShouldNotHaveAnyValidationErrors()
    {
        // Arrange
        var startDate = new DateTime(2024, 01, 01);
        var endDate = new DateTime(2024, 02, 01);
        var request = new GetMarginReportRequest
        {
            StartDate = startDate,
            EndDate = endDate,
            MaxProducts = 50,
            ProductFilter = null,
            CategoryFilter = null,
            IncludeDetailedBreakdown = false
        };

        // Act & Assert
        var result = _validator.TestValidate(request);
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }
```

Replace with (remove the `IncludeDetailedBreakdown = false` initializer line and the now-trailing comma on the preceding line):
```csharp
    [Fact]
    public void ValidRequest_ShouldNotHaveAnyValidationErrors()
    {
        // Arrange
        var startDate = new DateTime(2024, 01, 01);
        var endDate = new DateTime(2024, 02, 01);
        var request = new GetMarginReportRequest
        {
            StartDate = startDate,
            EndDate = endDate,
            MaxProducts = 50,
            ProductFilter = null,
            CategoryFilter = null
        };

        // Act & Assert
        var result = _validator.TestValidate(request);
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }
```

**Step 3 — Confirm no remaining references**

Run from repo root:
```bash
grep -rn "IncludeDetailedBreakdown\|includeDetailedBreakdown" backend/ --include="*.cs"
```
Expected output: no matches (empty result). If any match appears outside a generated-client file, investigate before proceeding — the spec/arch-review confirmed only the two locations edited above exist in hand-written backend code.

**Step 4 — Build and test the backend**

```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` (or equivalent success summary), no reference to `IncludeDetailedBreakdown` in any compiler error.

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetMarginReport"
```
Expected: all tests pass (`GetMarginReportRequestValidatorTests` and `GetMarginReportHandlerTests`), 0 failures.

Then run the full backend test suite to confirm nothing else broke:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```
Expected: 0 failures.

**Step 5 — Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportRequest.cs backend/test/Anela.Heblo.Tests/Features/Analytics/Validators/GetMarginReportRequestValidatorTests.cs
git commit -m "Remove dead IncludeDetailedBreakdown flag from GetMarginReportRequest"
```

---

### task: regenerate-openapi-clients-and-verify

**Context:** `GetMarginReportRequest` no longer declares `IncludeDetailedBreakdown` (removed in the previous task). The frontend TypeScript client (`frontend/src/api/generated/api-client.ts`) and, if built in Debug, the backend C# client (`backend/src/Anela.Heblo.API.Client/Generated/AnelaHebloApiClient.cs`) are NSwag-generated derived artifacts that still contain `includeDetailedBreakdown`/`IncludeDetailedBreakdown` from the old contract. This task regenerates them per `docs/development/api-client-generation.md` and verifies the frontend still builds clean. Generated files must never be hand-edited — regenerate only.

**Step 1 — Regenerate the frontend TypeScript client**

From repo root:
```bash
dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual
```

If this msbuild target is unavailable in the environment, use the equivalent npm script instead:
```bash
cd frontend && npm run generate-client
```

**Step 2 — Verify the generated client no longer references the removed parameter**

```bash
grep -n "includeDetailedBreakdown\|IncludeDetailedBreakdown" frontend/src/api/generated/api-client.ts
```
Expected output: no matches (empty result).

Also confirm the `analytics_GetMarginReport` method signature dropped the parameter and did not silently reorder `maxProducts` incorrectly:
```bash
grep -n "analytics_GetMarginReport" frontend/src/api/generated/api-client.ts
```
Expected: method signature includes `startDate, endDate, productFilter, categoryFilter, maxProducts` (5 params, in that order) with no `includeDetailedBreakdown` present.

**Step 3 — Regenerate the backend C# client (Debug-mode PostBuild artifact)**

```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj -c Debug
```
This triggers the `GenerateApiClient` PostBuild target automatically in Debug configuration, regenerating `backend/src/Anela.Heblo.API.Client/Generated/AnelaHebloApiClient.cs`.

Verify:
```bash
grep -n "IncludeDetailedBreakdown" backend/src/Anela.Heblo.API.Client/Generated/AnelaHebloApiClient.cs
```
Expected output: no matches (empty result).

**Step 4 — Verify the frontend builds and lints clean**

```bash
cd frontend
npm run build
```
Expected: build succeeds with no TypeScript errors (no compile error referencing `includeDetailedBreakdown` or a shifted-argument type mismatch on `analytics_GetMarginReport`).

```bash
npm run lint
```
Expected: no new lint errors introduced by the regeneration.

**Step 5 — Confirm no hand-written frontend caller was affected**

```bash
grep -rn "analytics_GetMarginReport(" frontend/src --include="*.ts" --include="*.tsx" | grep -v "frontend/src/api/generated/api-client.ts"
```
Expected output: no matches — confirms (as established in the arch review) there is no hand-written caller of this generated method that could be affected by the parameter-list shift.

**Step 6 — Final full verification pass**

```bash
dotnet build backend/Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```
Expected: build succeeds, all tests pass, 0 failures.

**Step 7 — Commit**

```bash
git add frontend/src/api/generated/api-client.ts backend/src/Anela.Heblo.API.Client/Generated/AnelaHebloApiClient.cs
git commit -m "Regenerate OpenAPI clients after removing IncludeDetailedBreakdown from GetMarginReport contract"
```

If the backend C# client generated file is gitignored (verify with `git status` — if it shows no changes, it's not tracked), skip staging it and commit only the frontend generated client file:
```bash
git add frontend/src/api/generated/api-client.ts
git commit -m "Regenerate OpenAPI TypeScript client after removing IncludeDetailedBreakdown from GetMarginReport contract"
```
