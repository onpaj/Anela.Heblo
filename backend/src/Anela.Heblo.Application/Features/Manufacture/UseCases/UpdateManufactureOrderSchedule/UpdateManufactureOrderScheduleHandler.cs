using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrderSchedule;

public class UpdateManufactureOrderScheduleHandler : IRequestHandler<UpdateManufactureOrderScheduleRequest, UpdateManufactureOrderScheduleResponse>
{
    private readonly IManufactureOrderRepository _repository;
    private readonly IMapper _mapper;
    private readonly ILogger<UpdateManufactureOrderScheduleHandler> _logger;

    public UpdateManufactureOrderScheduleHandler(
        IManufactureOrderRepository repository,
        IMapper mapper,
        ILogger<UpdateManufactureOrderScheduleHandler> logger)
    {
        _repository = repository;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<UpdateManufactureOrderScheduleResponse> Handle(UpdateManufactureOrderScheduleRequest request, CancellationToken cancellationToken)
    {
        try
        {
            // Fetch the existing order
            var existingOrder = await _repository.GetOrderByIdAsync(request.Id, cancellationToken);
            if (existingOrder == null)
            {
                _logger.LogWarning("Manufacture order with ID {OrderId} not found for schedule update", request.Id);
                return new UpdateManufactureOrderScheduleResponse(ErrorCodes.ResourceNotFound, "Manufacture order not found");
            }

            // Validate business rules
            var validationResponse = ValidateScheduleUpdate(existingOrder, request);
            if (validationResponse != null)
            {
                _logger.LogWarning("Schedule update validation failed for order {OrderId}: {ValidationError}",
                    request.Id, validationResponse.Message);
                return validationResponse;
            }

            // Update the schedule date
            bool hasChanges = false;

            if (request.PlannedDate.HasValue && request.PlannedDate != existingOrder.PlannedDate)
            {
                existingOrder.PlannedDate = request.PlannedDate.Value;
                hasChanges = true;
                _logger.LogInformation("Updated planned date for order {OrderId} to {NewDate}",
                    request.Id, request.PlannedDate.Value);
            }

            if (!hasChanges)
            {
                _logger.LogInformation("No schedule changes detected for order {OrderId}", request.Id);
                return new UpdateManufactureOrderScheduleResponse
                {
                    Message = "No changes were made to the schedule"
                };
            }


            // Save changes
            await _repository.UpdateOrderAsync(existingOrder, cancellationToken);

            _logger.LogInformation("Successfully updated schedule for manufacture order {OrderId}", request.Id);

            return new UpdateManufactureOrderScheduleResponse
            {
                Message = "Schedule updated successfully"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating schedule for manufacture order {OrderId}", request.Id);
            return new UpdateManufactureOrderScheduleResponse(ErrorCodes.InternalServerError,
                "An error occurred while updating the schedule");
        }
    }

    private static UpdateManufactureOrderScheduleResponse? ValidateScheduleUpdate(ManufactureOrder order, UpdateManufactureOrderScheduleRequest request)
    {
        // Can't update schedule for cancelled orders
        if (order.State == ManufactureOrderState.Cancelled)
        {
            return new UpdateManufactureOrderScheduleResponse(ErrorCodes.CannotUpdateCancelledOrder, "Cannot update schedule for cancelled orders");
        }

        // Can't update schedule for completed orders
        if (order.State == ManufactureOrderState.Completed)
        {
            return new UpdateManufactureOrderScheduleResponse(ErrorCodes.CannotUpdateCompletedOrder, "Cannot update schedule for completed orders");
        }

        // Don't allow scheduling in the past (with some tolerance for today)
        var today = DateOnly.FromDateTime(DateTime.Today);

        if (request.PlannedDate.HasValue && request.PlannedDate.Value < today)
        {
            return new UpdateManufactureOrderScheduleResponse(ErrorCodes.CannotScheduleInPast, "Cannot schedule manufacturing in the past");
        }

        // All validations passed
        return null;
    }
}