using Anela.Heblo.Domain.Features.Packaging;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.SetCarrierLabelOffset;

public class SetCarrierLabelOffsetValidator : AbstractValidator<SetCarrierLabelOffsetRequest>
{
    public SetCarrierLabelOffsetValidator()
    {
        RuleFor(x => x.Carrier)
            .Must(carrier => CarrierLabelOffset.LabelCarriers.Contains(carrier))
            .WithMessage("Carrier has no printed shipping label.");

        RuleFor(x => x.OffsetXMm)
            .InclusiveBetween(-CarrierLabelOffset.MaxOffsetMm, CarrierLabelOffset.MaxOffsetMm);

        RuleFor(x => x.OffsetYMm)
            .InclusiveBetween(-CarrierLabelOffset.MaxOffsetMm, CarrierLabelOffset.MaxOffsetMm);
    }
}
