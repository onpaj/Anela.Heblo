using FluentValidation;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.GetOrderTrackingNumber;

public class GetOrderTrackingNumberRequestValidator : AbstractValidator<GetOrderTrackingNumberRequest>
{
    public GetOrderTrackingNumberRequestValidator()
    {
        RuleFor(x => x.OrderCode).NotEmpty();
    }
}
