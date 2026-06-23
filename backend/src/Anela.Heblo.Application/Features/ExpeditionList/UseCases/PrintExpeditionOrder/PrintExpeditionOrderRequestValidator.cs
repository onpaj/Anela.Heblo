using FluentValidation;

namespace Anela.Heblo.Application.Features.ExpeditionList.UseCases.PrintExpeditionOrder;

public class PrintExpeditionOrderRequestValidator : AbstractValidator<PrintExpeditionOrderRequest>
{
    public PrintExpeditionOrderRequestValidator()
    {
        RuleFor(x => x.OrderCode).NotEmpty();
    }
}
