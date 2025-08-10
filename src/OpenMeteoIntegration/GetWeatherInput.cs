namespace OpenMeteoIntegration;

public class GetWeatherInput
{
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
}