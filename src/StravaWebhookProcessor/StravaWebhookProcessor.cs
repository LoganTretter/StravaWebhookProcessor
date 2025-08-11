using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenMeteoIntegration;
using StravaUtilities;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StravaWebhookProcessor;

public class StravaWebhookProcessor
{
    private readonly StravaWebhookProcessorOptions _options;

    private static SecretClient? _secretClient;

    public StravaWebhookProcessor(IOptions<StravaWebhookProcessorOptions> options)
    {
        _options = options.Value;

        var optionsValidationMessage = "";

        if (string.IsNullOrEmpty(_options.KeyVaultUri))
            optionsValidationMessage += $"{nameof(StravaWebhookProcessorOptions.KeyVaultUri)} is missing or blank in options. ";
        if (_options.StravaAthleteId <= 0)
            optionsValidationMessage += $"{nameof(StravaWebhookProcessorOptions.StravaAthleteId)} is missing or invalid in options (expect a long). ";
        if (_options.StravaWebhookSubscriptionId <= 0)
            optionsValidationMessage += $"{nameof(StravaWebhookProcessorOptions.StravaWebhookSubscriptionId)} is missing or invalid in options (expect a long). ";
        if (string.IsNullOrEmpty(_options.StravaWebhookSubscriptionVerificationToken))
            optionsValidationMessage += $"{nameof(StravaWebhookProcessorOptions.StravaWebhookSubscriptionVerificationToken)} is missing or blank in options. ";

        if (optionsValidationMessage != "")
            throw new ArgumentException(optionsValidationMessage);
    }

    [Function(nameof(KeepWarmFunction))]
    public static void KeepWarmFunction(
        [TimerTrigger("0 */10 * * * *")] TimerInfo timer,
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
        logger.LogDebug($"Executing {nameof(StravaWebhookReceiver)}.");

        HttpResponseData response;

        // This GET is just used on the initial subscription creation
        if (req.Method.Equals("get", StringComparison.OrdinalIgnoreCase))
        {
            var queryParams = req.Query;
            var mode = queryParams.Get("hub.mode");
            var verificationToken = queryParams.Get("hub.verify_token");

            var expectedVerificationToken = _options.StravaWebhookSubscriptionVerificationToken;

            if (mode != "subscribe" || verificationToken != expectedVerificationToken)
                return req.CreateResponse(HttpStatusCode.Forbidden);

            var challenge = queryParams.Get("hub.challenge");
            if (string.IsNullOrEmpty(challenge))
                return req.CreateResponse(HttpStatusCode.BadRequest);

            logger.LogDebug("Received successful subscription creation request.");

            response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new SubscriptionChallenge { Challenge = challenge }).ConfigureAwait(false);
        }
        // This POST is the actual webhook event
        else if (req.Method.Equals("post", StringComparison.OrdinalIgnoreCase))
        {
            var webhookContent = await req.ReadFromJsonAsync<WebhookContent>();

            if (webhookContent == null) // TODO plus other checks?
                return req.CreateResponse(HttpStatusCode.BadRequest);

            if (webhookContent.SubscriptionId != _options.StravaWebhookSubscriptionId)
                return req.CreateResponse(HttpStatusCode.Forbidden);

            // TODO these should just go to different methods that I would just not implement
            // but that has the downside of the overhead of the durable function, as opposed to stopping early
            if (webhookContent.AthleteId != _options.StravaAthleteId)
                return req.CreateResponse(HttpStatusCode.OK);

            if (webhookContent.ObjectType == "athlete")
                return req.CreateResponse(HttpStatusCode.OK);

            if (webhookContent.EventType == "delete" || webhookContent.EventType == "update")
                return req.CreateResponse(HttpStatusCode.OK);


            string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(nameof(StravaWebhookOrchestrator), webhookContent).ConfigureAwait(false);

            var statusResponse = client.CreateCheckStatusResponse(req, instanceId);

            response = req.CreateResponse(HttpStatusCode.OK);
        }
        else
        {
            response = req.CreateResponse(HttpStatusCode.NotImplemented);
        }

