# Task Plan: Unit tests for UpdateRuleRequestValidator (Photobank)

### task: add-updaterulerequestvalidator-tests
Add unit tests closing the coverage gap on `UpdateRuleRequestValidator` and its dependency `PhotobankValidationHelpers.BeValidRegex`. This is a pure test-authoring task — no production code changes.

**Files to create:**

1. `backend/test/Anela.Heblo.Tests/Features/Photobank/UpdateRuleRequestValidatorTests.cs`
   - Namespace: `Anela.Heblo.Tests.Features.Photobank`
   - Class under test: `Anela.Heblo.Application.Features.Photobank.Validators.UpdateRuleRequestValidator` (public `AbstractValidator<UpdateRuleRequest>`, no constructor args — instantiate directly, e.g. `_validator = new UpdateRuleRequestValidator();`).
   - Request DTO under test: `Anela.Heblo.Application.Features.Photobank.UseCases.UpdateRule.UpdateRuleRequest` with settable properties `int Id`, `string PathPattern`, `string TagName`, `bool IsActive`, `int SortOrder`.
   - Use FluentValidation's `TestValidate` extension (`FluentValidation.TestHelper`, already referenced by the test project) — call `_validator.TestValidate(request)` and assert with `ShouldHaveValidationErrorFor` / `ShouldNotHaveValidationErrorFor` / `ShouldNotHaveAnyValidationErrors`, matching the style of the existing sibling file `backend/test/Anela.Heblo.Tests/Features/Photobank/GetPhotosRequestValidatorTests.cs` (read this file first for exact conventions: how requests are built, how `WithErrorMessage` is asserted, `[Theory]`/`[InlineData]` usage).
   - Build every test request from a fully-valid baseline, varying only the field under test, to avoid cross-field noise:
     ```csharp
     var request = new UpdateRuleRequest
     {
         Id = 1,
         PathPattern = "^[a-z]+\\.png$",
         TagName = "summer",
         SortOrder = 0,
         IsActive = true
     };
     ```
   - Test cases to implement (one `[Fact]` or a `[Theory]`/`[InlineData]` group per bullet; `[Theory]` may collapse near-duplicate cases such as `Id = 0`/`Id = -1`, per the design doc — this is a style choice):
     - Baseline: the fully-valid request above produces `ShouldNotHaveAnyValidationErrors()`.
     - `Id = 0` and `Id = -1`: each produces `ShouldHaveValidationErrorFor(x => x.Id).WithErrorMessage("Id must be a positive integer")`.
     - `PathPattern = ""` and `PathPattern = null`: each produces `ShouldHaveValidationErrorFor(x => x.PathPattern).WithErrorMessage("PathPattern is required")`.
     - `PathPattern` = a 501-character benign literal (e.g. `new string('a', 501)` — always a syntactically valid regex, so only the length rule fires): produces `ShouldHaveValidationErrorFor(x => x.PathPattern).WithErrorMessage("PathPattern cannot exceed 500 characters")`.
     - `PathPattern` = a 500-character valid-regex literal (e.g. `new string('a', 500)`): boundary case, produces `ShouldNotHaveValidationErrorFor(x => x.PathPattern)`.
     - `PathPattern = "["` (syntactically invalid regex): produces `ShouldHaveValidationErrorFor(x => x.PathPattern).WithErrorMessage("Invalid regular expression pattern.")`.
     - `PathPattern = "^[a-z]+"` (syntactically valid regex): produces `ShouldNotHaveValidationErrorFor(x => x.PathPattern)`.
     - `TagName = ""` and `TagName = null`: each produces `ShouldHaveValidationErrorFor(x => x.TagName).WithErrorMessage("TagName is required")`.
     - `TagName` = a 101-character string: produces `ShouldHaveValidationErrorFor(x => x.TagName).WithErrorMessage("TagName cannot exceed 100 characters")`.
     - `SortOrder = -1`: produces `ShouldHaveValidationErrorFor(x => x.SortOrder)` (no `WithErrorMessage` — the rule `GreaterThanOrEqualTo(0)` has no custom `.WithMessage()`, so only assert the error exists, not its text).
     - `SortOrder = 0`: boundary case, produces `ShouldNotHaveValidationErrorFor(x => x.SortOrder)`.

2. `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankValidationHelpersTests.cs`
   - Namespace: `Anela.Heblo.Tests.Features.Photobank`
   - Class under test: `Anela.Heblo.Application.Features.Photobank.Validators.PhotobankValidationHelpers.BeValidRegex(string? pattern)` — `internal static bool`, callable directly via fully-qualified static reference (e.g. `PhotobankValidationHelpers.BeValidRegex(pattern)`) because `Anela.Heblo.Application.csproj` already grants `InternalsVisibleTo("Anela.Heblo.Tests")`. No DI, no constructor state needed (static method).
   - Test cases (no DTO involved, call the helper directly and assert the returned `bool`):
     - `[Theory]` with `[InlineData(null)]`, `[InlineData("")]`, `[InlineData("   ")]` → assert `BeValidRegex(pattern)` returns `true`.
     - `[Fact]` → `BeValidRegex("^[a-z]+$")` returns `true`.
     - `[Theory]` with `[InlineData("[")]`, `[InlineData("(unclosed")]` → assert `BeValidRegex(pattern)` returns `false`.

**Constraints:**
- Do not modify `UpdateRuleRequestValidator.cs`, `PhotobankValidationHelpers.cs`, `UpdateRuleRequest.cs`, `UpdateRuleHandler.cs`, or any other production file.
- Do not add tests for `UpdateRuleHandler`, the controller/endpoint, or `AddRuleRequestValidator` — out of scope.
- No new NuGet packages or project references are required or permitted; `FluentValidation.TestHelper` and `xunit` are already referenced by `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`.
- Match the existing flat-folder convention: both files go directly in `backend/test/Anela.Heblo.Tests/Features/Photobank/`, no new subfolders.

**Acceptance criteria:**
- Both new files exist at the paths above, compile, and contain all test cases enumerated.
- Every acceptance-criteria bullet from `artifacts/feat-3616/spec.r1.md` (FR-1 and FR-2) is covered by at least one test.
- All new tests pass; no existing test is modified or broken.
- No production source file is changed (verify with `git status` / `git diff --stat` showing only the two new test files added).

**Verification:**
1. Build the test project:
   `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
2. Run only the new tests and confirm all pass (adjust filter names if actual method names differ, but keep the class-name filter):
   `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Photobank.UpdateRuleRequestValidatorTests|FullyQualifiedName~Anela.Heblo.Tests.Features.Photobank.PhotobankValidationHelpersTests"`
3. Run the full test project once to confirm no regressions were introduced:
   `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
4. Confirm no production files changed: `git status --short` should show only the two new test files as additions.
