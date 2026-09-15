using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.CreateGiftPackageManufacture;

public class CreateGiftPackageManufactureHandler : IRequestHandler<CreateGiftPackageManufactureRequest, CreateGiftPackageManufactureResponse>
{
    private readonly IGiftPackageManufactureService _giftPackageService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<CreateGiftPackageManufactureHandler> _logger;

    public CreateGiftPackageManufactureHandler(
        IGiftPackageManufactureService giftPackageService,
        ICurrentUserService currentUserService,
        ILogger<CreateGiftPackageManufactureHandler> logger)
    {
        _giftPackageService = giftPackageService;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<CreateGiftPackageManufactureResponse> Handle(CreateGiftPackageManufactureRequest request, CancellationToken cancellationToken)
    {
        var user = _currentUserService.GetCurrentUser();

        try
        {
            var manufacture = await _giftPackageService.CreateManufactureAsync(
                request.GiftPackageCode,
                request.Quantity,
                request.AllowStockOverride,
                user.Name ?? "System",
                cancellationToken);

            return new CreateGiftPackageManufactureResponse
            {
                Manufacture = manufacture
            };
        }
        // Only the two rejections this handler owns are caught. A bare InvalidOperationException /
        // ArgumentException is deliberately left to bubble: EF Core raises both for tracking and
        // concurrency failures, and turning those into a 400 would hide real bugs behind a
        // "nedostatek zásob" toast.
        catch (InsufficientStockException ex)
        {
            _logger.LogWarning(ex, "Refused GiftPackageManufacture of {Quantity} x {GiftPackageCode} for {UserName}: insufficient warehouse stock",
                request.Quantity, request.GiftPackageCode, user.Name ?? "System");

            return Rejected(ErrorCodes.InvalidOperation, ex.Message);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            _logger.LogWarning(ex, "Refused GiftPackageManufacture of {Quantity} x {GiftPackageCode} for {UserName}: invalid quantity",
                request.Quantity, request.GiftPackageCode, user.Name ?? "System");

            // Not ex.Message: ArgumentOutOfRangeException appends "(Parameter 'quantity')" and the
            // actual value, which has no business in a user-facing toast.
            return Rejected(ErrorCodes.InvalidValue, InvalidQuantityMessage);
        }
    }

    private const string InvalidQuantityMessage = "Množství musí být větší než 0";

    private static CreateGiftPackageManufactureResponse Rejected(ErrorCodes errorCode, string message) =>
        new()
        {
            Success = false,
            ErrorCode = errorCode,
            Params = new Dictionary<string, string>
            {
                { "ErrorMessage", message }
            }
        };
}
