### task: add-orgchart-success-test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/OrgChart/OrgChartServiceTests.cs:94-95` (insert new test method between the last existing `[Fact]`, which ends at line 94, and the `private OrgChartService CreateService(...)` helper, which starts at line 96)

- [ ] **Step 1: Add the new test method**

Insert the following method immediately after the closing brace of `GetOrganizationStructureAsync_ThrowsOnNullDeserialization_AndDoesNotLogError` (line 94) and before `private OrgChartService CreateService(...)` (line 96):

```csharp
    [Fact]
    public async Task GetOrganizationStructureAsync_ReturnsMappedResponse_WhenJsonIsValid()
    {
        // Arrange: 200 OK with a valid JSON body describing one position with one nested employee,
        // so the test exercises the full deserialization tree, not just the top-level shell.
        const string json = """
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
        """;
        var service = CreateService(StubHttpMessageHandler.Returns(HttpStatusCode.OK, json));

        // Act
        var result = await service.GetOrganizationStructureAsync(CancellationToken.None);

        // Assert: base response shape (BaseResponse defaults via OrgChartResponse()'s parameterless ctor)
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ErrorCode.Should().BeNull();

        // Assert: top-level organization mapping
        result.Organization.Name.Should().Be("Anela");
        result.Organization.Positions.Should().HaveCount(1);

        // Assert: nested position mapping (guards against a PositionDto field rename going undetected)
        var position = result.Organization.Positions[0];
        position.Id.Should().Be("pos-1");
        position.Title.Should().Be("CEO");
        position.Level.Should().Be(1);
        position.Department.Should().Be("Executive");
        position.Employees.Should().HaveCount(1);

        // Assert: nested employee mapping (guards against an EmployeeDto field rename going undetected)
        var employee = position.Employees[0];
        employee.Name.Should().Be("Jana Novakova");
        employee.IsPrimary.Should().BeTrue();

        // Assert: service must not log Error (controller is the single owner)
        VerifyNoErrorLog();
    }
```

- [ ] **Step 2: Run the new test in isolation to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~OrgChartServiceTests.GetOrganizationStructureAsync_ReturnsMappedResponse_WhenJsonIsValid"`

Expected: `Passed! - Failed: 0, Passed: 1, Skipped: 0` (this is a coverage addition against already-correct production code, per spec FR-1's acceptance criteria — the test is expected to go green immediately, not follow red→green TDD, since `OrgChartService.GetOrganizationStructureAsync` is not being changed).

If it fails instead, do not modify `OrgChartService.cs` to make it pass — re-check the JSON fixture's property names against `OrganizationDto`/`PositionDto`/`EmployeeDto` (`PropertyNameCaseInsensitive = true` is set on `OrgChartService.JsonOptions`, so casing is not the likely cause; a mismatched property name is).

- [ ] **Step 3: Run the full `OrgChartServiceTests` class to confirm no regressions**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~OrgChartServiceTests"`

Expected: `Passed! - Failed: 0, Passed: 5, Skipped: 0` (the four existing tests plus the new one).

- [ ] **Step 4: Run the full backend test suite and build/format validation**

Run:
```bash
cd backend
dotnet build
dotnet format --verify-no-changes
dotnet test
```

Expected: build succeeds with no errors, `dotnet format --verify-no-changes` reports no files needing changes (if it does, run `dotnet format` without the flag and re-stage), and the full test run reports `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/OrgChart/OrgChartServiceTests.cs
git commit -m "test(orgchart): add success-path test for OrgChartService JSON mapping"
```

---

## Self-Review

**Spec coverage:** FR-1 (add one success-path test asserting `Success`, `ErrorCode`, `Organization.Name`, and nested `PositionDto`/`EmployeeDto` fields, without modifying production code) is fully covered by `add-orgchart-success-test`, Steps 1-2. NFR-1/NFR-2 are explicitly not applicable (stated in spec) and require no task. The spec's "Out of Scope" items (no production code changes, no additional edge-case tests, no changes to `OrgChartControllerTests`/`OrgChartModuleValidationTests`) are respected — this plan touches exactly one file.

**Placeholder scan:** No "TBD"/"TODO"/"handle edge cases" language; the test method is complete, runnable code; commands and expected outputs are exact.

**Type consistency:** `CreateService`, `StubHttpMessageHandler.Returns`, and `VerifyNoErrorLog` are used with the exact signatures already defined in `OrgChartServiceTests.cs` (confirmed by reading the file: `CreateService(HttpMessageHandler)` at line 96, `StubHttpMessageHandler.Returns(HttpStatusCode, string)` at line 115, `VerifyNoErrorLog()` at line 99). `OrgChartResponse.Organization`, `OrganizationDto.Name`/`Positions`, `PositionDto.Id`/`Title`/`Level`/`Department`/`Employees`, and `EmployeeDto.Name`/`IsPrimary` all match the property names and types confirmed by reading `OrgChartResponse.cs`, `OrganizationDto.cs`, `PositionDto.cs`, and `EmployeeDto.cs` directly — no invented members.
