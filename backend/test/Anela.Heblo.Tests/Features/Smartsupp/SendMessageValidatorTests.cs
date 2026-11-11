using Anela.Heblo.Application.Features.Smartsupp.UseCases.SendMessage;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class SendMessageValidatorTests
{
    private readonly SendMessageValidator _validator = new();

    private static SendMessageFile File() =>
        new() { FileName = "a.png", Content = new byte[1] };

    [Fact]
    public void Validate_Fails_WhenTextEmptyAndNoFiles()
    {
        var result = _validator.Validate(new SendMessageRequest { ConversationId = "c", Content = "" });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_Succeeds_WhenTextEmptyAndOneFile()
    {
        var result = _validator.Validate(new SendMessageRequest
        {
            ConversationId = "c",
            Content = null,
            Files = [File()],
        });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Fails_WhenTextLongerThan4000()
    {
        var result = _validator.Validate(new SendMessageRequest
        {
            ConversationId = "c",
            Content = new string('x', 4001),
            Files = [File()],
        });

        result.IsValid.Should().BeFalse();
    }
}
