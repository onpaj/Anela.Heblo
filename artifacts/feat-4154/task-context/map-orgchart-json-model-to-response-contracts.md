### task: map-orgchart-json-model-to-response-contracts

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/Models/OrgChartJsonModel.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/OrgChartService.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/OrgChart/OrgChartServiceTests.cs` (add one new test; the four existing tests are unmodified)

- [ ] **Step 1: Run the existing test suite to confirm the baseline passes before any change**

Run:
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~OrgChartServiceTests"
```
Expected: All 4 existing tests in `OrgChartServiceTests` PASS.

- [ ] **Step 2: Write the failing happy-path mapping test**

This closes the coverage gap called out in `arch-review.r1.md` ("Risks and Mitigations" table, row 1): the four existing tests only cover error paths, none assert the full mapped object graph.

In `backend/test/Anela.Heblo.Tests/Features/OrgChart/OrgChartServiceTests.cs`, add `using Anela.Heblo.Application.Features.OrgChart.Contracts;` to the `using` block at the top (after `using Anela.Heblo.Application.Features.OrgChart;` on line 5):

```csharp
using Anela.Heblo.Application.Features.OrgChart.Contracts;
```

Then add the following new `[Fact]` immediately after `GetOrganizationStructureAsync_ThrowsOnNullDeserialization_AndDoesNotLogError` (i.e. after line 94, before the `private OrgChartService CreateService(...)` helper on line 96):

```csharp

    [Fact]
    public async Task GetOrganizationStructureAsync_MapsFullJsonGraphToResponseContracts()
    {
        // Arrange: a representative external JSON payload covering every mapped field,
        // including one position with a null Employees list and one employee with a null Url,
        // to exercise the null-coalescing mapping rules from design.r1.md.
        const string json = """
        {
          "organization": {
            "name": "Anela Heblo",
            "positions": [
              {
                "id": "pos-1",
                "title": "CEO",
                "description": "Chief Executive Officer",
                "level": 1,
                "parentPositionId": null,
                "department": "Executive",
                "url": "https://example.test/pos-1",
                "employees": [
                  {
                    "id": "emp-1",
                    "name": "Jana Nováková",
                    "email": "jana.novakova@anela.cz",
                    "startDate": "2020-01-15",
                    "isPrimary": true,
                    "url": "https://example.test/emp-1"
                  }
                ]
              },
              {
                "id": "pos-2",
                "title": "CFO",
                "description": "Chief Financial Officer",
                "level": 2,
                "parentPositionId": "pos-1",
                "department": "Finance",
                "url": null,
                "employees": null
              }
            ]
          }
        }
        """;
        var service = CreateService(StubHttpMessageHandler.Returns(HttpStatusCode.OK, json));

        // Act
        var result = await service.GetOrganizationStructureAsync(CancellationToken.None);

        // Assert: response envelope defaults (never populated from the JSON model)
        result.Success.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        result.Params.Should().BeNull();

        // Assert: organization + full position/employee graph, in order, all fields
        result.Organization.Name.Should().Be("Anela Heblo");
        result.Organization.Positions.Should().HaveCount(2);

        var pos1 = result.Organization.Positions[0];
        pos1.Id.Should().Be("pos-1");
        pos1.Title.Should().Be("CEO");
        pos1.Description.Should().Be("Chief Executive Officer");
        pos1.Level.Should().Be(1);
        pos1.ParentPositionId.Should().Be(string.Empty);
        pos1.Department.Should().Be("Executive");
        pos1.Url.Should().Be("https://example.test/pos-1");
        pos1.Employees.Should().HaveCount(1);

        var emp1 = pos1.Employees[0];
        emp1.Id.Should().Be("emp-1");
        emp1.Name.Should().Be("Jana Nováková");
        emp1.Email.Should().Be("jana.novakova@anela.cz");
        emp1.StartDate.Should().Be("2020-01-15");
        emp1.IsPrimary.Should().BeTrue();
        emp1.Url.Should().Be("https://example.test/emp-1");

        var pos2 = result.Organization.Positions[1];
        pos2.Id.Should().Be("pos-2");
        pos2.Title.Should().Be("CFO");
        pos2.Description.Should().Be("Chief Financial Officer");
        pos2.Level.Should().Be(2);
        pos2.ParentPositionId.Should().Be("pos-1");
        pos2.Department.Should().Be("Finance");
        pos2.Url.Should().Be(string.Empty);
        pos2.Employees.Should().BeEmpty();

        // Assert: service must not log Error (controller is the single owner)
        VerifyNoErrorLog();
    }
```