        return response;
    }

    [Function(nameof(StravaWebhookOrchestrator))]
    public async Task StravaWebhookOrchestrator(
        [OrchestrationTrigger] TaskOrchestrationContext context,
        FunctionContext executionContext)
    {
        var logger = executionContext.GetLogger(nameof(StravaWebhookOrchestrator));
        logger.LogDebug($"Executing {nameof(StravaWebhookOrchestrator)}.");

        await context.CallActivityAsync<string>(nameof(StravaWebhookOrchestrator), context.GetInput<WebhookContent>());
    }

    [Function(nameof(StravaWebhookEventProcessor))]
    public async Task StravaWebhookEventProcessor(
        [ActivityTrigger] WebhookContent webhookContent,
        FunctionContext executionContext)
    {
        // Ideas:
        // Get better weather info and add to description.
        //   Starting / ending or avg conditions, compared to Strava's start only.
        //   Get closest couple of weather stations to the start/end and avg them for the result.

        var logger = executionContext.GetLogger(nameof(StravaWebhookEventProcessor));

        logger.LogDebug($"Executing StravaWebhookProcessor. Received: {JsonSerializer.Serialize(webhookContent)}");


        if (webhookContent.AthleteId != _options.StravaAthleteId)
            return;

        if (webhookContent.ObjectType == "athlete")
            return;

        if (webhookContent.EventType == "delete" || webhookContent.EventType == "update")
            return;


        StravaApiToken? tokenFromVault = null;

        _secretClient ??= new(new Uri(_options.KeyVaultUri), new DefaultAzureCredential());
        var _stravaApiClient = new StravaApiClient();

        if (_stravaApiClient.Token == null)
        {
            logger.LogDebug("Running initial authentication with Strava.");

            var secret = (await _secretClient.GetSecretAsync("StravaApiToken").ConfigureAwait(false)).Value;
            if (secret == null)
                throw new ApplicationException("StravaApiToken Secret was null.");

            var secretValue = secret.Value;
            if (string.IsNullOrEmpty(secretValue))
                throw new ApplicationException("StravaApiToken secret value was null or empty.");

            tokenFromVault = JsonSerializer.Deserialize<StravaApiToken>(secretValue);
            if (tokenFromVault == null)
                throw new ApplicationException("tokenFromVault was null.");
            if (string.IsNullOrEmpty(tokenFromVault.AccessToken))
                throw new ApplicationException("tokenFromVault.AccessToken was null or empty.");
            if (string.IsNullOrEmpty(tokenFromVault.RefreshToken))
                throw new ApplicationException("tokenFromVault.RefreshToken was null or empty.");

            var clientId = (await _secretClient.GetSecretAsync("StravaApiClientId").ConfigureAwait(false)).Value.Value;
            var clientSecret = (await _secretClient.GetSecretAsync("StravaApiClientSecret").ConfigureAwait(false)).Value.Value;

            await _stravaApiClient.Authenticate(tokenFromVault, clientId, clientSecret).ConfigureAwait(false);
        }

        if (_stravaApiClient.Token == null)
        {
            throw new ApplicationException("Tried to authenticate with Strava but Token is still null.");
        }

        try
        {
            if (webhookContent.EventType == "create")
            {
                logger.LogDebug($"Processing event creation event for activity id {webhookContent.ObjectId}.");

                var activity = await _stravaApiClient.GetActivity(webhookContent.ObjectId).ConfigureAwait(false);

                // Indicator that this activity has already been processed. Can't set private notes so using description.
                if (activity.Description != null && activity.Description.Contains("~~"))
                    return;

                if (activity.Name != null && (activity.Name.StartsWith("Treadmill Hike") || activity.Name.StartsWith("Treadmill Run") || activity.Name.StartsWith("General Activity")))
                    return;

                var updateInfo = new ActivityUpdateInfo
                {
                    ActivityId = activity.Id,
                    Description = "~~"
                };

                // I want to upload treadmill activities through my augmentor app
                // Can't delete an activity through Strava API so going to mark them in a way to tell me I need to delete them
                if (activity.SportType == ActivityType.Walk && activity.Name != null && !activity.Name.Equals("Treadmill Hike"))
                {
                    // The SummaryPolyline is blank if the entire walk is within privacy zone..
                    // Lowest elevation seems to still come through though so if it's like ~270 that probably means it was outside
                    //  Edit - seems to come through even on indoor walks ugh!
                    // The Trainer flag seems to be true though for indoor ones, will try that
                    // Could also maybe use avg HR - unlikely to be below 110 for my hikes
                    if (!string.IsNullOrEmpty(activity.Map?.SummaryPolyline) || !activity.Trainer)
                    {
                        logger.LogDebug("Updating walk activity with map - hiding from feed.");

                        updateInfo.SuppressFromFeed = true;
                    }
                    else
                    {
                        logger.LogDebug("Updating walk activity without map - marking as treadmill hike to be refined.");

                        updateInfo.Name = "Treadmill Hike ~";
                        updateInfo.Description = "~~\nto be refined";
                        updateInfo.SportType = ActivityType.Hike;
                        updateInfo.SuppressFromFeed = true;
                    }
                }
                else if (activity.SportType == ActivityType.Run && string.IsNullOrEmpty(activity.Map?.SummaryPolyline) && activity.Name != null && !activity.Name.Equals("Treadmill Run"))
                {
                    logger.LogDebug("Updating run activity without map - marking as treadmill run to be refined.");

                    updateInfo.Name = "Treadmill Run ~";
                    updateInfo.Description = "~~\nto be refined";
                    updateInfo.SuppressFromFeed = true;
                }
                else if ((activity.SportType == ActivityType.Run || activity.SportType == ActivityType.TrailRun || activity.SportType == ActivityType.Hike) && activity.StartLocation != null && activity.EndLocation != null)
                {
                    await UpdateDescriptionWithWeatherInfo(activity).ConfigureAwait(false);
                    updateInfo.Description = activity.Description;
                }
                else if (activity.SportType == ActivityType.WeightTraining)
                {
                    logger.LogDebug("Updating weight training activity - setting properties and hiding from feed.");

                    updateInfo.Name = "Strength Training";
                    updateInfo.SportType = ActivityType.WeightTraining;
                    updateInfo.SuppressFromFeed = true;
                }
                else if (activity.SportType == ActivityType.Workout && string.IsNullOrEmpty(activity.Map?.SummaryPolyline))
                {
                    logger.LogDebug("Updating workout activity without map - setting properties and hiding from feed.");

                    updateInfo.Name = "General Activity";
                    updateInfo.SportType = ActivityType.Workout;
                    updateInfo.SuppressFromFeed = true;
                }
                else
                {
                    return;
                }

                await _stravaApiClient.UpdateActivity(updateInfo).ConfigureAwait(false);
            }
        }
        finally
        {
            if (tokenFromVault != null && tokenFromVault.AccessToken != _stravaApiClient.Token.AccessToken)
            {
                logger.LogDebug("Updating token in key vault.");

                await _secretClient.SetSecretAsync("StravaApiToken", JsonSerializer.Serialize(_stravaApiClient.Token)).ConfigureAwait(false);
            }
        }
    }

    private static async Task UpdateDescriptionWithWeatherInfo(Activity activity)
    {
        activity.Description += "~~";

        // TODO handle different start and end locations. Should be fine for now since usually it's same.

        var startTime = activity.StartDate;
        var middleTime = startTime.AddSeconds(activity.ElapsedSeconds / 2);
        var endTime = startTime.AddSeconds(activity.ElapsedSeconds);

        var openMeteoClient = new OpenMeteoClient();
        var input = new GetWeatherInput()
        {
            Start = startTime,
            End = endTime,
            Latitude = activity.StartLocation.Latitude,
            Longitude = activity.StartLocation.Longitude
        };
        var output = await openMeteoClient.GetWeatherData(input).ConfigureAwait(false);

        if (output?.OutputDetails == null || !output.OutputDetails.Any())
            return;

        activity.Description += " \nWeather: ";

        // Less than 1 hour: just average all the data points in between
        if (activity.ElapsedSeconds < 3600)
        {
            var detailsInBetween = output.OutputDetails.Where(d => startTime <= d.OutputDetailTime && d.OutputDetailTime <= endTime).ToList();
            if (!detailsInBetween.Any())
            {
                var closestToStart = output.OutputDetails.MinBy(d => Math.Abs(startTime.Ticks - d.OutputDetailTime.Ticks));
                if (closestToStart == null)
                    return;

                detailsInBetween.Add(closestToStart);
            }

            var avg = GetAverageOfMultipleDetails(detailsInBetween);

            activity.Description += FormatWeatherSummary(avg);
        }
        // Greater than 1 hour but less than 3: show start and end
        else if (activity.ElapsedSeconds < 10800)
        {
            activity.Description += "\n";

            var firstDetailBeforeStart = output.OutputDetails.Where(d => d.OutputDetailTime <= startTime).MinBy(d => Math.Abs(startTime.Ticks - d.OutputDetailTime.Ticks));
            var firstDetailAfterStart = output.OutputDetails.Where(d => d.OutputDetailTime >= startTime).MinBy(d => Math.Abs(startTime.Ticks - d.OutputDetailTime.Ticks));
            var startDetails = new[] { firstDetailBeforeStart, firstDetailAfterStart };
            var startAvg = GetAverageOfMultipleDetails(startDetails);
            activity.Description += "Start: " + FormatWeatherSummary(startAvg);

            var firstDetailBeforeEnd = output.OutputDetails.Where(d => d.OutputDetailTime <= endTime).MinBy(d => Math.Abs(endTime.Ticks - d.OutputDetailTime.Ticks));
            var firstDetailAfterEnd = output.OutputDetails.Where(d => d.OutputDetailTime >= endTime).MinBy(d => Math.Abs(endTime.Ticks - d.OutputDetailTime.Ticks));
            var endDetails = new[] { firstDetailBeforeEnd, firstDetailAfterEnd };
            var endAvg = GetAverageOfMultipleDetails(endDetails);
            activity.Description += " \nEnd: " + FormatWeatherSummary(endAvg);
        }
        // Greater than 3 hours: show start, middle and end
        else
        {
            activity.Description += "\n";

            var firstDetailBeforeStart = output.OutputDetails.Where(d => d.OutputDetailTime <= startTime).MinBy(d => Math.Abs(startTime.Ticks - d.OutputDetailTime.Ticks));
            var firstDetailAfterStart = output.OutputDetails.Where(d => d.OutputDetailTime >= startTime).MinBy(d => Math.Abs(startTime.Ticks - d.OutputDetailTime.Ticks));
            var startDetails = new[] { firstDetailBeforeStart, firstDetailAfterStart };
            var startAvg = GetAverageOfMultipleDetails(startDetails);
            activity.Description += "Start: " + FormatWeatherSummary(startAvg);

            var firstDetailBeforeMiddle = output.OutputDetails.Where(d => d.OutputDetailTime <= middleTime).MinBy(d => Math.Abs(middleTime.Ticks - d.OutputDetailTime.Ticks));
            var firstDetailAfterMiddle = output.OutputDetails.Where(d => d.OutputDetailTime >= middleTime).MinBy(d => Math.Abs(middleTime.Ticks - d.OutputDetailTime.Ticks));
            var middleDetails = new[] { firstDetailBeforeMiddle, firstDetailAfterMiddle };
            var middleAvg = GetAverageOfMultipleDetails(middleDetails);
            activity.Description += " \nMiddle: " + FormatWeatherSummary(middleAvg);

            var firstDetailBeforeEnd = output.OutputDetails.Where(d => d.OutputDetailTime <= endTime).MinBy(d => Math.Abs(endTime.Ticks - d.OutputDetailTime.Ticks));
            var firstDetailAfterEnd = output.OutputDetails.Where(d => d.OutputDetailTime >= endTime).MinBy(d => Math.Abs(endTime.Ticks - d.OutputDetailTime.Ticks));
            var endDetails = new[] { firstDetailBeforeEnd, firstDetailAfterEnd };
            var endAvg = GetAverageOfMultipleDetails(endDetails);
            activity.Description += " \nEnd: " + FormatWeatherSummary(endAvg);
        }
    }

    private static string FormatWeatherSummary(OutputDetail detail)
    {
        string descFormat = "{0}F, Dew {1}F, Hum {2}%, Wind {3}mph {4} (gust {5}), Sky {6}{7}";

        return string.Format(descFormat,
            Math.Round(detail.TemperatureFahrenheit, 0),
            Math.Round(detail.DewPoint, 0),
            Math.Round(detail.RelativeHumidity, 0),
            Math.Round(detail.WindSpeedMph, 0),
            detail.WindDirection,
            Math.Round(detail.WindGustSpeedMph, 0),
            detail.WeatherCode,
            detail.PrecipitationInches > 0 ? ", some precip" : "");
    }

    private static OutputDetail GetAverageOfMultipleDetails(IEnumerable<OutputDetail> details)
    {
        var detailsList = details.Where(d => d != null).ToList();

        var temp = detailsList.Average(d => d.TemperatureFahrenheit);

        // How to average this.. Use max since that's kind of like the "worst"? Unless it's hot during the day then clear is probably worst.
        // Sometimes it will say overcast when it was really partly cloudy at worst, so lean down when hot to compensate for that.
        // The point is to know how the weather affected the effort so hot and clear sky makes it harder, cloudy makes it easier.
        var codes = detailsList.Select(d => d.WeatherCode).ToList();
        WMO_Code weatherCode = codes.Max();
        if (temp >= 50m)
        {
            if (weatherCode == WMO_Code.Overcast)
            {
                if (codes.Any(c => c <= WMO_Code.MainlyClear))
                    weatherCode = WMO_Code.MainlyClear;
                else if (codes.Any(c => c == WMO_Code.PartlyCloudy))
                    weatherCode = WMO_Code.PartlyCloudy;
            }
            else if (weatherCode == WMO_Code.PartlyCloudy)
            {
                if (codes.Any(c => c <= WMO_Code.MainlyClear))
                    weatherCode = WMO_Code.MainlyClear;
            }
        }

        return new OutputDetail
        {
            TemperatureFahrenheit = temp,
            DewPoint = detailsList.Average(d => d.DewPoint),
            RelativeHumidity = detailsList.Average(d => d.RelativeHumidity),
            WindSpeedMph = detailsList.Average(d => d.WindSpeedMph),
            WindGustSpeedMph = detailsList.Average(d => d.WindGustSpeedMph),
            WindDirectionDegrees = detailsList.First().WindDirectionDegrees, // TODO how to average wind direction.. could do a mode instead
            WeatherCode = weatherCode,
            PrecipitationInches = detailsList.Sum(d => d.PrecipitationInches)
        };
    }
}

