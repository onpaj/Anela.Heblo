using Anela.Heblo.Application.Features.Photobank.UseCases.GetPhotos;
using Anela.Heblo.Application.Features.Photobank.Validators;
using FluentValidation.TestHelper;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public class GetPhotosRequestValidatorTests
{
    private readonly GetPhotosRequestValidator _validator;

    public GetPhotosRequestValidatorTests()
    {
        _validator = new GetPhotosRequestValidator();
    }

    [Fact]
    public void Search_InvalidRegex_UseRegexTrue_FailsValidation()
    {
        // Arrange
        var request = new GetPhotosRequest
        {
            Search = "[unclosed",
            UseRegex = true,
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Search)
            .WithErrorMessage("Invalid regular expression pattern.");
    }

    [Fact]
    public void Search_ValidRegex_UseRegexTrue_PassesValidation()
    {
        // Arrange
        var request = new GetPhotosRequest
        {
            Search = @"^foo.*\.png$",
            UseRegex = true,
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.Search);
    }

    [Fact]
    public void Search_InvalidRegex_UseRegexFalse_PassesValidation()
    {
        // Arrange — flag off, regex pattern syntax is not checked
        var request = new GetPhotosRequest
        {
            Search = "[unclosed",
            UseRegex = false,
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Search_Null_UseRegexTrue_PassesValidation()
    {
        // Arrange — no pattern provided, nothing to validate
        var request = new GetPhotosRequest
        {
            Search = null,
            UseRegex = true,
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void FolderPath_InvalidRegex_UseFolderRegexTrue_FailsValidation()
    {
        // Arrange
        var request = new GetPhotosRequest
        {
            FolderPath = "[unclosed",
            UseFolderRegex = true,
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.FolderPath)
            .WithErrorMessage("Invalid regular expression pattern.");
    }

    [Fact]
    public void FolderPath_ValidRegex_UseFolderRegexTrue_PassesValidation()
    {
        // Arrange
        var request = new GetPhotosRequest
        {
            FolderPath = @"^Marketing/.*",
            UseFolderRegex = true,
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.FolderPath);
    }

    [Fact]
    public void FolderPath_InvalidRegex_UseFolderRegexFalse_PassesValidation()
    {
        // Arrange — flag off, regex pattern syntax is not checked
        var request = new GetPhotosRequest
        {
            FolderPath = "[unclosed",
            UseFolderRegex = false,
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void FolderPath_Null_UseFolderRegexTrue_PassesValidation()
    {
        // Arrange — no pattern provided, nothing to validate
        var request = new GetPhotosRequest
        {
            FolderPath = null,
            UseFolderRegex = true,
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }
}