- [ ] **Step 3: Run the new test to confirm it fails (current implementation deserializes straight into `OrgChartResponse`, so property names collide with the payload's lowercase-first JSON keys and camelCase `PropertyNameCaseInsensitive` deserialization already happens to bind most fields onto `OrganizationDto`/`PositionDto`/`EmployeeDto` today — confirm what actually happens rather than assuming)**

Run:
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetOrganizationStructureAsync_MapsFullJsonGraphToResponseContracts"
```
Expected: The test either FAILS (e.g. `Success`/`ErrorCode`/`Params` assertions could still pass today since `BaseResponse`'s parameterless constructor already sets those defaults — but confirm actual output) or, if it unexpectedly PASSES against the pre-refactor code, that is not a regression signal to worry about: this test's purpose is to pin the *post-refactor* mapping behavior via TDD for the new code in Steps 4-6, not to prove the old code is broken (the spec's whole point is that the old coupling is a silent-risk, not necessarily an active bug for today's payload shape). Record the actual result before proceeding; do not skip this step.

- [ ] **Step 4: Create the adapter-internal JSON model file**

Create `backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/Models/OrgChartJsonModel.cs`:

```csharp
namespace Anela.Heblo.Adapters.OrgChart.Models;

internal sealed class OrgChartJsonModel
{
    public OrgChartJsonOrganization? Organization { get; set; }
}

internal sealed class OrgChartJsonOrganization
{
    public string? Name { get; set; }
    public List<OrgChartJsonPosition>? Positions { get; set; }
}

internal sealed class OrgChartJsonPosition
{
    public string? Id { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public int? Level { get; set; }
    public string? ParentPositionId { get; set; }
    public string? Department { get; set; }
    public List<OrgChartJsonEmployee>? Employees { get; set; }
    public string? Url { get; set; }
}

internal sealed class OrgChartJsonEmployee
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? StartDate { get; set; }
    public bool IsPrimary { get; set; }
    public string? Url { get; set; }
}
```

- [ ] **Step 5: Build to confirm the new file compiles standalone**

Run:
```bash
cd backend
dotnet build src/Adapters/Anela.Heblo.Adapters.OrgChart/Anela.Heblo.Adapters.OrgChart.csproj
```
Expected: Build succeeds (0 errors). `OrgChartService.cs` still compiles unchanged since the new file is additive and not yet referenced.

- [ ] **Step 6: Change `OrgChartService.cs` to deserialize into the new model and map explicitly**

Current content of `backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/OrgChartService.cs` (lines 1-69):

```csharp
using System.Text.Json;
using Anela.Heblo.Application.Features.OrgChart;
using Anela.Heblo.Application.Features.OrgChart.Contracts;
using Anela.Heblo.Application.Features.OrgChart.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.OrgChart;

/// <summary>
/// Service for retrieving organizational chart data from external source
/// </summary>
public class OrgChartService : IOrgChartService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly OrgChartOptions _options;
    private readonly ILogger<OrgChartService> _logger;

    public OrgChartService(
        HttpClient httpClient,
        IOptions<OrgChartOptions> options,
        ILogger<OrgChartService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OrgChartResponse> GetOrganizationStructureAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fetching organizational structure from {Url}", _options.DataSourceUrl);

            var response = await _httpClient.GetAsync(_options.DataSourceUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            var orgChart = JsonSerializer.Deserialize<OrgChartResponse>(content, JsonOptions);

            if (orgChart == null)
            {
                throw new InvalidOperationException("Failed to deserialize organizational structure");
            }

            _logger.LogInformation(
                "Successfully loaded organizational structure: {PositionCount} positions, {EmployeeCount} employees",
                orgChart.Organization.Positions.Count,
                orgChart.Organization.Positions.Sum(p => p.Employees.Count));

            return orgChart;
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to fetch organizational structure: {ex.Message}", ex);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to parse organizational structure: {ex.Message}", ex);
        }
    }
}
```

Replace the entire file with:

```csharp
using System.Text.Json;
using Anela.Heblo.Adapters.OrgChart.Models;
using Anela.Heblo.Application.Features.OrgChart;
using Anela.Heblo.Application.Features.OrgChart.Contracts;
using Anela.Heblo.Application.Features.OrgChart.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.OrgChart;

