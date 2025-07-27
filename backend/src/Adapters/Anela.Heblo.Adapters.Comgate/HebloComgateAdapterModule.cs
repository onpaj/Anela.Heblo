using Anela.Heblo.Bank;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Modularity;

namespace Anela.Heblo.Adapters.Comgate;

[DependsOn(
    typeof(HebloDomainModule)
    )]
public class HebloComgateAdapterModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();
        
        context.Services.AddHttpClient();
        //context.Services.Configure<DropBoxSourceOptions>(configuration.GetSection("Shoptet"));
        context.Services.Configure<ComgateSettings>(configuration.GetSection(ComgateSettings.ConfigurationKey));
        context.Services.AddSingleton(configuration.GetSection(ComgateSettings.ConfigurationKey).Get<ComgateSettings>()!);
        context.Services.Configure<BankAccountSettings>(configuration.GetSection(BankAccountSettings.ConfigurationKey));
        context.Services.AddSingleton(configuration.GetSection(BankAccountSettings.ConfigurationKey).Get<BankAccountSettings>()!);

        context.Services.AddTransient<IBankClient, ComgateBankClient>();
    }
}
