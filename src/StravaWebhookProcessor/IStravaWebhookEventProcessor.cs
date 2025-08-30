using Microsoft.Extensions.Logging;

namespace StravaWebhookProcessor;

/// <summary>
/// Defines methods for what to do with each Strava webhook event
/// </summary>
public interface IStravaWebhookEventProcessor
{
    /// <summary>
    /// Whether this processor handles the <paramref name="eventType"/>
    /// </summary>
    /// <param name="eventType"></param>
    /// <returns></returns>
    bool HandleEventType(StravaWebhookEventType eventType);

    /// <summary>
    /// A method to process an activity event
    /// </summary>
    /// <param name="eventType">The type of the event</param>
    /// <param name="logger"></param>
    /// <param name="athleteId">The id of the athlete the event is for</param>
    /// <param name="activityId">The id of the activity the event is for</param>
    /// <returns></returns>
    Task ProcessActivityEvent(StravaWebhookEventType eventType, ILogger? logger, long athleteId, long activityId);
}