/// <summary>
/// Service for retrieving organizational chart data from external source
/// </summary>
public class OrgChartService : IOrgChartService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly OrgChartOptions _options;
    private readonly ILogger<OrgChartService> _logger;

    public OrgChartService(
        HttpClient httpClient,
        IOptions<OrgChartOptions> options,
        ILogger<OrgChartService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OrgChartResponse> GetOrganizationStructureAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fetching organizational structure from {Url}", _options.DataSourceUrl);

            var response = await _httpClient.GetAsync(_options.DataSourceUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            var jsonModel = JsonSerializer.Deserialize<OrgChartJsonModel>(content, JsonOptions);

            if (jsonModel == null)
            {
                throw new InvalidOperationException("Failed to deserialize organizational structure");
            }

            var orgChart = new OrgChartResponse
            {
                Organization = MapOrganization(jsonModel.Organization)
            };

            _logger.LogInformation(
                "Successfully loaded organizational structure: {PositionCount} positions, {EmployeeCount} employees",
                orgChart.Organization.Positions.Count,
                orgChart.Organization.Positions.Sum(p => p.Employees.Count));

            return orgChart;
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to fetch organizational structure: {ex.Message}", ex);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to parse organizational structure: {ex.Message}", ex);
        }
    }

    private static OrganizationDto MapOrganization(OrgChartJsonOrganization? organization)
    {
        return new OrganizationDto
        {
            Name = organization?.Name ?? string.Empty,
            Positions = organization?.Positions?.Select(MapPosition).ToList() ?? new List<PositionDto>()
        };
    }

    private static PositionDto MapPosition(OrgChartJsonPosition position)
    {
        return new PositionDto
        {
            Id = position.Id ?? string.Empty,
            Title = position.Title ?? string.Empty,
            Description = position.Description ?? string.Empty,
            Level = position.Level,
            ParentPositionId = position.ParentPositionId ?? string.Empty,
            Department = position.Department ?? string.Empty,
            Url = position.Url ?? string.Empty,
            Employees = position.Employees?.Select(MapEmployee).ToList() ?? new List<EmployeeDto>()
        };
    }

    private static EmployeeDto MapEmployee(OrgChartJsonEmployee employee)
    {
        return new EmployeeDto
        {
            Id = employee.Id ?? string.Empty,
            Name = employee.Name ?? string.Empty,
            Email = employee.Email ?? string.Empty,
            StartDate = employee.StartDate ?? string.Empty,
            IsPrimary = employee.IsPrimary,
            Url = employee.Url ?? string.Empty
        };
    }
}
```

This implements FR-1/FR-2/FR-3 of `spec.r1.md` and the field-mapping table of `design.r1.md`: `Level` and `IsPrimary` pass through with no coalescing (design table rows 6 and 13 of the "Field-level mapping rules"), every string field defaults via `?? string.Empty`, every list defaults via `?.Select(...).ToList() ?? new()`, the `try`/`catch` structure and both exception-wrapping messages are byte-for-byte unchanged, the null-check message and no-inner-exception shape are unchanged (now checked against `OrgChartJsonModel`), and `LogInformation` still reads from the mapped `orgChart` (the final `OrgChartResponse`), not the intermediate `jsonModel`.

- [ ] **Step 7: Build the solution**

Run:
```bash
cd backend
dotnet build
```
Expected: Build succeeds (0 errors, 0 new warnings).

- [ ] **Step 8: Run `dotnet format` to match project formatting conventions**

Run:
```bash
cd backend
dotnet format --verify-no-changes
```
Expected: No formatting changes required. If it reports changes, run `dotnet format` (without `--verify-no-changes`) and re-run `dotnet build` to confirm the fix didn't break anything, then continue.

- [ ] **Step 9: Run the full `OrgChartServiceTests` suite to confirm all 5 tests pass (4 existing + 1 new)**

Run:
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~OrgChartServiceTests"
```
Expected: All 5 tests PASS, including `GetOrganizationStructureAsync_MapsFullJsonGraphToResponseContracts`.

- [ ] **Step 10: Run the two other test files named in spec FR-3's acceptance criteria to confirm no regression**

