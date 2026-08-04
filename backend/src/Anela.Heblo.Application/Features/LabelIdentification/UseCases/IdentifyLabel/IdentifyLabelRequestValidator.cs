using FluentValidation;

namespace Anela.Heblo.Application.Features.LabelIdentification.UseCases.IdentifyLabel;

public class IdentifyLabelRequestValidator : AbstractValidator<IdentifyLabelRequest>
{
    public IdentifyLabelRequestValidator()
    {
        RuleFor(x => x.SizeBytes).GreaterThan(0);
        RuleFor(x => x.ContentType)
            .Must(ct => ct.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Uploaded file must be an image.");
    }
}
