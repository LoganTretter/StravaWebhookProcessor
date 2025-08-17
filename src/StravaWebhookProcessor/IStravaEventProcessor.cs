using Microsoft.Extensions.Logging;
using StravaUtilities;

namespace StravaWebhookProcessor;

public interface IStravaEventProcessor
{
    /// <summary>
    /// Whether this processor handles activity creation events
    /// </summary>
    bool HandleActivityCreation { get; }

    /// <summary>
    /// Whether this processor handles activity update events
    /// </summary>
    bool HandleActivityUpdate { get; }

    /// <summary>
    /// Whether this processor handles activity deletion events
    /// </summary>
    bool HandleActivityDeletion { get; }

    /// <summary>
    /// A method to process an activity creation event
    /// </summary>
    /// <param name="stravaApiClient">An instance of a strava api client, already authenticated for the athlete</param>
    /// <param name="logger"></param>
    /// <param name="athleteId">The id of the athlete the event is for</param>
    /// <param name="activityId">The id of the activity the event is for</param>
    /// <returns></returns>
    Task ProcessActivityCreation(StravaApiClient stravaApiClient, ILogger logger, long athleteId, long activityId);

    /// <summary>
    /// A method to process an activity update event
    /// </summary>
    /// <param name="stravaApiClient">An instance of a strava api client, already authenticated for the athlete</param>
    /// <param name="logger"></param>
    /// <param name="athleteId">The id of the athlete the event is for</param>
    /// <param name="activityId">The id of the activity the event is for</param>
    Task ProcessActivityUpdate(StravaApiClient stravaApiClient, ILogger logger, long athleteId, long activityId);

    /// <summary>
    /// A method to process an activity deletion event
    /// </summary>
    /// <param name="stravaApiClient">An instance of a strava api client, already authenticated for the athlete</param>
    /// <param name="logger"></param>
    /// <param name="athleteId">The id of the athlete the event is for</param>
    /// <param name="activityId">The id of the activity the event is for</param>
    Task ProcessActivityDeletion(StravaApiClient stravaApiClient, ILogger logger, long athleteId, long activityId);
}
