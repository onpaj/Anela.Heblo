using MediatR;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using Anela.Heblo.Application.Features.InvoiceClassification.Contracts;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.GetClassificationHistory;

public class GetClassificationHistoryHandler : IRequestHandler<GetClassificationHistoryRequest, GetClassificationHistoryResponse>
{
    private readonly IClassificationHistoryRepository _historyRepository;
    private readonly ILogger<GetClassificationHistoryHandler> _logger;

    public GetClassificationHistoryHandler(
        IClassificationHistoryRepository historyRepository,
        ILogger<GetClassificationHistoryHandler> logger)
    {
        _historyRepository = historyRepository;
        _logger = logger;
    }

    public async Task<GetClassificationHistoryResponse> Handle(GetClassificationHistoryRequest request, CancellationToken cancellationToken)
    {
        var (historyItems, totalCount) = await _historyRepository.GetPagedHistoryAsync(
            request.Page,
            request.PageSize,
            request.FromDate,
            request.ToDate,
            request.InvoiceNumber,
            request.CompanyName);

        var historyDtos = historyItems.Select(history => new ClassificationHistoryDto
        {
            Id = history.Id,
            InvoiceId = history.AbraInvoiceId,
            InvoiceNumber = history.InvoiceNumber,
            InvoiceDate = history.InvoiceDate,
            CompanyName = history.CompanyName,
            Description = history.Description,
            ClassificationRuleId = history.ClassificationRuleId,
            RuleName = history.ClassificationRule?.Name,
            Result = history.Result,
            AccountingPrescription = history.AccountingPrescription,
            ErrorMessage = history.ErrorMessage,
            Timestamp = history.Timestamp,
            ProcessedBy = history.ProcessedBy
        }).ToList();

        return new GetClassificationHistoryResponse
        {
            Items = historyDtos,
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize
        };
    }
}