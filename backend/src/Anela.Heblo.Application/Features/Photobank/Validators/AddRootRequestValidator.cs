using Anela.Heblo.Application.Features.Photobank.UseCases.AddRoot;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Photobank.Validators;

public class AddRootRequestValidator : AbstractValidator<AddRootRequest>
{
    public AddRootRequestValidator()
    {
        RuleFor(x => x.SharePointPath)
            .NotEmpty()
            .WithMessage("SharePointPath is required")
            .MaximumLength(1000)
            .WithMessage("SharePointPath cannot exceed 1000 characters");

        RuleFor(x => x.DriveId)
            .NotEmpty()
            .WithMessage("DriveId is required")
            .MaximumLength(200)
            .WithMessage("DriveId cannot exceed 200 characters");

        RuleFor(x => x.DisplayName)
            .MaximumLength(200)
            .WithMessage("DisplayName cannot exceed 200 characters")
            .When(x => x.DisplayName != null);
    }
}
