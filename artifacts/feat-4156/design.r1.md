# Design: OrgChartService success-path unit test

## Component Design

No production components change. The only component touched is the test class itself:

- **`OrgChartServiceTests`** (`backend/test/Anela.Heblo.Tests/Features/OrgChart/OrgChartServiceTests.cs`) gains one additional `[Fact]` test method, `GetOrganizationStructureAsync_ReturnsMappedResponse_WhenJsonIsValid`, alongside its four existing error-path tests. It responsibility is unchanged: verify `OrgChartService.GetOrganizationStructureAsync` behavior against a stubbed `HttpMessageHandler`, now covering the success path in addition to the four failure modes.
- The new test reuses the class's existing private members exactly as they stand today — no new helper is introduced:
  - `StubHttpMessageHandler.Returns(HttpStatusCode status, string body)` — supplies the fake HTTP 200 response.
  - `CreateService(HttpMessageHandler handler)` — constructs `OrgChartService` wired to the stub, `_options` (pointing at `TestDataSourceUrl`), and `_loggerMock.Object`.
  - `VerifyNoErrorLog()` — asserts `ILogger<OrgChartService>.Log(LogLevel.Error, ...)` was never invoked, matching the "controller is the single owner of Error logging" invariant already documented in the sibling tests.
- **`OrgChartService`**, **`OrgChartResponse`**, **`BaseResponse`**, **`OrganizationDto`**, **`PositionDto`**, **`EmployeeDto`** — all consumed as-is, no interface or behavior change.

## Data Schemas

No database schema, API request/response shape, or event payload changes. This section documents the JSON fixture shape the new test feeds through the existing `JsonSerializer.Deserialize<OrgChartResponse>(content, JsonOptions)` call (`JsonOptions` sets `PropertyNameCaseInsensitive = true`, so the fixture may use conventional lowerCamelCase JSON keys):

```json
{
  "organization": {
    "name": "Anela",
    "positions": [
      {
        "id": "pos-1",
        "title": "CEO",
        "description": "Chief Executive Officer",
        "level": 1,
        "department": "Executive",
        "employees": [
          {
            "id": "emp-1",
            "name": "Jana Novakova",
            "email": "jana.novakova@anela.cz",
            "startDate": "2020-01-01",
            "isPrimary": true
          }
        ]
      }
    ]
  }
}
```

Deserialization target and the fields the test asserts against (all pre-existing types, unchanged):

| JSON path | C# target | Asserted in new test |
|---|---|---|
| `organization.name` | `OrgChartResponse.Organization.Name` | yes — equals `"Anela"` |
| `organization.positions[0].id` | `PositionDto.Id` | yes |
| `organization.positions[0].title` | `PositionDto.Title` | yes |
| `organization.positions[0].level` | `PositionDto.Level` (`int?`) | yes |
| `organization.positions[0].department` | `PositionDto.Department` | yes |
| `organization.positions[0].employees[0].name` | `EmployeeDto.Name` | yes |
| `organization.positions[0].employees[0].isPrimary` | `EmployeeDto.IsPrimary` (`bool`) | yes |
| — (absent from JSON) | `BaseResponse.Success` (defaults `true` via `OrgChartResponse()`'s parameterless base ctor) | yes — asserted `true` |
| — (absent from JSON) | `BaseResponse.ErrorCode` (`ErrorCodes?`, default `null`) | yes — asserted `null` |

Fields not populated in the fixture (`description`, `parentPositionId`, `url` on `PositionDto`/`EmployeeDto`, `email`/`startDate` beyond what's shown) are left at their type defaults and are not asserted — consistent with the spec's stated scope (one success-path test proving the mapping works, not exhaustive per-field coverage).
