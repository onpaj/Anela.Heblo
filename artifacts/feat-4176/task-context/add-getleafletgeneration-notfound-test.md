### task: add-getleafletgeneration-notfound-test

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/GetLeafletGenerationHandlerTests.cs`

- [ ] **Step 1: Confirm the file does not already exist**

Run: `test -f backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/GetLeafletGenerationHandlerTests.cs && echo EXISTS || echo MISSING`
Expected: `MISSING`

- [ ] **Step 2: Write the test file**

Create `backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/GetLeafletGenerationHandlerTests.cs` with this exact content:

```csharp
using Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletGeneration;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Leaflet;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Leaflet.UseCases;

public class GetLeafletGenerationHandlerTests
{
    private readonly Mock<ILeafletGenerationRepository> _repoMock = new();

    private GetLeafletGenerationHandler CreateHandler() =>
        new(_repoMock.Object);

    [Fact]
    public async Task Handle_returns_not_found_error_code_and_default_fields_when_generation_missing()
    {
        // Arrange
        var requestedId = Guid.NewGuid();
        _repoMock
            .Setup(r => r.GetGenerationByIdAsync(requestedId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LeafletGeneration?)null);

        var handler = CreateHandler();
        var request = new GetLeafletGenerationRequest { Id = requestedId };

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.LeafletFeedbackNotFound);
        response.Id.Should().Be(Guid.Empty);
        response.Topic.Should().Be(string.Empty);
        response.Audience.Should().Be(string.Empty);
        response.Length.Should().Be(string.Empty);
        response.FinalMarkdown.Should().Be(string.Empty);
        response.KbSourceCount.Should().Be(0);
        response.LeafletSourceCount.Should().Be(0);
        response.DurationMs.Should().Be(0);
        response.CreatedAt.Should().Be(default(DateTimeOffset));
        response.UserId.Should().BeNull();
        response.PrecisionScore.Should().BeNull();
        response.StyleScore.Should().BeNull();
        response.FeedbackComment.Should().BeNull();
    }
}
```

- [ ] **Step 3: Run the new test to verify it compiles and passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetLeafletGenerationHandlerTests"`
Expected: build succeeds, `Passed! - Failed: 0, Passed: 1, Skipped: 0` (exactly one test found and passing — `Handle_returns_not_found_error_code_and_default_fields_when_generation_missing`).

If the build fails on a missing `using` or namespace mismatch, fix the `using` list against the actual namespaces of `ILeafletGenerationRepository` (`Anela.Heblo.Domain.Features.Leaflet`) and `GetLeafletGenerationHandler`/`GetLeafletGenerationRequest` (`Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletGeneration`) before re-running.

- [ ] **Step 4: Run the full backend test suite to confirm no regressions**

Run: `cd backend && dotnet test`
Expected: all tests pass (no new failures introduced; the suite's total passed count increases by exactly 1 versus the pre-change baseline).

- [ ] **Step 5: Format and build check**

Run: `cd backend && dotnet format && dotnet build`
Expected: `dotnet format` reports no changes needed (or applies only whitespace formatting to the new file — re-run `dotnet test` if it modifies anything); `dotnet build` succeeds with no errors.

- [ ] **Step 6: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/GetLeafletGenerationHandlerTests.cs
git commit -m "test: cover GetLeafletGenerationHandler not-found error path (#4176)"
```

---

## Self-Review

**Spec coverage:** FR-1 (the single functional requirement) is fully covered by this one task — the test asserts `Success == false`, `ErrorCode == LeafletFeedbackNotFound`, and every data property at its default, exactly as FR-1's acceptance criteria list. Out-of-scope items (success-path test, production code changes, other files' coverage) are correctly left untouched.

**Placeholder scan:** No TBD/TODO/"add appropriate handling" placeholders — the test file content above is complete and copy-pasteable; commands have concrete expected output.

**Type consistency:** `GetGenerationByIdAsync(Guid, CancellationToken)` return type `Task<LeafletGeneration?>` matches the interface signature confirmed during architecture review; the cast `(LeafletGeneration?)null` in the mock setup matches the sibling pattern (`(LeafletChunk?)null`) and the nullable reference type of the interface method. `GetLeafletGenerationRequest.Id` (`Guid`) and every asserted `GetLeafletGenerationResponse` property name/type match the DTOs read directly from source during analysis (`Id`, `Topic`, `Audience`, `Length`, `FinalMarkdown`, `KbSourceCount`, `LeafletSourceCount`, `DurationMs`, `CreatedAt`, `UserId`, `PrecisionScore`, `StyleScore`, `FeedbackComment`).
