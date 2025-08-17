using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenMeteoIntegration;
using StravaWebhookProcessor;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        services.AddOptions<StravaWebhookProcessorOptions>()
            .Configure<IConfiguration>((settings, configuration) =>
            {
                configuration.GetSection(nameof(StravaWebhookProcessorOptions)).Bind(settings);
            });

        services.AddSingleton<IOpenMeteoClient, OpenMeteoClient>();
        services.AddScoped<IStravaEventProcessor, StravaEventProcessor>();
    })
    .Build();

host.Run();