Run:
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetOrganizationStructureHandlerTests|FullyQualifiedName~OrgChartModuleValidationTests"
```
Expected: All tests in `GetOrganizationStructureHandlerTests` and `OrgChartModuleValidationTests` PASS unmodified (these exercise `IOrgChartService`/`OrgChartModule`/DI registration, none of which changed).

- [ ] **Step 11: Run the full backend test suite as a final safety net**

Run:
```bash
cd backend
dotnet test
```
Expected: All tests PASS (no regressions introduced anywhere else in the solution).

- [ ] **Step 12: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/Models/OrgChartJsonModel.cs
git add backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/OrgChartService.cs
git add backend/test/Anela.Heblo.Tests/Features/OrgChart/OrgChartServiceTests.cs
git commit -m "$(cat <<'EOF'
Decouple OrgChartService deserialization from OrgChartResponse contract

OrgChartService deserialized the external org-chart JSON directly into
OrgChartResponse, an Application-layer API response contract that
inherits Success/ErrorCode/Params from BaseResponse. Those fields
silently took on BaseResponse's default constructor values regardless
of what the external source returned, and any rename of
OrgChartResponse/OrganizationDto members would have silently changed
external JSON parsing with no compiler or test signal.

Add adapter-internal OrgChartJsonModel/OrgChartJsonOrganization/
OrgChartJsonPosition/OrgChartJsonEmployee POCOs that mirror the
external JSON shape independently of Application contracts, and map
them explicitly into OrgChartResponse/OrganizationDto/PositionDto/
EmployeeDto. No change to IOrgChartService, OrgChartModule, DI
registration, or observable behavior. Adds a happy-path mapping test
to close the coverage gap noted in the architecture review (only
error paths were previously covered).

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_016Xvtaftcb8eDJ9a2auhs7v
EOF
)"
```

---

## Self-Review

### Spec coverage (every FR/NFR mapped to a task/step)

