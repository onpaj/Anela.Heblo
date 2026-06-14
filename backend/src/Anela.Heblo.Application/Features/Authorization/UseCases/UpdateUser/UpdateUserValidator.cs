using FluentValidation;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.UpdateUser;

public class UpdateUserValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserValidator()
    {
        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("Display name is required.")
            .MaximumLength(255);

        RuleFor(x => x.Email)
            .MaximumLength(255)
            .EmailAddress().WithMessage("Email is not valid.")
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}
