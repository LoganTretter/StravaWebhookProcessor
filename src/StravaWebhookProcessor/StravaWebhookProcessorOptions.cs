using System.ComponentModel.DataAnnotations;

namespace StravaWebhookProcessor;

public class StravaWebhookProcessorOptions
{
    [Required(AllowEmptyStrings = false)]
    public required string KeyVaultUri { get; set; }

    [Required(AllowEmptyStrings = false)]
    public required string StravaApiClientId { get; set; }

    [Required(AllowEmptyStrings = false)]
    public required string StravaApiClientSecret { get; set; }

    [Required]
    [Range(0, long.MaxValue)]
    public required long StravaAthleteId { get; set; } = long.MinValue;

    [Required]
    [Range(0, long.MaxValue)]
    public required long StravaWebhookSubscriptionId { get; set; } = long.MinValue;

    [Required(AllowEmptyStrings = false)]
    public required string StravaWebhookSubscriptionVerificationToken { get; set; }
}
