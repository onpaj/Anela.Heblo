using Anela.Heblo.Application.Features.GiftSettings.Dto;
using Anela.Heblo.Domain.Features.Logistics.GiftSettings;
using MediatR;

namespace Anela.Heblo.Application.Features.GiftSettings.UseCases.GetGiftSetting;

public class GetGiftSettingHandler : IRequestHandler<GetGiftSettingQuery, GiftSettingDto>
{
    private readonly IGiftSettingRepository _repository;

    public GetGiftSettingHandler(IGiftSettingRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<GiftSettingDto> Handle(GetGiftSettingQuery request, CancellationToken cancellationToken)
    {
        var setting = await _repository.GetAsync(cancellationToken);
        return new GiftSettingDto
        {
            IsEnabled = setting.IsEnabled,
            ThresholdCzk = setting.ThresholdCzk,
            Text = setting.Text,
            ModifiedAt = setting.ModifiedAt,
            ModifiedBy = setting.ModifiedBy,
        };
    }
}
