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
