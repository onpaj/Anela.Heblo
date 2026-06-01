using Anela.Heblo.Application.Features.Photobank.UseCases.DeleteTag;
using Anela.Heblo.Application.Features.Photobank.Validators;
using FluentValidation.TestHelper;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public class DeleteTagRequestValidatorTests
{
    private readonly DeleteTagRequestValidator _validator;

    public DeleteTagRequestValidatorTests()
    {
        _validator = new DeleteTagRequestValidator();
    }

    [Fact]
    public void IdIsZero_FailsValidation()
    {
        // Arrange
        var request = new DeleteTagRequest { Id = 0 };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Id);
    }

    [Fact]
    public void IdIsNegative_FailsValidation()
    {
        // Arrange
        var request = new DeleteTagRequest { Id = -1 };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Id);
    }

    [Fact]
    public void IdIsPositive_PassesValidation()
    {
        // Arrange
        var request = new DeleteTagRequest { Id = 1 };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }
}
