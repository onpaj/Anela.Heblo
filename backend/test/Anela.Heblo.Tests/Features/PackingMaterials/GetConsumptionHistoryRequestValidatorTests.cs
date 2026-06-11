using Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetConsumptionHistory;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.PackingMaterials;

public class GetConsumptionHistoryRequestValidatorTests
{
    private readonly GetConsumptionHistoryRequestValidator _validator = new();

    [Fact]
    public void Validate_DefaultRequest_IsValid()
    {
        var result = _validator.Validate(new GetConsumptionHistoryRequest());
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Validate_PageSizeOutOfRange_IsInvalid(int pageSize)
    {
        var result = _validator.Validate(new GetConsumptionHistoryRequest { PageSize = pageSize });
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(GetConsumptionHistoryRequest.PageSize));
    }

    [Fact]
    public void Validate_PageNumberBelowOne_IsInvalid()
    {
        var result = _validator.Validate(new GetConsumptionHistoryRequest { PageNumber = 0 });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_MalformedDateFrom_IsInvalid()
    {
        var result = _validator.Validate(new GetConsumptionHistoryRequest { DateFrom = "10-01-2026" });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_WellFormedDates_IsValid()
    {
        var result = _validator.Validate(new GetConsumptionHistoryRequest { DateFrom = "2026-01-01", DateTo = "2026-01-31" });
        result.IsValid.Should().BeTrue();
    }
}
