using FluentValidation;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.SendMessage;

public class SendMessageValidator : AbstractValidator<SendMessageRequest>
{
    private const int MaxContentLength = 4000;

    public SendMessageValidator()
    {
        RuleFor(r => r.ConversationId).NotEmpty();
        RuleFor(r => r.Content)
            .NotEmpty()
            .When(r => r.Files.Count == 0);
        RuleFor(r => r.Content)
            .MaximumLength(MaxContentLength);
    }
}
