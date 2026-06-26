using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.GiftSettings.UseCases.SetGiftSetting;
using Anela.Heblo.Domain.Features.Logistics.GiftSettings;
using Anela.Heblo.Persistence.Logistics.GiftSettings;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.GiftSettings;

public static class GiftSettingsModule
{
    public static IServiceCollection AddGiftSettingsModule(this IServiceCollection services)
    {
        services.AddScoped<IGiftSettingRepository, GiftSettingRepository>();
        services.AddScoped<IValidator<SetGiftSettingCommand>, SetGiftSettingValidator>();
        services.AddScoped<IPipelineBehavior<SetGiftSettingCommand, SetGiftSettingResponse>, ValidationBehavior<SetGiftSettingCommand, SetGiftSettingResponse>>();
        return services;
    }
}