| Requirement | Covered by |
|---|---|
| FR-1 (four adapter-internal classes, no `BaseResponse` dependency, `internal`, not referenced outside Adapters project) | Step 4 (creates `Models/OrgChartJsonModel.cs` with exactly the four `internal sealed class` types per FR-1/design.r1.md); Step 5 confirms standalone compile; the classes are referenced only from `OrgChartService.cs` (Step 6) and never from Application/Core projects |
| FR-2.1 (deserialize into `OrgChartJsonModel` using existing `JsonOptions`) | Step 6, `JsonSerializer.Deserialize<OrgChartJsonModel>(content, JsonOptions)` |
| FR-2.2 (null-check guard on deserialized model) | Step 6, `if (jsonModel == null) throw ...` |
| FR-2.3 (explicit construction of `OrgChartResponse` via parameterless constructor, field-by-field mapping) | Step 6, `new OrgChartResponse { Organization = MapOrganization(...) }` plus `MapOrganization`/`MapPosition`/`MapEmployee` |
| FR-2.4 (null-coalescing rules: strings → `string.Empty`, lists → empty list, `Level` passes through) | Step 6 mapping methods, matching the design.r1.md field-mapping table exactly; asserted by the new test in Step 2 (`pos1.ParentPositionId` empty when null-input case isn't hit, `pos2.Url`/`pos2.Employees` empty-list/empty-string when JSON supplies `null`) |
| FR-2 acceptance: never `Deserialize<OrgChartResponse/*Dto>` in Adapters project | Step 6 replaces the only such call in the project; Step 7 build + Step 9-11 tests confirm the file compiles and behaves correctly with no other deserialization call sites in this project |
| FR-2 acceptance: identical output graph vs. today for same payload | New happy-path test (Step 2/3/9) asserts the full graph (name, 2 positions in order, nested employee) field-by-field |
| FR-2 acceptance: `Success=true`, `ErrorCode=null`, `Params=null` on success | Asserted explicitly in the new test (Step 2) |
| FR-3 (exception wrapping messages/types unchanged, null-check message/no-inner-exception unchanged, no `LogError` added, `LogInformation` reads final `OrgChartResponse`) | Step 6 preserves the `try`/`catch` structure and both catch-block messages verbatim; null-check message unchanged; `LogInformation` still reads `orgChart.Organization.Positions...` (the mapped result); no `LogError`/`ILogger.Log(LogLevel.Error, ...)` call added anywhere — verified by all 4 existing `VerifyNoErrorLog()` assertions plus the new test's own `VerifyNoErrorLog()` call, all run in Step 9 |
| FR-3 acceptance: 4 existing `OrgChartServiceTests` pass unmodified | Step 9 (existing 4 tests untouched, run alongside the 1 new test) |
| FR-3 acceptance: `GetOrganizationStructureHandlerTests`/`OrgChartModuleValidationTests` pass unmodified | Step 10 |
| NFR-1 (performance: no regression expected, no benchmark required) | No benchmark added, per spec — the mapping is an in-memory `.Select(...).ToList()` pass equivalent in cost to the removed indirection; no action needed beyond what Steps 6-11 already verify functionally |
| NFR-2 (security: no auth/secret/Shoptet-doc changes) | No such changes made anywhere in this plan; not applicable |
| Out of Scope items (no `IOrgChartService`/`OrgChartModule`/DI/handler changes; no `BaseResponse`/`*Dto` changes; no schema validation; no logging-level/retry changes; no Shoptet doc changes) | None of these files are touched by any step — only `OrgChartService.cs` (modified) and `Models/OrgChartJsonModel.cs` (new) in the Adapters project, plus the test file |

No gaps identified: every FR, NFR, and acceptance criterion in `spec.r1.md` maps to a concrete step, and every architectural decision in `arch-review.r1.md`/`design.r1.md` (file location, `internal sealed class`, plain classes not records, 3 private static mapping methods, no AutoMapper, `Level`/`IsPrimary` passed through without coalescing) is followed literally in Step 4 and Step 6.

### Placeholder scan

Every step contains complete, literal code or exact shell commands with an expected result — no "TBD", "similar to above", "add appropriate handling", or elided code blocks. The before/after blocks in Step 6 are the full, exact current and replacement contents of `OrgChartService.cs` (verified against the file at `backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/OrgChartService.cs` read during planning). The new test in Step 2 is the complete method body, not a sketch.

### Type/name consistency check across tasks

Single task; all types and members are used consistently within it:
- `OrgChartJsonModel.Organization : OrgChartJsonOrganization?` — matches `MapOrganization(jsonModel.Organization)` call site.
- `OrgChartJsonOrganization.Name : string?`, `.Positions : List<OrgChartJsonPosition>?` — matches `MapOrganization`'s `organization?.Name` / `organization?.Positions?.Select(MapPosition)`.
- `OrgChartJsonPosition.{Id,Title,Description,Level,ParentPositionId,Department,Employees,Url}` — matches every field read in `MapPosition`, no extra/missing members, `Level` typed `int?` on both source (`OrgChartJsonPosition.Level`) and target (`PositionDto.Level`), passed through with no coalescing per design.r1.md row 6.
- `OrgChartJsonEmployee.{Id,Name,Email,StartDate,IsPrimary,Url}` — matches every field read in `MapEmployee`; `IsPrimary` typed `bool` (non-nullable) on both source and target, passed through directly per design.r1.md row 13.
- Target types `OrganizationDto`/`PositionDto`/`EmployeeDto` and their member names/types used in the mapping methods match the actual current definitions read from `backend/src/Anela.Heblo.Application/Features/OrgChart/Contracts/{OrganizationDto,PositionDto,EmployeeDto}.cs` (confirmed during planning: `PositionDto.ParentPositionId`/`Url` and `EmployeeDto.Url` are `string?` in the DTO itself, so assigning `?? string.Empty` — a non-null `string` — to them is valid C#, consistent with the design.r1.md field-mapping table which prescribes `?? string.Empty` for these three fields specifically, overriding the DTO's own implicit-null default for those three members only).
- Namespace `Anela.Heblo.Adapters.OrgChart.Models` (new file) is imported in `OrgChartService.cs` via `using Anela.Heblo.Adapters.OrgChart.Models;`, consistent with the file's declared namespace.
- The new test method name `GetOrganizationStructureAsync_MapsFullJsonGraphToResponseContracts` follows the existing naming convention in the same file (`GetOrganizationStructureAsync_<Behavior>_AndDoesNotLogError`-style prefix, adapted since this test's purpose is mapping-correctness rather than error/no-log behavior alone — it still calls `VerifyNoErrorLog()` for consistency with every other test in the file).
