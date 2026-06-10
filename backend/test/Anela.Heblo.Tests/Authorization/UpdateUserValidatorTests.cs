using Anela.Heblo.Application.Features.Authorization.UseCases.UpdateUser;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class UpdateUserValidatorTests
{
    private readonly UpdateUserValidator _validator = new();

    [Fact]
    public void Rejects_BlankDisplayName()
    {
        var result = _validator.Validate(new UpdateUserRequest { DisplayName = "", Email = "a@b.cz" });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Rejects_InvalidEmail()
    {
        var result = _validator.Validate(new UpdateUserRequest { DisplayName = "Name", Email = "not-an-email" });
        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Accepts_EmptyEmail(string? email)
    {
        var result = _validator.Validate(new UpdateUserRequest { DisplayName = "Name", Email = email });
        result.IsValid.Should().BeTrue();
    }
}
