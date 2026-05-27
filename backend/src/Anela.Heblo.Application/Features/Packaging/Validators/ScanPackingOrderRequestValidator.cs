using Anela.Heblo.Application.Features.Packaging.UseCases.ScanPackingOrder;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Packaging.Validators;

public class ScanPackingOrderRequestValidator : AbstractValidator<ScanPackingOrderRequest>
{
    public ScanPackingOrderRequestValidator()
    {
        RuleFor(x => x.OrderCode).NotEmpty();
    }
}
