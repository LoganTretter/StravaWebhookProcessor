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
        services.AddSingleton<IStravaApiAthleteAuthInfoStorer, StravaApiAthleteAuthInfoKeyVaultStorer>();

        services.AddSingleton(serviceProvider =>
        {
            // FYI for local testing - need to run the "az login" cmd for this to work
            var config = serviceProvider.GetRequiredService<IConfiguration>();
            var keyVaultUri = config.GetValue<string>("KeyVaultUri");
            if (string.IsNullOrWhiteSpace(keyVaultUri))
            {
                throw new InvalidOperationException("KeyVaultUri setting is not configured.");
            }

            return new SecretClient(new Uri(keyVaultUri), new DefaultAzureCredential());
        });

        services.AddScoped(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<StravaWebhookProcessorOptions>>().Value;
            var stravaTokenStorer = serviceProvider.GetRequiredService<IStravaApiAthleteAuthInfoStorer>();
            return new StravaApiClient(options.StravaApiClientId, options.StravaApiClientSecret, stravaTokenStorer);
        });

        services.AddScoped<IStravaWebhookEventProcessor, StravaWebhookEventProcessor>();
    })
    .Build();

host.Run();
