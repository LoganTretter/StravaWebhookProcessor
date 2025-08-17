namespace OpenMeteoIntegration;

/// <summary>
/// Input class for getting weather data
/// </summary>
public class GetWeatherInput
{
    /// <summary>
    /// The start time of the range to get weather data for.
    /// data will be returned for one time interval prior to this time.
    /// </summary>
    public DateTimeOffset StartTime { get; set; }

    /// <summary>
    /// The end time of the range to get weather data for.
    /// data will be returned for one time interval after this time.
    /// </summary>
    public DateTimeOffset EndTime { get; set; }

    /// <summary>
    /// The latitude to get weather data for
    /// </summary>
    public decimal Latitude { get; set; }

    /// <summary>
    /// The longitute to get weather data for
    /// </summary>
    public decimal Longitude { get; set; }
}