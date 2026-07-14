# Design: Unit tests for UpdateRuleRequestValidator (Photobank)

## Component Design

No production components change. Two new xUnit test classes are added to `Anela.Heblo.Tests`, each targeting one existing, untested unit:

### `UpdateRuleRequestValidatorTests`
- **Path:** `backend/test/Anela.Heblo.Tests/Features/Photobank/UpdateRuleRequestValidatorTests.cs`
- **Namespace:** `Anela.Heblo.Tests.Features.Photobank`
- **Responsibility:** Exercise every `RuleFor` clause of `UpdateRuleRequestValidator` (`Id`, `PathPattern`, `TagName`, `SortOrder`) via FluentValidation's `TestValidate`, asserting both failure messages and pass-through (valid/boundary) cases.
- **Interface under test:** `UpdateRuleRequestValidator` (public, `AbstractValidator<UpdateRuleRequest>`) — instantiated directly in the constructor (no DI, no mocking), matching `GetPhotosRequestValidatorTests`.
- **Structure:**
  - Constructor: `_validator = new UpdateRuleRequestValidator();`
  - `[Fact]` per discrete case (valid baseline, `Id` boundary, `PathPattern` empty/null/too-long/500-boundary/invalid-regex/valid-regex, `TagName` empty/null/too-long, `SortOrder` negative/zero-boundary).
  - `[Theory]`/`[InlineData]` may be used to collapse near-duplicate cases (e.g. `Id = 0` and `Id = -1`) per the arch review's guidance, but this is a style choice, not a contract.
  - Each request is built as a fully-valid `UpdateRuleRequest` baseline (`Id = 1`, `PathPattern = "^[a-z]+\\.png$"`, `TagName = "summer"`, `SortOrder = 0`, `IsActive = true`) with only the field under test varied, to avoid cross-field noise (per spec and arch review).
  - Assertions use `result.ShouldHaveValidationErrorFor(x => x.<Field>).WithErrorMessage("...")` for every rule that defines `.WithMessage(...)`, and `ShouldNotHaveValidationErrorFor` / `ShouldNotHaveAnyValidationErrors` for pass cases. `SortOrder`'s `GreaterThanOrEqualTo(0)` rule has no custom message, so its failure case asserts only `ShouldHaveValidationErrorFor(x => x.SortOrder)` without `WithErrorMessage`.

### `PhotobankValidationHelpersTests`
- **Path:** `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankValidationHelpersTests.cs`
- **Namespace:** `Anela.Heblo.Tests.Features.Photobank`
- **Responsibility:** Exercise `PhotobankValidationHelpers.BeValidRegex(string?)` directly and in isolation, independent of any validator that consumes it.
- **Interface under test:** `PhotobankValidationHelpers.BeValidRegex` — `internal static bool BeValidRegex(string? pattern)`, called via fully-qualified static reference; visible to the test assembly through the existing `InternalsVisibleTo("Anela.Heblo.Tests")` grant on `Anela.Heblo.Application.csproj` (no project changes needed).
- **Structure:**
  - No constructor state needed (static method, no instance fields).
  - `[Theory]`/`[InlineData(null)]`, `[InlineData("")]`, `[InlineData("   ")]` → assert `BeValidRegex(pattern)` returns `true` (null/empty/whitespace short-circuits to valid).
  - `[Fact]` → `BeValidRegex("^[a-z]+$")` returns `true`.
  - `[Theory]`/`[InlineData("[")]`, `[InlineData("(unclosed")]` → assert `BeValidRegex(pattern)` returns `false`.

Both classes follow the existing flat-folder, 1:1 file-to-tested-unit convention already used by `GetPhotosRequestValidatorTests` and `BulkAddPhotoTagRequestValidatorTests` in the same directory — no new folders, base classes, or test utilities are introduced.

## Data Schemas

No database, API, or event schema changes. Tests construct instances of the existing `UpdateRuleRequest` DTO in-memory only:

```csharp
public class UpdateRuleRequest : IRequest<UpdateRuleResponse>
{
    public int Id { get; set; }
    public string PathPattern { get; set; } = null!;
    public string TagName { get; set; } = null!;
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
}
```

Test input/output "shapes" (in-memory only, not persisted or serialized):

| Case group | Field varied | Input value(s) | Expected `TestValidate` outcome |
|---|---|---|---|
| Baseline | — | `Id=1, PathPattern="^[a-z]+\.png$", TagName="summer", SortOrder=0, IsActive=true` | `ShouldNotHaveAnyValidationErrors()` |
| `Id` | `Id` | `0`, `-1` | error on `Id`, message `"Id must be a positive integer"` |
| `PathPattern` required | `PathPattern` | `""`, `null` | error on `PathPattern`, message `"PathPattern is required"` |
| `PathPattern` length | `PathPattern` | 501-char benign literal (e.g. `"a"` × 501) | error on `PathPattern`, message `"PathPattern cannot exceed 500 characters"` |
| `PathPattern` length boundary | `PathPattern` | 500-char valid-regex literal | `ShouldNotHaveValidationErrorFor(x => x.PathPattern)` |
| `PathPattern` regex | `PathPattern` | `"["` | error on `PathPattern`, message `"Invalid regular expression pattern."` |
| `PathPattern` regex valid | `PathPattern` | `"^[a-z]+"` | `ShouldNotHaveValidationErrorFor(x => x.PathPattern)` |
| `TagName` required | `TagName` | `""`, `null` | error on `TagName`, message `"TagName is required"` |
| `TagName` length | `TagName` | 101-char string | error on `TagName`, message `"TagName cannot exceed 100 characters"` |
| `SortOrder` | `SortOrder` | `-1` | error on `SortOrder` (no message assertion) |
| `SortOrder` boundary | `SortOrder` | `0` | `ShouldNotHaveValidationErrorFor(x => x.SortOrder)` |

`BeValidRegex` input/output (direct helper calls, no DTO involved):

| Input (`pattern`) | Expected return |
|---|---|
| `null` | `true` |
| `""` | `true` |
| `"   "` | `true` |
| `"^[a-z]+$"` | `true` |
| `"["` | `false` |
| `"(unclosed"` | `false` |
