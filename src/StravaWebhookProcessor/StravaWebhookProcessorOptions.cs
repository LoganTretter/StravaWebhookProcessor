namespace StravaWebhookProcessor;

public class StravaWebhookProcessorOptions
{
    public string KeyVaultUri { get; set; } = string.Empty;
    public long StravaAthleteId { get; set; } = long.MinValue;
    public long StravaWebhookSubscriptionId { get; set; } = long.MinValue;
    public string StravaWebhookSubscriptionVerificationToken { get; set; } = string.Empty;
}
