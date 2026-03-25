using Anela.Heblo.Xcc.Services.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SendGrid;

namespace Anela.Heblo.Adapters.SendGrid;

public static class SendGridAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddSendGridAdapter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<SendGridOptions>(
            configuration.GetSection(SendGridOptions.ConfigurationKey));

        var apiKey = configuration[$"{SendGridOptions.ConfigurationKey}:ApiKey"];
        services.AddSingleton<ISendGridClient>(new SendGridClient(apiKey ?? string.Empty));

        services.AddSingleton<IEmailSender, SendGridEmailSender>();

        return services;
    }
}
