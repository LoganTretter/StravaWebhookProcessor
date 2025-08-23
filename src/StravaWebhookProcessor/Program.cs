using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenMeteoIntegration;
using StravaUtilities;
using StravaWebhookProcessor;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        services.AddOptions<StravaWebhookProcessorOptions>()
            .Configure<IConfiguration>((settings, configuration) =>
            {
                configuration.GetSection(nameof(StravaWebhookProcessorOptions)).Bind(settings);
            })
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IOpenMeteoClient, OpenMeteoClient>();
        services.AddSingleton<IStravaApiTokenStorer, StravaApiTokenKeyVaultStorer>();

        services.AddSingleton(serviceProvider =>
        {
            // FYI for local testing - need to run the "az login" cmd for this to work
            var options = serviceProvider.GetRequiredService<IOptions<StravaWebhookProcessorOptions>>().Value;
            return new SecretClient(new Uri(options.KeyVaultUri), new DefaultAzureCredential());
        });

        services.AddScoped(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<StravaWebhookProcessorOptions>>().Value;
            var stravaTokenStorer = serviceProvider.GetRequiredService<IStravaApiTokenStorer>();
            return new StravaApiClient(options.StravaApiClientId, options.StravaApiClientSecret, stravaTokenStorer);
        });

        services.AddScoped<IStravaEventProcessor, StravaEventProcessor>();
    })
    .Build();

host.Run();
