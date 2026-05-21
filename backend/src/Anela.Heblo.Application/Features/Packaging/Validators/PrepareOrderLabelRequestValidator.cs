using Anela.Heblo.Application.Features.Packaging.UseCases.PrepareOrderLabel;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Packaging.Validators;

public class PrepareOrderLabelRequestValidator : AbstractValidator<PrepareOrderLabelRequest>
{
    public PrepareOrderLabelRequestValidator()
    {
        RuleFor(x => x.OrderCode).NotEmpty();
    }
}
