using Anela.Heblo.Application.Features.UserManagement.UseCases.GetGroupMembers;
using FluentValidation;

namespace Anela.Heblo.Application.Features.UserManagement.Validators;

public class GetGroupMembersRequestValidator : AbstractValidator<GetGroupMembersRequest>
{
    public GetGroupMembersRequestValidator()
    {
        RuleFor(x => x.GroupId)
            .Must(value => !string.IsNullOrWhiteSpace(value))
            .WithMessage("GroupId is required.");
    }
}
