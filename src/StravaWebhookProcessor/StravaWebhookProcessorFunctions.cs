using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text.Json;

namespace StravaWebhookProcessor;

public class StravaWebhookProcessor
{
    private readonly StravaWebhookProcessorOptions _options;
    private readonly IStravaWebhookEventProcessor _stravaEventProcessor;

    public StravaWebhookProcessor(IOptions<StravaWebhookProcessorOptions> options, IStravaWebhookEventProcessor stravaEventProcessor)
    {
        _options = options.Value;
        _stravaEventProcessor = stravaEventProcessor;
    }

    // On the basic Azure Function "consumption" plan, it may sort of "power down" the function app after some amount of inactive time (5-20 minutes is what I'm seeing)
    // This leads to "cold starts", where the next time it's triggered, it takes longer to start from that state
    // This timer function is just a cheap way to keep it warm / ready to go, since the main receiver needs to respond quickly, doesn't have time to start up on each event
    [Function(nameof(KeepWarmFunction))]
    public static void KeepWarmFunction(
        [TimerTrigger("0 */5 * * * *")] TimerInfo timer,
        FunctionContext executionContext)
    {
        var logger = executionContext.GetLogger(nameof(KeepWarmFunction));
        logger.LogDebug($"Executed {nameof(KeepWarmFunction)}");
    }

    [Function(nameof(StravaWebhookReceiver))]
    public async Task<HttpResponseData> StravaWebhookReceiver(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "")] HttpRequestData req,
        [DurableClient] DurableTaskClient client,
        FunctionContext executionContext)
    {
        var logger = executionContext.GetLogger(nameof(StravaWebhookReceiver));
        logger.LogDebug($"Executing {nameof(StravaWebhookReceiver)}");

        HttpResponseData? response;

        if (req.Method.Equals("get", StringComparison.OrdinalIgnoreCase))
        {
            // This GET is just used on the initial subscription creation
            response = await HandleSubscriptionChallenge(logger, req).ConfigureAwait(false);
        }
        else if (req.Method.Equals("post", StringComparison.OrdinalIgnoreCase))
        {
            // This POST is the actual webhook event
            response = await HandleWebhookEvent(logger, client, req).ConfigureAwait(false);
        }
        else
        {
            response = req.CreateResponse(HttpStatusCode.NotImplemented);
        }

        return response;
    }

    private async Task<HttpResponseData> HandleSubscriptionChallenge(ILogger logger, HttpRequestData req)
    {
        logger.LogDebug("Received subscription creation request");

        var queryParams = req.Query;
        var mode = queryParams.Get("hub.mode");
        var verificationToken = queryParams.Get("hub.verify_token");

        var expectedVerificationToken = _options.StravaWebhookSubscriptionVerificationToken;

        if (mode != "subscribe" || verificationToken != expectedVerificationToken)
            return req.CreateResponse(HttpStatusCode.Forbidden);

        var challenge = queryParams.Get("hub.challenge");
        if (string.IsNullOrEmpty(challenge))
            return req.CreateResponse(HttpStatusCode.BadRequest);

        logger.LogDebug("Received successful subscription creation request");

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new SubscriptionChallenge { Challenge = challenge }).ConfigureAwait(false);

        return response;
    }

    private async Task<HttpResponseData> HandleWebhookEvent(ILogger logger, DurableTaskClient durableTaskClient, HttpRequestData req)
    {
        logger.LogDebug($"Executing {nameof(HandleWebhookEvent)}");

        StravaWebhookEventContent? webhookContent = null;

        try
        {
            webhookContent = await req.ReadFromJsonAsync<StravaWebhookEventContent>();

            logger.LogDebug("Received: {webhookContent}", JsonSerializer.Serialize(webhookContent));
        }
        catch
        {
            try
            {
                var requestString = await req.ReadAsStringAsync().ConfigureAwait(false);
                logger.LogDebug("Failed to parse content, received string: {webhookContent}", requestString);
            }
            catch
            {
                logger.LogDebug("Failed to parse content, failed to even read as string");
            }

            return req.CreateResponse(HttpStatusCode.BadRequest);
        }

        if (webhookContent == null)
            return req.CreateResponse(HttpStatusCode.BadRequest);

        if (webhookContent.StravaWebhookEventType == StravaWebhookEventType.Unknown)
            return req.CreateResponse(HttpStatusCode.BadRequest);

        if (webhookContent.StravaWebhookObjectType == StravaWebhookObjectType.Unknown)
            return req.CreateResponse(HttpStatusCode.BadRequest);

        if (webhookContent.SubscriptionId != _options.StravaWebhookSubscriptionId)
        {
            logger.LogWarning("Subscription id on event is not the configured subscription id");
            return req.CreateResponse(HttpStatusCode.Forbidden);
        }

        if (webhookContent.AthleteId != _options.StravaAthleteId)
        {
            logger.LogWarning("Athlete id on event is not the configured athlete id");
            return req.CreateResponse(HttpStatusCode.OK);
        }

        if (webhookContent.StravaWebhookObjectType != StravaWebhookObjectType.Activity)
            return req.CreateResponse(HttpStatusCode.OK); // Only activity events are supported

        if (!_stravaEventProcessor.HandleEventType(webhookContent.StravaWebhookEventType))
            return req.CreateResponse(HttpStatusCode.OK);

        // Start a separate task to do the actual processing
        string instanceId = await durableTaskClient.ScheduleNewOrchestrationInstanceAsync(nameof(StravaWebhookOrchestrator), webhookContent).ConfigureAwait(false);

        var statusResponse = durableTaskClient.CreateCheckStatusResponse(req, instanceId);

        // And respond with success
        return req.CreateResponse(HttpStatusCode.OK);
    }

    [Function(nameof(StravaWebhookOrchestrator))]
    public async Task StravaWebhookOrchestrator(
        [OrchestrationTrigger] TaskOrchestrationContext context,
        FunctionContext executionContext)
    {
        var logger = executionContext.GetLogger(nameof(StravaWebhookOrchestrator));
        logger.LogDebug($"Executing {nameof(StravaWebhookOrchestrator)}");

        await context.CallActivityAsync<string>(nameof(StravaWebhookEventProcessor), context.GetInput<StravaWebhookEventContent>());
    }

    [Function(nameof(StravaWebhookEventProcessor))]
    public async Task StravaWebhookEventProcessor(
        [ActivityTrigger] StravaWebhookEventContent webhookContent,
        FunctionContext executionContext)
    {
        var logger = executionContext.GetLogger(nameof(StravaWebhookEventProcessor));
        logger.LogDebug($"Executing {nameof(StravaWebhookEventProcessor)}");

        await _stravaEventProcessor.ProcessActivityEvent(webhookContent.StravaWebhookEventType, logger, webhookContent.AthleteId, webhookContent.ObjectId).ConfigureAwait(false);
    }
}
