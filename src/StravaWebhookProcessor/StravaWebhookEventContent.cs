using System.Text.Json.Serialization;

namespace StravaWebhookProcessor;

public class StravaWebhookEventContent
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

    [JsonPropertyName("aspect_type")]
    public string? EventType { get; set; }
    public StravaWebhookEventType StravaWebhookEventType => Enum.TryParse(EventType, out StravaWebhookEventType eventType) ? eventType : StravaWebhookEventType.Unknown;

    [JsonPropertyName("object_type")]
    public string? ObjectType { get; set; }
    public StravaWebhookObjectType StravaWebhookObjectType => Enum.TryParse(ObjectType, out StravaWebhookObjectType objectType) ? objectType : StravaWebhookObjectType.Unknown;

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
    public Dictionary<string, string> Updates { get; set; } = [];
}

public enum StravaWebhookEventType
{
    Unknown,
    Create,
    Delete,
    Update
}

public enum StravaWebhookObjectType
{
    Unknown,
    Activity,
    Athlete
}