public class SubscriptionChallenge
{
    [JsonPropertyName("hub.challenge")]
    public string Challenge { get; set; }
}

public class WebhookContent
{
    // Example
    /*
    {
        "aspect_type": "update",
        "event_time": 1716126040,
        "object_id": 1360128500,
        "object_type": "activity",
        "owner_id": 100000,
        "subscription_id": 120000,
        "updates": {
            "title": "Messy"
        }
    }
    */

    // create / update / delete
    [JsonPropertyName("aspect_type")]
    public string EventType { get; set; }

    // activity / athlete
    [JsonPropertyName("object_type")]
    public string ObjectType { get; set; }

    [JsonPropertyName("event_time")]
    public long EventTime { get; set; }

    [JsonPropertyName("object_id")]
    public long ObjectId { get; set; }

    [JsonPropertyName("owner_id")]
    public long AthleteId { get; set; }

    [JsonPropertyName("subscription_id")]
    public long SubscriptionId { get; set; }

    // For activity update events, keys can contain "title," "type," and "private,"
    // which is always "true" (activity visibility set to Only You)
    // or "false" (activity visibility set to Followers Only or Everyone).
    // For app deauthorization events, there is always an "authorized" : "false" key-value pair.
    [JsonPropertyName("updates")]
    public Dictionary<string, string> Updates { get; set; }
}
