### task: add-invalid-date-factory

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate/GetExpeditionListsByDateResponse.cs`
- Test: `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionListsByDateHandlerTests.cs` (new test method, appended)

- [ ] **Step 1: Write the failing test for the new factory**

Read the current test file first:

```bash
cat backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionListsByDateHandlerTests.cs
```

Add a new test method to the existing `GetExpeditionListsByDateHandlerTests` class (append it after `Handle_ReturnsFailure_WhenDateIsInvalid`, before the closing `}` of the class):

```csharp
    [Fact]
    public void InvalidDate_ReturnsExpectedFailureShape()
    {
        // Act
        var result = GetExpeditionListsByDateResponse.InvalidDate();

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.InvalidFormat, result.ErrorCode);
        Assert.NotNull(result.Params);
        Assert.Equal("Date", result.Params!["Field"]);
        Assert.Equal("yyyy-MM-dd", result.Params!["ExpectedFormat"]);
        Assert.Empty(result.Items);
    }
```

**Step 2: Run test to verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetExpeditionListsByDateHandlerTests.InvalidDate_ReturnsExpectedFailureShape"`
Expected: FAIL with a compile error — `'GetExpeditionListsByDateResponse' does not contain a definition for 'InvalidDate'`

- [ ] **Step 3: Implement the minimal factory method**

Modify `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate/GetExpeditionListsByDateResponse.cs` to:

```csharp
using Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.GetExpeditionListsByDate;

public class GetExpeditionListsByDateResponse : BaseResponse
{
    public List<ExpeditionListItemDto> Items { get; set; } = new();

    public static GetExpeditionListsByDateResponse InvalidDate() =>
        new()
        {
            Success = false,
            ErrorCode = ErrorCodes.InvalidFormat,
            Params = new Dictionary<string, string>
            {
                { "Field", "Date" },
                { "ExpectedFormat", "yyyy-MM-dd" }
            }
        };
}
```

(Only the `InvalidDate()` method is new; the existing `using` directives, namespace, class declaration, and `Items` property are unchanged — verify your edit leaves them exactly as they were.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetExpeditionListsByDateHandlerTests.InvalidDate_ReturnsExpectedFailureShape"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate/GetExpeditionListsByDateResponse.cs backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionListsByDateHandlerTests.cs
git commit -m "feat(expedition-list-archive): add GetExpeditionListsByDateResponse.InvalidDate() factory"
```

