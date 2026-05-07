using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.PackingMaterials;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.DeleteAllocation;

public class DeleteAllocationHandler : IRequestHandler<DeleteAllocationRequest, DeleteAllocationResponse>
{
    private readonly IPackingMaterialRepository _materialRepository;
    private readonly IPackingMaterialAllocationRepository _allocationRepository;
    private readonly ILogger<DeleteAllocationHandler> _logger;

    public DeleteAllocationHandler(
        IPackingMaterialRepository materialRepository,
        IPackingMaterialAllocationRepository allocationRepository,
        ILogger<DeleteAllocationHandler> logger)
    {
        _materialRepository = materialRepository;
        _allocationRepository = allocationRepository;
        _logger = logger;
    }

    public async Task<DeleteAllocationResponse> Handle(DeleteAllocationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var material = await _materialRepository.GetByIdWithAllocationsAsync(request.PackingMaterialId, cancellationToken);

            if (material == null)
                return new DeleteAllocationResponse { Success = false, ErrorCode = ErrorCodes.ResourceNotFound, Error = $"Packing material with ID {request.PackingMaterialId} not found." };

            var allocation = material.Allocations.FirstOrDefault(a => a.Id == request.AllocationId);
            if (allocation == null)
                return new DeleteAllocationResponse { Success = false, ErrorCode = ErrorCodes.ResourceNotFound, Error = $"Allocation with ID {request.AllocationId} not found on this material." };

            await _allocationRepository.DeleteAsync(allocation, cancellationToken);
            await _allocationRepository.SaveChangesAsync(cancellationToken);

            return new DeleteAllocationResponse { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting allocation {AllocationId} for packing material {PackingMaterialId}", request.AllocationId, request.PackingMaterialId);
            return new DeleteAllocationResponse { Success = false, Error = "An unexpected error occurred while deleting the allocation." };
        }
    }
}
