using Anela.Heblo.Application.Features.Catalog.UseCases.SubmitStockTaking;
using FluentValidation.TestHelper;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Validators;

public class SubmitStockTakingRequestValidatorTests
{
    private readonly SubmitStockTakingRequestValidator _validator;

    public SubmitStockTakingRequestValidatorTests()
    {
        _validator = new SubmitStockTakingRequestValidator();
    }

    private static SubmitStockTakingRequest ValidRequest() => new()
    {
        ProductCode = "ABC123",
        TargetAmount = 500
    };

    [Theory]
    [InlineData(500)]
    [InlineData(99999)]
    [InlineData(0)]
    [InlineData(1)]
    public void TargetAmount_ValidValues_PassesValidation(decimal targetAmount)
    {
        var request = ValidRequest();
        request.TargetAmount = targetAmount;

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.TargetAmount);
    }

    [Theory]
    [InlineData(100001)]
    [InlineData(100000)]
    [InlineData(-1)]
    public void TargetAmount_InvalidValues_FailsValidation(decimal targetAmount)
    {
        var request = ValidRequest();
        request.TargetAmount = targetAmount;

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.TargetAmount);
    }

    [Fact]
    public void TargetAmount_ExceedsUpperBound_HasCorrectErrorMessage()
    {
        var request = ValidRequest();
        request.TargetAmount = 100001;

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.TargetAmount)
            .WithErrorMessage("Target amount must be less than 100,000");
    }

    [Fact]
    public void TargetAmount_AtUpperBoundExclusive_FailsValidation()
    {
        // 100000 itself must fail: the rule is LessThan(100000), i.e. exclusive upper bound.
        var request = ValidRequest();
        request.TargetAmount = 100000;

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.TargetAmount)
            .WithErrorMessage("Target amount must be less than 100,000");
    }

    [Fact]
    public void TargetAmount_JustBelowUpperBound_PassesValidation()
    {
        var request = ValidRequest();
        request.TargetAmount = 99999;

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.TargetAmount);
    }

    [Fact]
    public void TargetAmount_Negative_HasCorrectErrorMessage()
    {
        var request = ValidRequest();
        request.TargetAmount = -1;

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.TargetAmount)
            .WithErrorMessage("Target amount must be greater than or equal to 0");
    }

    [Fact]
    public void TargetAmount_Zero_PassesValidation()
    {
        // Lower bound is inclusive (GreaterThanOrEqualTo).
        var request = ValidRequest();
        request.TargetAmount = 0;

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.TargetAmount);
    }

    [Fact]
    public void TargetAmount_One_PassesValidation()
    {
        var request = ValidRequest();
        request.TargetAmount = 1;

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.TargetAmount);
    }

    [Fact]
    public void ProductCode_TypicalValue_PassesValidation()
    {
        var request = ValidRequest();
        request.ProductCode = "ABC123";

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
    public void ProductCode_Exceeds50Characters_HasCorrectErrorMessage()
    {
        var request = ValidRequest();
        request.ProductCode = new string('A', 51);

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.ProductCode)
            .WithErrorMessage("Product code cannot exceed 50 characters");
    }

    [Fact]
    public void ValidRequest_PassesAllValidation()
    {
        var request = new SubmitStockTakingRequest
        {
            ProductCode = "ABC123",
            TargetAmount = 500
        };

        var result = _validator.TestValidate(request);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }
}
