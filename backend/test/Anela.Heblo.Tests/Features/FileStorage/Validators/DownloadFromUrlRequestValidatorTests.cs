using Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl;
using Anela.Heblo.Application.Features.FileStorage.Validators;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using FluentValidation.TestHelper;
using Xunit;

namespace Anela.Heblo.Tests.Features.FileStorage.Validators;

public class DownloadFromUrlRequestValidatorTests
{
    private readonly DownloadFromUrlRequestValidator _validator;

    public DownloadFromUrlRequestValidatorTests()
    {
        _validator = new DownloadFromUrlRequestValidator();
    }

    private static DownloadFromUrlRequest CreateRequest(string containerName) =>
        new()
        {
            FileUrl = "https://example.com/file.txt",
            ContainerName = containerName,
        };

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("very-long-container-name-that-exceeds-sixty-three-characters-limit")]
    [InlineData("InvalidCase")]
    [InlineData("invalid--double-hyphen")]
    [InlineData("-starts-with-hyphen")]
    [InlineData("ends-with-hyphen-")]
    [InlineData("invalid_underscore")]
    public void ContainerName_Invalid_ShouldHaveValidationError(string invalidContainerName)
    {
        // Arrange
        var request = CreateRequest(invalidContainerName);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.ContainerName)
            .WithErrorMessage("Invalid container name");
    }

    [Theory]
    [InlineData("valid-container")]
    [InlineData("container123")]
    [InlineData("my-container-name")]
    [InlineData("abc")]
    [InlineData("container-with-exactly-sixty-three-characters-in-total-length")]
    public void ContainerName_Valid_ShouldNotHaveValidationError(string validContainerName)
    {
        // Arrange
        var request = CreateRequest(validContainerName);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.ContainerName);
    }

    [Fact]
    public void ContainerName_Invalid_ShouldHaveInvalidContainerNameErrorCode()
    {
        // Arrange
        var request = CreateRequest("INVALID_UPPERCASE");

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        var failure = result.Errors.Single(f => f.PropertyName == nameof(DownloadFromUrlRequest.ContainerName));
        failure.ErrorCode.Should().Be(((int)ErrorCodes.InvalidContainerName).ToString());
    }

    [Fact]
    public void ContainerName_Invalid_ErrorCodeRoundTrips_ToInvalidContainerName()
    {
        // Arrange
        var request = CreateRequest("INVALID_UPPERCASE");

        // Act
        var result = _validator.TestValidate(request);
        var failure = result.Errors.Single(f => f.PropertyName == nameof(DownloadFromUrlRequest.ContainerName));

        // Assert — the WithErrorCode string must round-trip through Enum.TryParse<ErrorCodes>
        // exactly to ErrorCodes.InvalidContainerName, per spec FR-1's acceptance criteria.
        var parsed = Enum.TryParse<ErrorCodes>(failure.ErrorCode, out var errorCode);
        parsed.Should().BeTrue();
        errorCode.Should().Be(ErrorCodes.InvalidContainerName);
    }

    [Fact]
    public void ContainerName_Invalid_ShouldHaveCorrectParams()
    {
        // Arrange
        var request = CreateRequest("INVALID_UPPERCASE");

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        var failure = result.Errors.Single(f => f.PropertyName == nameof(DownloadFromUrlRequest.ContainerName));
        var customState = failure.CustomState as Dictionary<string, string>;
        customState.Should().NotBeNull();
        customState.Should().ContainKey("containerName").WhoseValue.Should().Be("INVALID_UPPERCASE");
        customState.Should().ContainKey("cause").WhoseValue.Should().Be("validation");
    }
}
