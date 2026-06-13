using Anela.Heblo.Application.Features.UserManagement.UseCases.GetGroupMembers;
using Anela.Heblo.Application.Features.UserManagement.Validators;
using FluentValidation.TestHelper;
using Xunit;

namespace Anela.Heblo.Tests.Features.UserManagement.Validators;

public class GetGroupMembersRequestValidatorTests
{
    private readonly GetGroupMembersRequestValidator _validator = new();

    [Fact]
    public void GroupId_Null_FailsValidation()
    {
        var request = new GetGroupMembersRequest { GroupId = null! };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.GroupId)
              .WithErrorMessage("GroupId is required.");
    }

    [Fact]
    public void GroupId_Empty_FailsValidation()
    {
        var request = new GetGroupMembersRequest { GroupId = string.Empty };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.GroupId)
              .WithErrorMessage("GroupId is required.");
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void GroupId_Whitespace_FailsValidation(string whitespace)
    {
        var request = new GetGroupMembersRequest { GroupId = whitespace };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.GroupId)
              .WithErrorMessage("GroupId is required.");
    }

    [Fact]
    public void GroupId_NonEmpty_PassesValidation()
    {
        var request = new GetGroupMembersRequest { GroupId = "11111111-2222-3333-4444-555555555555" };
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }
}
