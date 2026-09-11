using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.DisassembleGiftPackage;

public class DisassembleGiftPackageHandler : IRequestHandler<DisassembleGiftPackageRequest, DisassembleGiftPackageResponse>
{
    private readonly IGiftPackageManufactureService _giftPackageService;
    private readonly ICurrentUserService _currentUserService;

    public DisassembleGiftPackageHandler(
        IGiftPackageManufactureService giftPackageService,
        ICurrentUserService currentUserService)
    {
        _giftPackageService = giftPackageService;
        _currentUserService = currentUserService;
    }

    public async Task<DisassembleGiftPackageResponse> Handle(DisassembleGiftPackageRequest request, CancellationToken cancellationToken)
    {
        var user = _currentUserService.GetCurrentUser();

        try
        {
            var disassembly = await _giftPackageService.DisassembleGiftPackageAsync(
                request.GiftPackageCode,
                request.Quantity,
                user.Name ?? "System",
                cancellationToken);

            return new DisassembleGiftPackageResponse
            {
                Disassembly = disassembly
            };
        }
        catch (InvalidOperationException ex)
        {
            return new DisassembleGiftPackageResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.InvalidOperation,
                Params = new Dictionary<string, string>
                {
                    { "ErrorMessage", ex.Message }
                }
            };
        }
        catch (ArgumentException ex)
        {
            return new DisassembleGiftPackageResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.InvalidValue,
                Params = new Dictionary<string, string>
                {
                    { "ErrorMessage", ex.Message }
                }
            };
        }
    }
}
