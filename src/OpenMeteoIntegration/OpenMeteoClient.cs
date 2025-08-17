using Microsoft.AspNetCore.WebUtilities;
using Polly;
using Polly.Extensions.Http;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Globalization;

namespace OpenMeteoIntegration;

/// <summary>
/// A client to get weather data from Open-Meteo
/// </summary>
public interface IOpenMeteoClient : IDisposable
{
    /// <summary>
    /// Gets weather data
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    Task<WeatherOutput> GetWeatherData(GetWeatherInput input);
}

/// <inheritdoc />
public class OpenMeteoClient : IOpenMeteoClient
{
    private const string BaseUrl = $"https://api.open-meteo.com";
    private const string ApiPath = "v1";
    private const string ForecastPath = "forecast";

    private static Uri BaseUri = new Uri(BaseUrl, UriKind.Absolute);
    private static Uri ApiUri = new Uri(ApiPath + "/", UriKind.Relative);

    private readonly Lazy<HttpClient> _lazyHttpClient = new Lazy<HttpClient>(() => new() { BaseAddress = new Uri(BaseUri, ApiUri) });

    private bool _disposed;

    private static readonly IAsyncPolicy<HttpResponseMessage> TransientRetryPolicy = HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.NotFound)
        .WaitAndRetryAsync(new[] { TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(800) });

    public async Task<WeatherOutput> GetWeatherData(GetWeatherInput input)
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
            { "start_minutely_15", input.StartTime.AddMinutes(-15).ToString("s") },
            { "end_minutely_15", input.EndTime.AddMinutes(15).ToString("s") }

        };
        var uri = new Uri(QueryHelpers.AddQueryString(new Uri(ForecastPath, UriKind.Relative).ToString(), queryParams), UriKind.Relative);

        using var response = await TransientRetryPolicy.ExecuteAsync(() => _lazyHttpClient.Value.GetAsync(uri)).ConfigureAwait(false);

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

    private static WeatherOutput CreateOutput(ResponseRoot response)
    {
        var output = new WeatherOutput();

        if (response?.ResponseDetails?.Times == null)
            return output;

        // Sometimes the times array has one more entry than the actual data arrays.
        // But the last time entry is the extra one, all the others correspond.

        for (int i = 0; i < response.ResponseDetails.TemperaturesAt2Meters.Length; i++)
        {
            string timeString = response.ResponseDetails.Times[i];
            var time = DateTimeOffset.ParseExact(timeString, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
            output.OutputDetails.Add(new WeatherOutputDetail
            {
                Time = time,
                TemperatureFahrenheit = response.ResponseDetails.TemperaturesAt2Meters[i],
                RelativeHumidityPercent = response.ResponseDetails.RelativeHumiditiesAt2Meters[i],
                DewPointFahrenheit = response.ResponseDetails.DewPointsAt2Meters[i],
                PrecipitationInches = response.ResponseDetails.Precipitations[i],
                WeatherCode = response.ResponseDetails.WeatherCodes[i],
                WindSpeedMph = response.ResponseDetails.WindSpeedsAt10Meters[i],
                WindGustSpeedMph = response.ResponseDetails.WindGustsAt10Meters[i],
                WindDirectionDegrees = response.ResponseDetails.WindDirectionsAt10Meters[i]
            });
        }

        return output;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                if (_lazyHttpClient.IsValueCreated)
                    _lazyHttpClient.Value?.Dispose();
            }

            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
