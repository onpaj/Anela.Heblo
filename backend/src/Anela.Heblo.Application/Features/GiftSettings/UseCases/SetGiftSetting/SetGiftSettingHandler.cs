using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics.GiftSettings;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.GiftSettings.UseCases.SetGiftSetting;

public sealed class SetGiftSettingHandler : IRequestHandler<SetGiftSettingCommand, SetGiftSettingResponse>
{
    private const int MaxTextLength = 50;

    private readonly IGiftSettingRepository _repository;
    private readonly ICurrentUserService _currentUserService;

    public SetGiftSettingHandler(IGiftSettingRepository repository, ICurrentUserService currentUserService)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
    }

    public async Task<SetGiftSettingResponse> Handle(SetGiftSettingCommand command, CancellationToken cancellationToken)
    {
        var currentUser = _currentUserService.GetCurrentUser();
        if (string.IsNullOrEmpty(currentUser.Id))
        {
            return new SetGiftSettingResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.Unauthorized,
            };
        }

        if (command.IsEnabled)
        {
            if (command.ThresholdCzk <= 0)
                return new SetGiftSettingResponse
                {
                    Success = false,
                    ErrorCode = ErrorCodes.ValidationError,
                    Params = new Dictionary<string, string> { { "message", "ThresholdCzk must be greater than zero when enabled." } },
                };

            if (string.IsNullOrEmpty(command.Text))
                return new SetGiftSettingResponse
                {
                    Success = false,
                    ErrorCode = ErrorCodes.ValidationError,
                    Params = new Dictionary<string, string> { { "message", "Text is required when enabled." } },
                };
        }

        if (command.Text?.Length > MaxTextLength)
            return new SetGiftSettingResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.ValidationError,
                Params = new Dictionary<string, string> { { "message", "Text cannot exceed 50 characters." } },
            };

        var setting = new GiftSetting(command.IsEnabled, command.ThresholdCzk, command.Text ?? string.Empty, currentUser.Id);
        await _repository.SaveAsync(setting, cancellationToken);
        return new SetGiftSettingResponse();
    }
}
