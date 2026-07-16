using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Users;
using Anela.Heblo.Application.Shared;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.ResolveManualAction;

public class ResolveManualActionHandler : IRequestHandler<ResolveManualActionRequest, ResolveManualActionResponse>
{
    private readonly IManufactureOrderRepository _repository;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<ResolveManualActionHandler> _logger;
    private readonly TimeProvider _timeProvider;

    public ResolveManualActionHandler(
        IManufactureOrderRepository repository,
        ICurrentUserService currentUserService,
        ILogger<ResolveManualActionHandler> logger,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _currentUserService = currentUserService;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task<ResolveManualActionResponse> Handle(ResolveManualActionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var order = await _repository.GetOrderByIdAsync(request.OrderId, cancellationToken);

            if (order == null)
            {
                return new ResolveManualActionResponse(ErrorCodes.ResourceNotFound,
                    new Dictionary<string, string> { { "orderId", request.OrderId.ToString() } });
            }

            var currentUser = _currentUserService.GetCurrentUser();
            var userName = currentUser?.Name ?? "Unknown User";

            // Update ERP order numbers if provided
            if (!string.IsNullOrEmpty(request.ErpOrderNumberSemiproduct))
            {
                order.ErpOrderNumberSemiproduct = request.ErpOrderNumberSemiproduct;
            }

            if (!string.IsNullOrEmpty(request.ErpOrderNumberProduct))
            {
                order.ErpOrderNumberProduct = request.ErpOrderNumberProduct;
            }

            if (!string.IsNullOrEmpty(request.ErpDiscardResidueDocumentNumber))
            {
                order.ErpDiscardResidueDocumentNumber = request.ErpDiscardResidueDocumentNumber;
                order.ErpDiscardResidueDocumentNumberDate = _timeProvider.GetUtcNow().DateTime;
            }

            // Set ManualActionRequired to false
            order.ManualActionRequired = false;

            // Add note if provided
            if (!string.IsNullOrEmpty(request.Note))
            {
                order.Notes.Add(new ManufactureOrderNote
                {
                    Text = request.Note,
                    CreatedAt = _timeProvider.GetUtcNow().DateTime,
                    CreatedByUser = userName
                });
            }


            await _repository.UpdateOrderAsync(order, cancellationToken);

            _logger.LogInformation("Manual action resolved for order {OrderId} by user {UserName}",
                request.OrderId, userName);

            return new ResolveManualActionResponse();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving manual action for order {OrderId}", request.OrderId);
            return new ResolveManualActionResponse(ErrorCodes.InternalServerError);
        }
    }
}