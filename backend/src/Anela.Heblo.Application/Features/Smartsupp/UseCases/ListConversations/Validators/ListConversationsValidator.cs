using FluentValidation;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ListConversations.Validators;

public class ListConversationsValidator : AbstractValidator<ListConversationsRequest>
{
    private static readonly string[] AllowedStatuses = ["Open", "Resolved"];

    public ListConversationsValidator()
    {
        RuleFor(r => r.Status)
            .NotEmpty()
            .Must(s => AllowedStatuses.Contains(s))
            .WithMessage("Status must be 'Open' or 'Resolved'.");
        RuleFor(r => r.Page).GreaterThanOrEqualTo(1);
        RuleFor(r => r.PageSize).InclusiveBetween(1, 200);
    }
}
