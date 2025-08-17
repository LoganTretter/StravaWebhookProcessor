using System.Text.Json.Serialization;

namespace OpenMeteoIntegration;

internal class ResponseRoot
{
    [JsonPropertyName("latitude")]
    public decimal Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public decimal Longitude { get; set; }

    [JsonPropertyName("elevation")]
    public decimal Elevation { get; set; }

    [JsonPropertyName("generationtime_ms")]
    public decimal GenerationTimeMilliseconds { get; set; }

    [JsonPropertyName("utc_offset_seconds")]
    public int UtcOffsetSeconds { get; set; }

    [JsonPropertyName("timezone")]
    public string TimeZone { get; set; }

    [JsonPropertyName("timezone_abbreviation")]
    public string TimeZoneAbbreviation { get; set; }

    [JsonPropertyName("minutely_15")]
    public ResponseDetails ResponseDetails { get; set; } = new();
}

internal class ResponseDetails
{
    [JsonPropertyName("time")]
    public string[] Times { get; set; } = [];

    [JsonPropertyName("temperature_2m")]
    public decimal[] TemperaturesAt2Meters { get; set; } = [];

    [JsonPropertyName("relative_humidity_2m")]
    public decimal[] RelativeHumiditiesAt2Meters { get; set; } = [];

    [JsonPropertyName("dew_point_2m")]
    public decimal[] DewPointsAt2Meters { get; set; } = [];

    [JsonPropertyName("precipitation")]
    public decimal[] Precipitations { get; set; } = [];

    [JsonPropertyName("weather_code")]
    public WMO_Code[] WeatherCodes { get; set; } = [];

    [JsonPropertyName("wind_speed_10m")]
    public decimal[] WindSpeedsAt10Meters { get; set; } = [];

    [JsonPropertyName("wind_direction_10m")]
    public decimal[] WindDirectionsAt10Meters { get; set; } = [];

    [JsonPropertyName("wind_gusts_10m")]
    public decimal[] WindGustsAt10Meters { get; set; } = [];
}
