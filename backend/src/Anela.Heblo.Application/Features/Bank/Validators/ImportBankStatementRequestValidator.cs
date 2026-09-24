using Anela.Heblo.Application.Features.Bank.UseCases.ImportBankStatement;
using Anela.Heblo.Domain.Features.Bank;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Bank.Validators;

public class ImportBankStatementRequestValidator : AbstractValidator<ImportBankStatementRequest>
{
    public ImportBankStatementRequestValidator(IOptions<BankAccountSettings> bankSettings)
    {
        var settings = bankSettings.Value;

        RuleFor(x => x.AccountName)
            .NotEmpty().WithMessage("AccountName is required");

        RuleFor(x => x.AccountName)
            .Must(name => settings.Accounts.Any(a => a.Name == name))
            .WithMessage(x => $"Account name {x.AccountName} not found in {BankAccountSettings.ConfigurationKey} configuration.")
            .When(x => !string.IsNullOrEmpty(x.AccountName));

        RuleFor(x => x.DateFrom)
            .Must((req, dateFrom) => dateFrom.Date <= req.DateTo.Date)
            .WithMessage("DateFrom must not be later than DateTo");
    }
}
