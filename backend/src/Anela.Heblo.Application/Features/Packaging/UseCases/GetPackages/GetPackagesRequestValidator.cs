using FluentValidation;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.GetPackages;

public class GetPackagesRequestValidator : AbstractValidator<GetPackagesRequest>
{
    public GetPackagesRequestValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}
