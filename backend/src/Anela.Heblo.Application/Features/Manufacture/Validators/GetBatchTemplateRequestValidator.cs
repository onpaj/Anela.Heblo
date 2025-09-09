using Anela.Heblo.Application.Features.Manufacture.UseCases.GetBatchTemplate;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Manufacture.Validators;

public class GetBatchTemplateRequestValidator : AbstractValidator<GetBatchTemplateRequest>
{
    public GetBatchTemplateRequestValidator()
    {
        RuleFor(x => x.ProductCode)
            .NotEmpty()
            .WithMessage("Product code is required")
            .MaximumLength(50)
            .WithMessage("Product code cannot exceed 50 characters");
    }
}