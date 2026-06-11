using FluentValidation;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.AddGroupMember;

public class AddGroupMemberValidator : AbstractValidator<AddGroupMemberRequest>
{
    public AddGroupMemberValidator()
    {
        RuleFor(x => x.EntraObjectId).NotEmpty();
        RuleFor(x => x.Email).NotEmpty();
        RuleFor(x => x.DisplayName).NotEmpty();
    }
}
