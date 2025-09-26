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
            var validationResult = ValidateScheduleUpdate(existingOrder, request);
            if (!validationResult.IsValid)
            {
                _logger.LogWarning("Schedule update validation failed for order {OrderId}: {ValidationErrors}", 
                    request.Id, string.Join(", ", validationResult.Errors));
                return new UpdateManufactureOrderScheduleResponse(ErrorCodes.InvalidOperation, 
                    string.Join("; ", validationResult.Errors));
            }

            // Update the schedule dates
            bool hasChanges = false;
            
            if (request.SemiProductPlannedDate.HasValue && request.SemiProductPlannedDate != existingOrder.SemiProductPlannedDate)
            {
                existingOrder.SemiProductPlannedDate = request.SemiProductPlannedDate.Value;
                hasChanges = true;
                _logger.LogInformation("Updated semi-product planned date for order {OrderId} to {NewDate}", 
                    request.Id, request.SemiProductPlannedDate.Value);
            }

            if (request.ProductPlannedDate.HasValue && request.ProductPlannedDate != existingOrder.ProductPlannedDate)
            {
                existingOrder.ProductPlannedDate = request.ProductPlannedDate.Value;
                hasChanges = true;
                _logger.LogInformation("Updated product planned date for order {OrderId} to {NewDate}", 
                    request.Id, request.ProductPlannedDate.Value);
            }

            if (!hasChanges)
            {
                _logger.LogInformation("No schedule changes detected for order {OrderId}", request.Id);
                return new UpdateManufactureOrderScheduleResponse
                {
                    Message = "No changes were made to the schedule"
                };
            }

            // Add audit log entry
            var auditLog = new ManufactureOrderAuditLog
            {
                ManufactureOrderId = request.Id,
                Action = ManufactureOrderAuditAction.DateChanged,
                Timestamp = DateTime.UtcNow,
                User = "system", // TODO: Get from current user context
                Details = $"Semi-product: {request.SemiProductPlannedDate?.ToString() ?? "unchanged"}, " +
                         $"Product: {request.ProductPlannedDate?.ToString() ?? "unchanged"}"
            };
            
            existingOrder.AuditLog.Add(auditLog);

            // Save changes
            var updatedOrder = await _repository.UpdateOrderAsync(existingOrder, cancellationToken);
            
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

    private static ValidationResult ValidateScheduleUpdate(ManufactureOrder order, UpdateManufactureOrderScheduleRequest request)
    {
        var errors = new List<string>();

        // Can't update schedule for cancelled orders
        if (order.State == ManufactureOrderState.Cancelled)
        {
            errors.Add("Cannot update schedule for cancelled orders");
        }

        // Can't update schedule for completed orders
        if (order.State == ManufactureOrderState.Completed)
        {
            errors.Add("Cannot update schedule for completed orders");
        }

        // Semi-product date should be before or equal to product date
        if (request.SemiProductPlannedDate.HasValue && request.ProductPlannedDate.HasValue)
        {
            if (request.SemiProductPlannedDate.Value > request.ProductPlannedDate.Value)
            {
                errors.Add("Semi-product date cannot be after product date");
            }
        }

        // Don't allow scheduling in the past (with some tolerance for today)
        var today = DateOnly.FromDateTime(DateTime.Today);
        
        if (request.SemiProductPlannedDate.HasValue && request.SemiProductPlannedDate.Value < today)
        {
            errors.Add("Cannot schedule semi-product manufacturing in the past");
        }

        if (request.ProductPlannedDate.HasValue && request.ProductPlannedDate.Value < today)
        {
            errors.Add("Cannot schedule product manufacturing in the past");
        }

        return new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors
        };
    }

    private class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new();
    }
}