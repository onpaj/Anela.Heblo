using Anela.Heblo.Application.Features.CarrierCooling.UseCases.SetCarrierCooling;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Shared;
using FluentValidation.TestHelper;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Application.CarrierCooling;

public class SetCarrierCoolingValidatorTests
{
    private readonly Mock<IShippingMethodCatalog> _catalogMock = new();

    private SetCarrierCoolingValidator CreateValidator() => new(_catalogMock.Object);

    private void SetupCatalog(params (Carriers, DeliveryHandling)[] options)
    {
        _catalogMock.Setup(c => c.GetAvailableDeliveryOptions())
            .Returns(options.ToList().AsReadOnly());
    }

    [Fact]
    public void Validator_PassesForAvailableCombo()
    {
        SetupCatalog((Carriers.PPL, DeliveryHandling.NaRuky));
        var validator = CreateValidator();
        var request = new SetCarrierCoolingRequest
        {
            Carrier = Carriers.PPL,
            DeliveryHandling = DeliveryHandling.NaRuky,
            Cooling = Cooling.L1,
            ModifiedBy = "user-123",
        };

        var result = validator.TestValidate(request);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validator_FailsForUnavailableCarrierHandlingCombo()
    {
        SetupCatalog((Carriers.PPL, DeliveryHandling.NaRuky));
        var validator = CreateValidator();
        var request = new SetCarrierCoolingRequest
        {
            Carrier = Carriers.GLS,
            DeliveryHandling = DeliveryHandling.NaRuky,
            Cooling = Cooling.L1,
            ModifiedBy = "user-123",
        };

        var result = validator.TestValidate(request);

        result.ShouldHaveAnyValidationError();
    }

    [Fact]
    public void Validator_FailsForInvalidCarrierEnum()
    {
        SetupCatalog();
        var validator = CreateValidator();
        var request = new SetCarrierCoolingRequest
        {
            Carrier = (Carriers)999,
            DeliveryHandling = DeliveryHandling.NaRuky,
            Cooling = Cooling.L1,
            ModifiedBy = "user-123",
        };

        var result = validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.Carrier);
    }

    [Fact]
    public void Validator_FailsForInvalidDeliveryHandlingEnum()
    {
        SetupCatalog();
        var validator = CreateValidator();
        var request = new SetCarrierCoolingRequest
        {
            Carrier = Carriers.PPL,
            DeliveryHandling = (DeliveryHandling)999,
            Cooling = Cooling.L1,
            ModifiedBy = "user-123",
        };

        var result = validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.DeliveryHandling);
    }

    [Fact]
    public void Validator_FailsForInvalidCoolingEnum()
    {
        SetupCatalog((Carriers.PPL, DeliveryHandling.NaRuky));
        var validator = CreateValidator();
        var request = new SetCarrierCoolingRequest
        {
            Carrier = Carriers.PPL,
            DeliveryHandling = DeliveryHandling.NaRuky,
            Cooling = (Cooling)999,
            ModifiedBy = "user-123",
        };

        var result = validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.Cooling);
    }
}
