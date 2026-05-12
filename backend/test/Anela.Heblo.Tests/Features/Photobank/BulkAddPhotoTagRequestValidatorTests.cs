using System.Collections.Generic;
using Anela.Heblo.Application.Features.Photobank.UseCases.BulkAddPhotoTag;
using Anela.Heblo.Application.Features.Photobank.Validators;
using Anela.Heblo.Application.Shared;
using FluentValidation.TestHelper;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public class BulkAddPhotoTagRequestValidatorTests
{
    private readonly BulkAddPhotoTagRequestValidator _validator;

    public BulkAddPhotoTagRequestValidatorTests()
    {
        _validator = new BulkAddPhotoTagRequestValidator();
    }

    [Fact]
    public void NoFilters_FailsWithBulkTagFiltersRequiredErrorCode()
    {
        // Arrange
        var request = new BulkAddPhotoTagRequest
        {
            TagName = "flowers",
            Tags = null,
            Search = null,
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveAnyValidationError()
            .WithErrorCode(((int)ErrorCodes.BulkTagFiltersRequired).ToString());
    }

    [Fact]
    public void TagsListAllWhitespace_FailsWithBulkTagFiltersRequiredErrorCode()
    {
        // Arrange — Tags list present but all entries are whitespace
        var request = new BulkAddPhotoTagRequest
        {
            TagName = "flowers",
            Tags = new List<string> { "  ", "" },
            Search = null,
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveAnyValidationError()
            .WithErrorCode(((int)ErrorCodes.BulkTagFiltersRequired).ToString());
    }

    [Fact]
    public void SearchProvided_PassesFilterValidation()
    {
        // Arrange
        var request = new BulkAddPhotoTagRequest
        {
            TagName = "flowers",
            Search = "ruze",
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void NonEmptyTagsProvided_PassesFilterValidation()
    {
        // Arrange
        var request = new BulkAddPhotoTagRequest
        {
            TagName = "flowers",
            Tags = new List<string> { "summer" },
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TagNameEmptyOrWhitespace_FailsValidation(string tagName)
    {
        // Arrange
        var request = new BulkAddPhotoTagRequest
        {
            TagName = tagName,
            Search = "ruze",
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.TagName);
    }

    [Fact]
    public void TagNameTooLong_FailsValidation()
    {
        // Arrange
        var request = new BulkAddPhotoTagRequest
        {
            TagName = new string('a', 101),
            Search = "ruze",
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.TagName)
            .WithErrorMessage("TagName cannot exceed 100 characters");
    }
}
