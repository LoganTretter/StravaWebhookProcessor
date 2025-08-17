using Microsoft.Extensions.Logging;
using OpenMeteoIntegration;
using StravaUtilities;

namespace StravaWebhookProcessor;

/// <inheritdoc />
public class StravaEventProcessor : IStravaEventProcessor
{
    public bool HandleActivityCreation { get; } = true;
    public bool HandleActivityUpdate { get; } = false;
    public bool HandleActivityDeletion { get; } = false;

    public async Task ProcessActivityCreation(StravaApiClient stravaApiClient, ILogger logger, long athleteId, long activityId)
    {
        var activity = await stravaApiClient.GetActivity(activityId).ConfigureAwait(false);

        // Indicator that this activity has already been processed. Can't set private notes so using description.
        if (activity.Description != null && activity.Description.Contains("~~"))
            return;

        // These indicate I have already processed these activities in some other way
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

        await stravaApiClient.UpdateActivity(updateInfo).ConfigureAwait(false);
    }

    public async Task ProcessActivityUpdate(StravaApiClient stravaApiClient, ILogger logger, long athleteId, long activityId)
    {
        return; // nothing for now
    }

    public async Task ProcessActivityDeletion(StravaApiClient stravaApiClient, ILogger logger, long athleteId, long activityId)
    {
        return; // nothing for now
    }

    private static async Task UpdateDescriptionWithWeatherInfo(Activity activity)
    {
        activity.Description += "~~";

        // Ideas:
        //   Handle different start and end locations. Should be fine for now since usually it's same.
        //   Get closest couple of weather stations to the start/end and avg them for the result.

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
