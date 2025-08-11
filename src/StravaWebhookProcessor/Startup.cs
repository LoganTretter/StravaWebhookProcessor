using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: FunctionsStartup(typeof(StravaWebhookProcessor.Startup))]

namespace StravaWebhookProcessor;

public class Startup : FunctionsStartup
{
    public override void Configure(IFunctionsHostBuilder builder)
    {
        builder.Services.AddOptions<StravaWebhookProcessorOptions>()
            .Configure<IConfiguration>((settings, configuration) =>
            {
                configuration.GetSection(nameof(StravaWebhookProcessorOptions)).Bind(settings);
            });
    }
}