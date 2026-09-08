using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics.GiftSettings;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftSettings.UseCases.SetGiftSetting;

public sealed class SetGiftSettingHandler : IRequestHandler<SetGiftSettingCommand, SetGiftSettingResponse>
{
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

        var setting = new GiftSetting(command.IsEnabled, command.ThresholdCzk, command.Text ?? string.Empty, currentUser.Id);
        await _repository.SaveAsync(setting, cancellationToken);
        return new SetGiftSettingResponse();
    }
}
