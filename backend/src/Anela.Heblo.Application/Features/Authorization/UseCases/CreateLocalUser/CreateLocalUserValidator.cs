using FluentValidation;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.CreateLocalUser;

public class CreateLocalUserValidator : AbstractValidator<CreateLocalUserRequest>
{
    public CreateLocalUserValidator()
    {
        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("Display name is required.")
            .MaximumLength(255);
    }
}
