namespace Anela.Heblo.Application.Features.LabelIdentification.UseCases.IdentifyLabel;

public class LabelCandidateDto
{
    public string Family { get; set; } = string.Empty;
    public double Score { get; set; }
    public List<LabelVariantDto> Variants { get; set; } = new();
}
