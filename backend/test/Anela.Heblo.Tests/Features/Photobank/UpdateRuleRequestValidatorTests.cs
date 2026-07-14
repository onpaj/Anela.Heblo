using Anela.Heblo.Application.Features.Photobank.UseCases.UpdateRule;
using Anela.Heblo.Application.Features.Photobank.Validators;
using FluentValidation.TestHelper;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public class UpdateRuleRequestValidatorTests
{
    private readonly UpdateRuleRequestValidator _validator;

    public UpdateRuleRequestValidatorTests()
    {
        _validator = new UpdateRuleRequestValidator();
    }

    private static UpdateRuleRequest ValidRequest()
    {
        return new UpdateRuleRequest
        {
            Id = 1,
            PathPattern = "^[a-z]+\\.png$",
            TagName = "summer",
            SortOrder = 0,
            IsActive = true,
        };
    }

    [Fact]
    public void ValidRequest_PassesValidation()
    {
        // Arrange
        var request = ValidRequest();

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Id_NotPositive_FailsValidation(int id)
    {
        // Arrange
        var request = ValidRequest();
        request.Id = id;

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Id)
            .WithErrorMessage("Id must be a positive integer");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void PathPattern_Empty_FailsValidation(string? pathPattern)
    {
        // Arrange
        var request = ValidRequest();
        request.PathPattern = pathPattern!;

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.PathPattern)
            .WithErrorMessage("PathPattern is required");
    }

    [Fact]
    public void PathPattern_TooLong_FailsValidation()
    {
        // Arrange — 501 benign chars, always a valid regex, so only the length rule fires
        var request = ValidRequest();
        request.PathPattern = new string('a', 501);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.PathPattern)
            .WithErrorMessage("PathPattern cannot exceed 500 characters");
    }

    [Fact]
    public void PathPattern_MaxLength_PassesValidation()
    {
        // Arrange — boundary case, exactly 500 chars
        var request = ValidRequest();
        request.PathPattern = new string('a', 500);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.PathPattern);
    }

    [Fact]
    public void PathPattern_InvalidRegex_FailsValidation()
    {
        // Arrange
        var request = ValidRequest();
        request.PathPattern = "[";

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.PathPattern)
            .WithErrorMessage("Invalid regular expression pattern.");
    }

    [Fact]
    public void PathPattern_ValidRegex_PassesValidation()
    {
        // Arrange
        var request = ValidRequest();
        request.PathPattern = "^[a-z]+";

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.PathPattern);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void TagName_Empty_FailsValidation(string? tagName)
    {
        // Arrange
        var request = ValidRequest();
        request.TagName = tagName!;

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.TagName)
            .WithErrorMessage("TagName is required");
    }

    [Fact]
    public void TagName_TooLong_FailsValidation()
    {
        // Arrange
        var request = ValidRequest();
        request.TagName = new string('a', 101);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.TagName)
            .WithErrorMessage("TagName cannot exceed 100 characters");
    }

    [Fact]
    public void SortOrder_Negative_FailsValidation()
    {
        // Arrange
        var request = ValidRequest();
        request.SortOrder = -1;

        // Act
        var result = _validator.TestValidate(request);

        // Assert — no custom .WithMessage() on this rule, only assert the error exists
        result.ShouldHaveValidationErrorFor(x => x.SortOrder);
    }

    [Fact]
    public void SortOrder_Zero_PassesValidation()
    {
        // Arrange — boundary case
        var request = ValidRequest();
        request.SortOrder = 0;

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.SortOrder);
    }
}
