using Anela.Heblo.Application.Features.Photobank.UseCases.CreateTag;
using Anela.Heblo.Application.Features.Photobank.Validators;
using FluentValidation.TestHelper;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public class CreateTagRequestValidatorTests
{
    private readonly CreateTagRequestValidator _validator;

    public CreateTagRequestValidatorTests()
    {
        _validator = new CreateTagRequestValidator();
    }

    [Fact]
    public void EmptyName_FailsValidation()
    {
        // Arrange
        var request = new CreateTagRequest { Name = "" };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void WhitespaceOnlyName_FailsValidation()
    {
        // Arrange
        var request = new CreateTagRequest { Name = "   " };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void NameExceedsMaxLength_FailsValidation()
    {
        // Arrange
        var request = new CreateTagRequest { Name = new string('a', 101) };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Name)
            .WithErrorMessage("Name cannot exceed 100 characters");
    }

    [Fact]
    public void ValidName_PassesValidation()
    {
        // Arrange
        var request = new CreateTagRequest { Name = "summer" };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }
}
