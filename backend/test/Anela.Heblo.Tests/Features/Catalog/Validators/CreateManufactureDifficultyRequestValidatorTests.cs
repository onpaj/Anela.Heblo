using Anela.Heblo.Application.Features.Catalog.UseCases.CreateManufactureDifficulty;
using Anela.Heblo.Application.Features.Catalog.Validators;
using FluentValidation.TestHelper;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Validators;

public class CreateManufactureDifficultyRequestValidatorTests
{
    private readonly CreateManufactureDifficultyRequestValidator _validator;

    public CreateManufactureDifficultyRequestValidatorTests()
    {
        _validator = new CreateManufactureDifficultyRequestValidator();
    }

    private static CreateManufactureDifficultyRequest ValidRequest() => new()
    {
        ProductCode = "PROD001",
        DifficultyValue = 1,
        ValidFrom = null,
        ValidTo = null
    };

    // --- ProductCode (FR-2) ---

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ProductCode_NullOrEmpty_HasCorrectErrorMessage(string? productCode)
    {
        var request = ValidRequest();
        request.ProductCode = productCode!;

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.ProductCode)
            .WithErrorMessage("Product code is required");
    }

    [Fact]
    public void ProductCode_TypicalValue_PassesValidation()
    {
        var request = ValidRequest();
        request.ProductCode = "PROD001";

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.ProductCode);
    }

    [Fact]
    public void ProductCode_Exactly50Characters_PassesValidation()
    {
        var request = ValidRequest();
        request.ProductCode = new string('A', 50);

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.ProductCode);
    }

    [Fact]
    public void ProductCode_Exactly51Characters_HasCorrectErrorMessage()
    {
        var request = ValidRequest();
        request.ProductCode = new string('A', 51);

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.ProductCode)
            .WithErrorMessage("Product code cannot exceed 50 characters");
    }

    // --- DifficultyValue (FR-3) ---

    [Fact]
    public void DifficultyValue_Negative_HasCorrectErrorMessage()
    {
        var request = ValidRequest();
        request.DifficultyValue = -1;

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.DifficultyValue)
            .WithErrorMessage("Difficulty value must be non-negative");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void DifficultyValue_NonNegative_PassesValidation(int value)
    {
        var request = ValidRequest();
        request.DifficultyValue = value;

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.DifficultyValue);
    }

    // --- ValidFrom / ValidTo cross-field (FR-4) ---

    [Fact]
    public void ValidFromValidTo_FromBeforeTo_PassesValidation()
    {
        var request = ValidRequest();
        request.ValidFrom = new DateTime(2026, 1, 1);
        request.ValidTo = new DateTime(2026, 1, 2);

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.ValidFrom);
        result.ShouldNotHaveValidationErrorFor(x => x.ValidTo);
    }

    [Fact]
    public void ValidFromValidTo_Equal_HasCorrectErrorMessageOnBothFields()
    {
        var request = ValidRequest();
        var same = new DateTime(2026, 1, 1);
        request.ValidFrom = same;
        request.ValidTo = same;

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.ValidFrom)
            .WithErrorMessage("ValidFrom must be earlier than ValidTo");
        result.ShouldHaveValidationErrorFor(x => x.ValidTo)
            .WithErrorMessage("ValidTo must be later than ValidFrom");
    }

    [Fact]
    public void ValidFromValidTo_FromAfterTo_HasCorrectErrorMessageOnBothFields()
    {
        var request = ValidRequest();
        request.ValidFrom = new DateTime(2026, 1, 2);
        request.ValidTo = new DateTime(2026, 1, 1);

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.ValidFrom)
            .WithErrorMessage("ValidFrom must be earlier than ValidTo");
        result.ShouldHaveValidationErrorFor(x => x.ValidTo)
            .WithErrorMessage("ValidTo must be later than ValidFrom");
    }

    [Fact]
    public void ValidFromValidTo_OnlyFromSet_PassesValidation()
    {
        var request = ValidRequest();
        request.ValidFrom = new DateTime(2026, 1, 1);
        request.ValidTo = null;

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.ValidFrom);
        result.ShouldNotHaveValidationErrorFor(x => x.ValidTo);
    }

    [Fact]
    public void ValidFromValidTo_OnlyToSet_PassesValidation()
    {
        var request = ValidRequest();
        request.ValidFrom = null;
        request.ValidTo = new DateTime(2026, 1, 1);

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.ValidFrom);
        result.ShouldNotHaveValidationErrorFor(x => x.ValidTo);
    }

    [Fact]
    public void ValidFromValidTo_BothNull_PassesValidation()
    {
        var request = ValidRequest();
        request.ValidFrom = null;
        request.ValidTo = null;

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.ValidFrom);
        result.ShouldNotHaveValidationErrorFor(x => x.ValidTo);
    }

    // --- Whole request (FR-5) ---

    [Fact]
    public void ValidRequest_PassesAllValidation()
    {
        var request = new CreateManufactureDifficultyRequest
        {
            ProductCode = "PROD001",
            DifficultyValue = 1,
            ValidFrom = new DateTime(2026, 1, 1),
            ValidTo = new DateTime(2026, 1, 2)
        };

        var result = _validator.TestValidate(request);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }
}
