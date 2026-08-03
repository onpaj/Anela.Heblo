using Anela.Heblo.Application.Features.LabelIdentification.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.LabelIdentification.UseCases.IdentifyLabel;

public class IdentifyLabelHandler : IRequestHandler<IdentifyLabelRequest, IdentifyLabelResponse>
{
    private readonly ILabelOcrService _ocrService;
    private readonly ILabelMatcher _matcher;
    private readonly ICatalogRepository _catalogRepository;
    private readonly ILogger<IdentifyLabelHandler> _logger;

    public IdentifyLabelHandler(
        ILabelOcrService ocrService,
        ILabelMatcher matcher,
        ICatalogRepository catalogRepository,
        ILogger<IdentifyLabelHandler> logger)
    {
        _ocrService = ocrService;
        _matcher = matcher;
        _catalogRepository = catalogRepository;
        _logger = logger;
    }

    public async Task<IdentifyLabelResponse> Handle(
        IdentifyLabelRequest request,
        CancellationToken cancellationToken)
    {
        string rawText;
        try
        {
            rawText = await _ocrService.ReadIngredientsAsync(request.PhotoStream, cancellationToken);
        }
        catch (LabelOcrException ex)
        {
            _logger.LogWarning(ex, "Label photo could not be decoded");
            return new IdentifyLabelResponse(ErrorCodes.LabelPhotoUndecodable);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Label OCR service failed");
            return new IdentifyLabelResponse(ErrorCodes.ExternalServiceError);
        }

        if (string.IsNullOrWhiteSpace(rawText))
        {
            _logger.LogInformation("Label OCR returned no readable ingredients");
            return new IdentifyLabelResponse(ErrorCodes.LabelTextUnreadable);
        }

        var normalized = LabelTextNormalizer.Normalize(rawText);
        var match = _matcher.Match(normalized);

        var candidates = new List<LabelCandidateDto>();
        foreach (var candidate in match.Candidates)
        {
            candidates.Add(new LabelCandidateDto
            {
                Family = candidate.Family,
                Score = Math.Round(candidate.Score, 1),
                Variants = await ResolveVariantsAsync(candidate.Codes, cancellationToken),
            });
        }

        _logger.LogInformation(
            "Label identified as {Decision} with top family {Family}",
            match.Decision,
            candidates.FirstOrDefault()?.Family ?? "none");

        return new IdentifyLabelResponse
        {
            RawText = rawText,
            Decision = match.Decision,
            Candidates = candidates,
        };
    }

    private async Task<List<LabelVariantDto>> ResolveVariantsAsync(
        IReadOnlyList<string> codes,
        CancellationToken cancellationToken)
    {
        var variants = new List<LabelVariantDto>();
        foreach (var code in codes)
        {
            // A code missing from the catalogue still yields the code — that is the
            // answer the operator needs; the name is a convenience.
            var product = await _catalogRepository.GetByIdAsync(code, cancellationToken);
            variants.Add(new LabelVariantDto
            {
                ProductCode = code,
                ProductName = product?.ProductName ?? string.Empty,
            });
        }

        return variants;
    }
}
