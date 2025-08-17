using System.Text.Json.Serialization;

namespace StravaWebhookProcessor;

public class SubscriptionChallenge
{
    [JsonPropertyName("hub.challenge")]
    public string Challenge { get; set; }
}
