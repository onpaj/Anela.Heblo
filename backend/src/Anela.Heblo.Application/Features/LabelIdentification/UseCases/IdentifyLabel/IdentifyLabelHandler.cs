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
        catch (OperationCanceledException)
        {
            // Operator navigated away mid-request — not an OCR outage, let it propagate.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Label OCR service failed");
            return new IdentifyLabelResponse(ErrorCodes.LabelOcrServiceUnavailable);
        }

        if (string.IsNullOrWhiteSpace(rawText))
        {
            _logger.LogInformation("Label OCR returned no readable ingredients");
            return new IdentifyLabelResponse(ErrorCodes.LabelTextUnreadable);
        }

        var normalized = LabelTextNormalizer.Normalize(rawText);
        var match = _matcher.Match(normalized);

        // Bulk-fetch every referenced product in a single call to avoid N+1 DB queries
        // (up to 3 candidates x 2 variants would otherwise be up to 6 sequential lookups).
        var allCodes = match.Candidates.SelectMany(c => c.Codes).Distinct();
        var products = await _catalogRepository.GetByIdsAsync(allCodes, cancellationToken);

        var candidates = match.Candidates
            .Select(candidate => new LabelCandidateDto
            {
                Family = candidate.Family,
                Score = Math.Round(candidate.Score, 1),
                Variants = ResolveVariants(candidate.Codes, products),
            })
            .ToList();

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

    private static List<LabelVariantDto> ResolveVariants(
        IReadOnlyList<string> codes,
        IReadOnlyDictionary<string, CatalogAggregate> products)
    {
        return codes
            .Select(code =>
            {
                // A code missing from the catalogue still yields the code — that is the
                // answer the operator needs; the name is a convenience.
                products.TryGetValue(code, out var product);
                return new LabelVariantDto
                {
                    ProductCode = code,
                    ProductName = product?.ProductName ?? string.Empty,
                };
            })
            .ToList();
    }
}
