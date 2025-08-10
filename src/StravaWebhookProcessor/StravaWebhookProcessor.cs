using Microsoft.Extensions.Logging;

namespace StravaIntegrationFunctions;

public class StravaWebhookProcessor
{
    private readonly ILogger _logger;

    public StravaWebhookProcessor(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<StravaWebhookProcessor>();
    }
}
