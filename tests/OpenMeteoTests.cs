using OpenMeteoIntegration;

namespace Tests;
public class OpenMeteoTests
{
    [Fact]
    public async Task Test1()
    {
        var client = new OpenMeteoClient();

        var input = new GetWeatherInput
        {
            Latitude = decimal.Parse("41.882629"),
            Longitude = decimal.Parse("-87.623474"),
            StartTime = DateTimeOffset.Parse("2024-01-01T12:00:00Z"),
            EndTime = DateTimeOffset.Parse("2024-01-01T16:00:00Z")
        };

        var output = await client.GetWeatherData(input);
        Assert.NotNull(output);
    }
}