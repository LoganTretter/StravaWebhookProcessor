using Microsoft.AspNetCore.WebUtilities;
using Polly;
using Polly.Extensions.Http;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Globalization;

namespace OpenMeteoIntegration;

public class OpenMeteoClient
{
    private const string BaseUrl = $"https://api.open-meteo.com";
    private const string ApiPath = "v1";
    private const string ForecastPath = "forecast";

    private HttpClient _httpClient;

    private static readonly IAsyncPolicy<HttpResponseMessage> TransientRetryPolicy = HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.NotFound)
        .WaitAndRetryAsync(new[] { TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(800) });

    public OpenMeteoClient()
    {
        var baseUri = new Uri(BaseUrl, UriKind.Absolute);
        var apiUri = new Uri(ApiPath + "/", UriKind.Relative);
        _httpClient = new() { BaseAddress = new Uri(baseUri, apiUri) };
    }

    public async Task<Output> GetWeatherData(GetWeatherInput input)
    {
        var queryParams = new Dictionary<string, string?>()
        {
            { "latitude", input.Latitude.ToString() },
            { "longitude", input.Longitude.ToString() },
            { "minutely_15", "temperature_2m,relative_humidity_2m,dew_point_2m,precipitation,weather_code,wind_speed_10m,wind_direction_10m,wind_gusts_10m" },
            { "temperature_unit", "fahrenheit" },
            { "wind_speed_unit", "mph" },
            { "precipitation_unit", "inch" },
            { "timezone", "GMT" },
            { "start_minutely_15", input.Start.AddMinutes(-15).ToString("s") },
            { "end_minutely_15", input.End.AddMinutes(15).ToString("s") }

        };
        var uri = new Uri(QueryHelpers.AddQueryString(new Uri(ForecastPath, UriKind.Relative).ToString(), queryParams), UriKind.Relative);

        using var response = await TransientRetryPolicy.ExecuteAsync(() => _httpClient.GetAsync(uri)).ConfigureAwait(false);

        var responseObject = await ParseResponse<ResponseRoot>(response, uri.ToString()).ConfigureAwait(false);

        var output = CreateOutput(responseObject);

        return output;
    }

    private static async Task<T> ParseResponse<T>(HttpResponseMessage response, string pathUsed)
    {
        await EnsureResponseSuccess(response, pathUsed).ConfigureAwait(false);

        var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (string.IsNullOrEmpty(responseString))
            throw new ApplicationException($"Problem reading API call response for path: '{pathUsed}'. Status is {(int)response.StatusCode} {response.StatusCode} but response content was empty.");

        try
        {
            var options = new JsonSerializerOptions();
            options.Converters.Add(new JsonStringEnumConverter());
            var item = JsonSerializer.Deserialize<T>(responseString, options);
            return item;
        }
        catch (Exception ex)
        {
            throw new ApplicationException($"Problem deserializing API call response for path: '{pathUsed}' - {ex.Message}", ex);
        }
    }

    private static async Task EnsureResponseSuccess(HttpResponseMessage response, string pathUsed)
    {
        if (response.IsSuccessStatusCode)
            return;

        string message = $"{(int)response.StatusCode} {response.StatusCode} error in API call for path: '{pathUsed}'";

        var responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (string.IsNullOrEmpty(responseContent))
            message += $"{Environment.NewLine}Response did not specify additional info.";
        else
            message += $"{Environment.NewLine}Response content: {responseContent}";

        throw new ApplicationException(message);
    }

    private static Output CreateOutput(ResponseRoot response)
    {
        var output = new Output();

        if (response?.minutely_15?.time == null)
            return output;

        // Sometimes time has one more entry than the actual data arrays.
        // But the last time entry is the extra one, all the others correspond.

        for (int i = 0; i < response.minutely_15.temperature_2m.Length; i++)
        {
            string timeString = response.minutely_15.time[i];
            var time = DateTimeOffset.ParseExact(timeString, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
            output.OutputDetails.Add(new OutputDetail
            {
                OutputDetailTime = time,
                TemperatureFahrenheit = response.minutely_15.temperature_2m[i],
                RelativeHumidity = response.minutely_15.relative_humidity_2m[i],
                DewPoint = response.minutely_15.dew_point_2m[i],
                PrecipitationInches = response.minutely_15.precipitation[i],
                WeatherCode = response.minutely_15.weather_code[i],
                WindSpeedMph = response.minutely_15.wind_speed_10m[i],
                WindGustSpeedMph = response.minutely_15.wind_gusts_10m[i],
                WindDirectionDegrees = response.minutely_15.wind_direction_10m[i]
            });
        }

        return output;
    }
}

public class ResponseRoot
{
    public decimal latitude { get; set; }
    public decimal longitude { get; set; }
    public decimal elevation { get; set; }
    public decimal generationtime_ms { get; set; }
    public int utc_offset_seconds { get; set; }
    public string timezone { get; set; }
    public string timezone_abbreviation { get; set; }
    public ResponseDetails minutely_15 { get; set; }
}

public class ResponseDetails
{
    public string[] time { get; set; }
    public decimal[] temperature_2m { get; set; }
    public decimal[] relative_humidity_2m { get; set; }
    public decimal[] dew_point_2m { get; set; }
    public decimal[] precipitation { get; set; }
    public WMO_Code[] weather_code { get; set; }
    public decimal[] wind_speed_10m { get; set; }
    public decimal[] wind_direction_10m { get; set; }
    public decimal[] wind_gusts_10m { get; set; }
}